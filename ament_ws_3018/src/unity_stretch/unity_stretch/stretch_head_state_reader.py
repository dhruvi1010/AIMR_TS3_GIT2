#!/usr/bin/env python3
import rclpy
from rclpy.node import Node
from sensor_msgs.msg import JointState

class StretchHeadStateReader(Node):
    def __init__(self):
        super().__init__('stretch_head_state_reader')
        
        # Subscribe to Stretch's joint states
        self.subscription = self.create_subscription(
            JointState,
            '/joint_states',
            self.joint_state_callback,
            10
        )
        
        # Optional: Republish just head joints for Unity/other consumers
        self.head_state_publisher = self.create_publisher(
            JointState,
            '/stretch_head_state',
            10
        )
        
        self.get_logger().info(' Stretch head state reader started!')
        self.get_logger().info('Listening on /joint_states...')
        
        # Store last known head positions
        self.last_pan = None
        self.last_tilt = None
    
    def joint_state_callback(self, msg):
        """Read current head pan/tilt from joint states"""
        
        try:
            # Find head pan and tilt indices
            pan_idx = msg.name.index('joint_head_pan')
            tilt_idx = msg.name.index('joint_head_tilt')
            
            current_pan = msg.position[pan_idx]
            current_tilt = msg.position[tilt_idx]
            
            # Only log if values changed significantly (reduce spam)
            if (self.last_pan is None or 
                abs(current_pan - self.last_pan) > 0.01 or 
                abs(current_tilt - self.last_tilt) > 0.01):
                
                self.get_logger().info(
                    f'Stretch_Head State: '
                    f'pan={current_pan:.3f} rad ({current_pan*57.3:.1f}°), '
                    f'tilt={current_tilt:.3f} rad ({current_tilt*57.3:.1f}°)'
                )
                
                self.last_pan = current_pan
                self.last_tilt = current_tilt
            
            # Publish filtered head-only state
            head_state = JointState()
            head_state.header = msg.header
            head_state.name = ['joint_head_pan', 'joint_head_tilt']
            head_state.position = [current_pan, current_tilt]
            
            # Include velocities and efforts if available
            if len(msg.velocity) > max(pan_idx, tilt_idx):
                head_state.velocity = [msg.velocity[pan_idx], msg.velocity[tilt_idx]]
            if len(msg.effort) > max(pan_idx, tilt_idx):
                head_state.effort = [msg.effort[pan_idx], msg.effort[tilt_idx]]
            
            self.head_state_publisher.publish(head_state)
            
        except ValueError:
            self.get_logger().warn(
                ' Head joints not found in /joint_states. '
                'Available joints: ' + ', '.join(msg.name)
            )

def main(args=None):
    rclpy.init(args=args)
    reader = StretchHeadStateReader()
    
    try:
        rclpy.spin(reader)
    except KeyboardInterrupt:
        reader.get_logger().info('Head state reader shutting down...')
    finally:
        reader.destroy_node()
        rclpy.shutdown()

if __name__ == '__main__':
    main()