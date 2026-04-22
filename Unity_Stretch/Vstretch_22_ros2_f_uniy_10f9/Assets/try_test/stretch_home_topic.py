#!/usr/bin/env python3
"""
ROS2 Topic Subscriber for Stretch Robot Homing (Alternative to Service)
This is simpler and follows the same pattern as unity_lift.py

Usage:
    python3 stretch_home_topic.py

Topic:
    Topic Name: /stretch/home_command
    Topic Type: std_msgs/msg/String
    Any message on this topic triggers homing

Status Topic:
    Topic Name: /stretch/home_status
    Topic Type: std_msgs/msg/String
    Publishes status: "homing_started", "homing_complete", "homing_failed: <error>"
"""

import rclpy
from rclpy.node import Node
from std_msgs.msg import String
import stretch_body.robot as robot
import threading


class StretchHomeTopic(Node):
    def __init__(self):
        super().__init__('stretch_home_topic')
        
        # Subscribe to home command topic
        self.subscription = self.create_subscription(
            String,
            '/stretch/home_command',
            self.home_command_callback,
            10
        )
        
        # Publish home status
        self.status_publisher = self.create_publisher(
            String,
            '/stretch/home_status',
            10
        )
        
        self.get_logger().info('Stretch Home Topic subscriber started')
        self.get_logger().info('Listening on /stretch/home_command')
        self.get_logger().info('Publishing status to /stretch/home_status')
        
        # Robot instance
        self.robot = None
        self.is_homing = False
        self.homing_lock = threading.Lock()
    
    def home_command_callback(self, msg):
        """
        Callback when home command is received
        NOTE: This runs on ROS2 callback thread, but robot.home() must run on main thread
        We use a timer to execute homing on the main thread
        """
        with self.homing_lock:
            if self.is_homing:
                status_msg = String()
                status_msg.data = "homing_failed: Already homing"
                self.status_publisher.publish(status_msg)
                self.get_logger().warn("Home command rejected: already homing")
                return
            
            self.is_homing = True
        
        # Publish started status
        status_msg = String()
        status_msg.data = "homing_started"
        self.status_publisher.publish(status_msg)
        self.get_logger().info("Home command received, starting homing...")
        
        # Execute homing synchronously in this callback
        # This blocks the callback but robot.home() must run on main thread
        # Alternative: Use a timer or executor, but for simplicity we do it here
        self.execute_homing()
    
    def execute_homing(self):
        """
        Execute the actual homing sequence
        NOTE: This must run on the main thread (not a background thread)
        because robot.home() uses signals which only work in main thread
        
        After homing, the robot is stopped and released to match stretch_robot_home.py behavior.
        This allows stretch_driver to use the robot after homing.
        """
        robot_instance = None
        try:
            # Initialize robot if needed
            if self.robot is None:
                self.get_logger().info("Initializing robot...")
                self.robot = robot.Robot()
                if not self.robot.startup():
                    raise Exception("Failed to startup robot hardware")
                self.get_logger().info("Robot initialized")
            
            robot_instance = self.robot
            
            # Check for runstop
            if robot_instance.pimu.status['runstop_event']:
                raise Exception("Cannot home while run-stopped. Please clear runstop first.")
            
            # Execute homing (this blocks for ~30 seconds)
            self.get_logger().info("Executing homing sequence...")
            robot_instance.home()
            self.get_logger().info("Homing completed successfully")
            
            # Publish success
            status_msg = String()
            status_msg.data = "homing_complete"
            self.status_publisher.publish(status_msg)
            
        except Exception as e:
            error_msg = f"homing_failed: {str(e)}"
            self.get_logger().error(error_msg)
            
            status_msg = String()
            status_msg.data = error_msg
            self.status_publisher.publish(status_msg)
            
        finally:
            # IMPORTANT: Stop and release robot after homing (matches stretch_robot_home.py behavior)
            # This allows stretch_driver to use the robot after homing
            if robot_instance is not None:
                try:
                    self.get_logger().info("Stopping robot and releasing hardware...")
                    robot_instance.stop()
                    self.get_logger().info("Robot stopped and hardware released")
                except Exception as e:
                    self.get_logger().error(f"Error stopping robot: {e}")
                finally:
                    # Release robot instance so hardware is free for other processes
                    self.robot = None
            
            self.is_homing = False
    
    def shutdown_robot(self):
        """Safely shutdown robot on node destruction"""
        if self.robot is not None:
            try:
                self.get_logger().info("Shutting down robot...")
                self.robot.stop()
            except Exception as e:
                self.get_logger().error(f"Error during robot shutdown: {e}")
            finally:
                self.robot = None


def main(args=None):
    rclpy.init(args=args)
    
    home_node = StretchHomeTopic()
    
    try:
        rclpy.spin(home_node)
    except KeyboardInterrupt:
        home_node.get_logger().info('Shutting down Stretch Home Topic node...')
    finally:
        home_node.shutdown_robot()
        home_node.destroy_node()
        rclpy.shutdown()


if __name__ == '__main__':
    main()
