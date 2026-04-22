#!/usr/bin/env python3
"""
ROS2 Service Server for Stretch Robot Homing
Provides a ROS2 service that calls stretch_body's home() method

This script can run alongside unity_lift.py on the Stretch 3 robot.

Usage:
    python3 stretch_home_service.py

Service:
    Service Name: /stretch/home
    Service Type: std_srvs/srv/Trigger
    Request: Empty (no parameters needed)
    Response: 
        success (bool): True if homing succeeded
        message (string): Status message

Alternative Topic-Based Approach:
    Topic: /stretch/home_command (std_msgs/msg/String)
    Subscribe to this topic and call home() when message received
"""

import rclpy
from rclpy.node import Node
from std_srvs.srv import Trigger
import stretch_body.robot as robot
import sys
import threading


class StretchHomeService(Node):
    def __init__(self):
        super().__init__('stretch_home_service')
        
        # Create service
        self.srv = self.create_service(
            Trigger,
            '/stretch/home',
            self.home_callback
        )
        
        self.get_logger().info('Stretch Home Service started on /stretch/home')
        self.get_logger().info('Waiting for home requests...')
        
        # Robot instance (will be initialized on first call)
        self.robot = None
        self.is_homing = False
        self.homing_lock = threading.Lock()
    
    def home_callback(self, request, response):
        """
        Service callback that executes the homing sequence
        """
        with self.homing_lock:
            if self.is_homing:
                response.success = False
                response.message = "Homing already in progress"
                self.get_logger().warn("Home request rejected: already homing")
                return response
            
            self.is_homing = True
        
        try:
            self.get_logger().info("Home request received, initializing robot...")
            
            # Initialize robot if not already done
            if self.robot is None:
                self.robot = robot.Robot()
                if not self.robot.startup():
                    response.success = False
                    response.message = "Failed to startup robot hardware"
                    self.get_logger().error("Robot startup failed")
                    self.is_homing = False
                    return response
                self.get_logger().info("Robot initialized successfully")
            
            # Check for runstop
            if self.robot.pimu.status['runstop_event']:
                response.success = False
                response.message = "Cannot home while run-stopped. Please clear runstop first."
                self.get_logger().error("Cannot home: runstop is active")
                self.is_homing = False
                return response
            
            # Execute homing
            self.get_logger().info("Starting homing sequence...")
            self.robot.home()
            self.get_logger().info("Homing sequence completed successfully")
            
            response.success = True
            response.message = "Homing completed successfully"
            
        except Exception as e:
            error_msg = f"Homing failed: {str(e)}"
            self.get_logger().error(error_msg)
            response.success = False
            response.message = error_msg
            
        finally:
            self.is_homing = False
        
        return response
    
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
    
    home_service = StretchHomeService()
    
    try:
        rclpy.spin(home_service)
    except KeyboardInterrupt:
        home_service.get_logger().info('Shutting down Stretch Home Service...')
    finally:
        home_service.shutdown_robot()
        home_service.destroy_node()
        rclpy.shutdown()


if __name__ == '__main__':
    main()
