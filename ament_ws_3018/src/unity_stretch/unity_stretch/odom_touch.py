#!/usr/bin/env python3
"""
Simple bridge that forwards ROS 2 data to Unity via UDP
Run alongside stretch_driver

Installation: No additional packages needed (uses standard library)
Usage: python3 unity_bridge.py
"""

import rclpy
from rclpy.node import Node
from sensor_msgs.msg import JointState
from nav_msgs.msg import Odometry
from geometry_msgs.msg import Twist
import socket
import json

class UnityBridge(Node):
    def __init__(self):
        super().__init__('unity_bridge')
        
        # ============================================
        # CONFIGURATION - MODIFY THESE IF NEEDED
        # ============================================
        self.unity_ip = '172.31.1.90'  # Change to Unity PC IP if on different machine
        self.send_port = 5005        # Port Unity listens on (for receiving robot state)
        self.receive_port = 5006     # Port this bridge listens on (for receiving Unity commands)
        # ============================================
        
        # Socket for sending data TO Unity
        self.send_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.get_logger().info(f'Sending robot state to Unity at {self.unity_ip}:{self.send_port}')
        
        # Socket for receiving commands FROM Unity
        self.receive_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.receive_sock.bind(('0.0.0.0', self.receive_port))
        self.receive_sock.setblocking(False)  # Non-blocking mode
        self.get_logger().info(f'Listening for Unity commands on port {self.receive_port}')
        
        # Subscribe to robot state topics
        self.joint_sub = self.create_subscription(
            JointState, '/joint_states', self.joint_callback, 10)
        self.odom_sub = self.create_subscription(
            Odometry, '/odom', self.odom_callback, 10)
        
        # Publisher for cmd_vel (commands from Unity to robot)
        self.cmd_vel_pub = self.create_publisher(Twist, '/stretch/cmd_vel', 10)
        
        # Timer to check for Unity commands (100Hz)
        self.create_timer(0.01, self.check_unity_commands)
        
        self.get_logger().info('Unity Bridge started successfully !!!')
        self.get_logger().info('Waiting for /joint_states and /odom topics...')
    
    def joint_callback(self, msg):
        """Forward joint states to Unity"""
        self.get_logger().info(f'Recieved {len(msg)}')
        data = {
            'type': 'joint_states',
            'names': msg.name,
            'positions': list(msg.position),
            'velocities': list(msg.velocity) if msg.velocity else [],
            'efforts': list(msg.effort) if msg.effort else []
        }
        self.send_to_unity(data)
    
    def odom_callback(self, msg):
        """Forward odometry to Unity"""
        self.get_logger().info(f'sending odom x= {msg.pose.pose.position.x:.2f}')
        data = {
            'type': 'odom',
            'position': {
                'x': msg.pose.pose.position.x,
                'y': msg.pose.pose.position.y,
                'z': msg.pose.pose.position.z
            },
            'orientation': {
                'x': msg.pose.pose.orientation.x,
                'y': msg.pose.pose.orientation.y,
                'z': msg.pose.pose.orientation.z,
                'w': msg.pose.pose.orientation.w
            },
            'linear_velocity': {
                'x': msg.twist.twist.linear.x,
                'y': msg.twist.twist.linear.y,
                'z': msg.twist.twist.linear.z
            },
            'angular_velocity': {
                'x': msg.twist.twist.angular.x,
                'y': msg.twist.twist.angular.y,
                'z': msg.twist.twist.angular.z
            }
        }
        self.send_to_unity(data)
    
    def send_to_unity(self, data):
        """Send JSON data to Unity via UDP"""
        try:
            json_data = json.dumps(data).encode('utf-8')
            self.send_sock.sendto(json_data, (self.unity_ip, self.send_port))
        except Exception as e:
            self.get_logger().error(f'Error sending to Unity: {e}')
    
    def check_unity_commands(self):
        """Receive commands from Unity and publish to ROS"""
        try:
            data, addr = self.receive_sock.recvfrom(4096)  # 4KB buffer
            cmd = json.loads(data.decode('utf-8'))
            
            if cmd['type'] == 'cmd_vel':
                twist = Twist()
                twist.linear.x = float(cmd['linear'])
                twist.angular.z = float(cmd['angular'])
                self.cmd_vel_pub.publish(twist)
                # Uncomment for debugging:
                # self.get_logger().info(f'Published cmd_vel: linear={twist.linear.x}, angular={twist.angular.z}')
                
        except BlockingIOError:
            pass  # No data available (expected in non-blocking mode)
        except json.JSONDecodeError as e:
            self.get_logger().error(f'Invalid JSON from Unity: {e}')
        except Exception as e:
            self.get_logger().error(f'Error receiving from Unity: {e}')

def main():
    rclpy.init()
    bridge = UnityBridge()
    
    try:
        rclpy.spin(bridge)
    except KeyboardInterrupt:
        bridge.get_logger().info('Shutting down Unity Bridge...')
    finally:
        bridge.destroy_node()
        rclpy.shutdown()

if __name__ == '__main__':
    main()