#!/usr/bin/env python3
import rclpy
from rclpy.node import Node
from rclpy.action import ActionClient
from control_msgs.action import FollowJointTrajectory
from trajectory_msgs.msg import JointTrajectory

class UnityToStretchBridge(Node):
    def __init__(self):
        super().__init__('unity_stretch_lift_bridge')
        
        # Subscribe to Unity's simple topic
        self.subscription = self.create_subscription(
            JointTrajectory,
            '/stretch_controller/follow_joint_trajectory/goal',
            self.trajectory_callback,
            10
        )
        
        # Create action client for Stretch robot
        self.action_client = ActionClient(
            self,
            FollowJointTrajectory,
            '/stretch_controller/follow_joint_trajectory'
        )
        
        self.get_logger().info('unity_stretch_lift_bridge, waiting for action server...')
        self.action_client.wait_for_server()
        self.get_logger().info('Action server connected!')
    
    def trajectory_callback(self, msg):
        """Convert Unity's JointTrajectory topic to action goal"""
        
        # Wrap the trajectory in an action Goal message
        goal_msg = FollowJointTrajectory.Goal()
        goal_msg.trajectory = msg
        
        # Send to robot action server
        # self.get_logger().info(
        #     f'Sending head command: '
        #     f'pan={msg.points[0].positions[0]:.3f} rad ({msg.points[0].positions[0]:.1f}°), '
        #     f'tilt={msg.points[0].positions[1]:.3f} rad ({msg.points[0].positions[1]:.1f}°)'
        # )

        # Log what unity is sending
        positions_str = ', '.join([f'{msg.joint_names[i]}={msg.points[0].positions[i]:.3f}' 
                               for i in range(len(msg.points[0].positions))])
        self.get_logger().info(f'Sending command: {positions_str}')
        
        send_goal_future = self.action_client.send_goal_async(goal_msg)
        send_goal_future.add_done_callback(self.goal_response_callback)
    
    def goal_response_callback(self, future):
        goal_handle = future.result()
        if not goal_handle.accepted:
            self.get_logger().warn('Goal rejected by robot !!!')
            return
        
        # Optionally get result (not required for continuous control)
        # result_future = goal_handle.get_result_async()
        # result_future.add_done_callback(self.result_callback)
    
    def result_callback(self, future):
        result = future.result().result
        if result.error_code == 0:
            self.get_logger().info('Goal achieved')
        else:
            self.get_logger().warn(f'Goal failed: {result.error_string}')

def main(args=None):
    rclpy.init(args=args)
    bridge = UnityToStretchBridge()
    
    try:
        rclpy.spin(bridge)
    except KeyboardInterrupt:
        bridge.get_logger().info('Bridge shutting down...')
    finally:
        bridge.destroy_node()
        rclpy.shutdown()

if __name__ == '__main__':
    main()
