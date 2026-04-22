 !/usr/bin/env python3
"""
Reference Implementation: stretch_gamepad_teleop.py
Based on Stretch3 ROS2 gamepad teleoperation

This is a reference implementation showing how stretch_gamepad_teleop.py works.
The actual implementation may vary, but this demonstrates the core concepts.
"""

import rclpy
from rclpy.node import Node
from sensor_msgs.msg import Joy
from geometry_msgs.msg import Twist
from std_msgs.msg import Float64
from control_msgs.action import FollowJointTrajectory
from trajectory_msgs.msg import JointTrajectory, JointTrajectoryPoint
from rclpy.action import ActionClient
from builtin_interfaces.msg import Duration


class StretchGamepadTeleop(Node):
    """
    Main gamepad teleoperation node for Stretch robot.
    
    Maps gamepad inputs to robot joint commands with safety limits.
    """
    
    def __init__(self):
        super().__init__('stretch_gamepad_teleop')
        
        # ============================================
        # PARAMETERS & LIMITS
        # ============================================
        
        # Base limits
        self.max_linear_velocity = 0.2  # m/s
        self.max_angular_velocity = 0.5  # rad/s
        self.base_timeout = 1.0  # seconds
        
        # Lift limits (from URDF)
        self.lift_min = 0.0  # meters
        self.lift_max = 1.1  # meters
        self.lift_speed = 0.01  # m/s per joystick unit
        
        # Arm limits (from URDF)
        self.arm_min = 0.0  # meters
        self.arm_max = 0.52  # meters
        self.arm_speed = 0.01  # m/s per joystick unit
        
        # Wrist limits (from URDF)
        self.wrist_yaw_min = -2.967  # radians (-170 degrees)
        self.wrist_yaw_max = 2.967   # radians (+170 degrees)
        self.wrist_yaw_speed = 0.1  # rad/s per joystick unit
        
        self.wrist_pitch_min = -0.873  # radians (-50 degrees)
        self.wrist_pitch_max = 0.873   # radians (+50 degrees)
        self.wrist_pitch_speed = 0.1  # rad/s
        
        self.wrist_roll_min = -2.967  # radians (-170 degrees)
        self.wrist_roll_max = 2.967   # radians (+170 degrees)
        self.wrist_roll_speed = 0.1  # rad/s
        
        # Head limits (from URDF)
        self.head_pan_min = -3.02  # radians (-173 degrees)
        self.head_pan_max = 3.02   # radians (+173 degrees)
        self.head_pan_speed = 0.1  # rad/s
        
        self.head_tilt_min = -0.087  # radians (-5 degrees)
        self.head_tilt_max = 1.92    # radians (+110 degrees)
        self.head_tilt_speed = 0.1  # rad/s
        
        # Gripper limits
        self.gripper_min = -100.0  # fully open
        self.gripper_max = 100.0   # fully closed
        self.gripper_speed = 5.0   # units per second
        
        # Current joint positions (updated from joint_states)
        self.current_lift = 0.0
        self.current_arm = 0.0
        self.current_wrist_yaw = 0.0
        self.current_wrist_pitch = 0.0
        self.current_wrist_roll = 0.0
        self.current_head_pan = 0.0
        self.current_head_tilt = 0.0
        self.current_gripper = 0.0
        
        # ============================================
        # PUBLISHERS
        # ============================================
        
        # Base velocity publisher
        self.base_pub = self.create_publisher(
            Twist,
            '/stretch/cmd_vel',
            10
        )
        
        # Individual joint velocity publishers (alternative to trajectory)
        self.lift_pub = self.create_publisher(
            Float64,
            '/stretch/joint_lift/cmd',
            10
        )
        
        self.arm_pub = self.create_publisher(
            Float64,
            '/stretch/joint_arm_l0/cmd',
            10
        )
        
        self.wrist_yaw_pub = self.create_publisher(
            Float64,
            '/stretch/joint_wrist_yaw/cmd',
            10
        )
        
        self.wrist_pitch_pub = self.create_publisher(
            Float64,
            '/stretch/joint_wrist_pitch/cmd',
            10
        )
        
        self.wrist_roll_pub = self.create_publisher(
            Float64,
            '/stretch/joint_wrist_roll/cmd',
            10
        )
        
        self.head_pan_pub = self.create_publisher(
            Float64,
            '/stretch/joint_head_pan/cmd',
            10
        )
        
        self.head_tilt_pub = self.create_publisher(
            Float64,
            '/stretch/joint_head_tilt/cmd',
            10
        )
        
        self.gripper_pub = self.create_publisher(
            Float64,
            '/stretch/gripper_motor/cmd_gripper',
            10
        )
        
        # Trajectory action client (for position control)
        self.trajectory_client = ActionClient(
            self,
            FollowJointTrajectory,
            '/stretch_controller/follow_joint_trajectory'
        )
        
        # ============================================
        # SUBSCRIBERS
        # ============================================
        
        # Gamepad input
        self.joy_sub = self.create_subscription(
            Joy,
            '/joy',
            self.joy_callback,
            10
        )
        
        # Joint states (to know current positions)
        from sensor_msgs.msg import JointState
        self.joint_state_sub = self.create_subscription(
            JointState,
            '/stretch/joint_states',
            self.joint_state_callback,
            10
        )
        
        # ============================================
        # TIMERS
        # ============================================
        
        # Timer for base timeout check
        self.base_timeout_timer = self.create_timer(0.1, self.check_base_timeout)
        self.last_base_command_time = self.get_clock().now()
        
        # Timer for command publishing (rate limiting)
        self.command_timer = self.create_timer(0.05, self.publish_commands)  # 20 Hz
        
        self.get_logger().info('Stretch Gamepad Teleop initialized')
        self.get_logger().info('Waiting for action server...')
        self.trajectory_client.wait_for_server()
        self.get_logger().info('Action server ready!')
    
    # ============================================
    # GAMEPAD INPUT PROCESSING
    # ============================================
    
    def joy_callback(self, msg):
        """
        Process gamepad input and update command targets.
        
        Typical Xbox controller mapping:
        - axes[0]: Left stick X (base rotation)
        - axes[1]: Left stick Y (base forward/back)
        - axes[2]: Right stick X (arm extend/retract)
        - axes[3]: Right stick Y (lift up/down)
        - axes[4]: Left trigger
        - axes[5]: Right trigger
        - buttons[0]: A button
        - buttons[1]: B button (gripper open)
        - buttons[2]: X button (gripper close)
        - buttons[3]: Y button
        - buttons[4]: Left bumper
        - buttons[5]: Right bumper
        - buttons[6]: Back button
        - buttons[7]: Start button
        - buttons[8]: Left stick press
        - buttons[9]: Right stick press
        """
        
        # Store latest gamepad state
        self.joy_msg = msg
        
        # Process base movement (left stick)
        self.process_base_input(msg)
        
        # Process lift (right stick Y or triggers)
        self.process_lift_input(msg)
        
        # Process arm (right stick X)
        self.process_arm_input(msg)
        
        # Process wrist (bumpers or buttons)
        self.process_wrist_input(msg)
        
        # Process head (D-pad or buttons)
        self.process_head_input(msg)
        
        # Process gripper (buttons)
        self.process_gripper_input(msg)
    
    # ============================================
    # JOINT COMMAND PROCESSING
    # ============================================
    
    def process_base_input(self, msg):
        """Process base movement from left joystick."""
        if len(msg.axes) < 2:
            return
        
        # Left stick: axes[0] = X (rotation), axes[1] = Y (forward/back)
        linear_input = msg.axes[1]  # Forward/backward
        angular_input = msg.axes[0]  # Left/right rotation
        
        # Apply deadzone
        if abs(linear_input) < 0.1:
            linear_input = 0.0
        if abs(angular_input) < 0.1:
            angular_input = 0.0
        
        # Calculate velocities with limits
        self.target_linear_vel = linear_input * self.max_linear_velocity
        self.target_angular_vel = angular_input * self.max_angular_velocity
        
        # Update last command time
        self.last_base_command_time = self.get_clock().now()
    
    def process_lift_input(self, msg):
        """Process lift movement from right joystick Y or triggers."""
        if len(msg.axes) < 4:
            return
        
        # Right stick Y-axis (axes[3]) or triggers
        lift_input = 0.0
        
        # Option 1: Right stick Y
        if len(msg.axes) > 3:
            lift_input = msg.axes[3]
        
        # Option 2: Triggers (if right stick not used)
        # lift_input = (msg.axes[5] - msg.axes[4]) / 2.0  # Right - Left trigger
        
        # Apply deadzone
        if abs(lift_input) < 0.1:
            lift_input = 0.0
        
        # Calculate target position increment
        dt = 0.05  # Approximate update rate
        position_delta = lift_input * self.lift_speed * dt
        
        # Update target position with limits
        self.target_lift = self.current_lift + position_delta
        self.target_lift = max(min(self.target_lift, self.lift_max), self.lift_min)
    
    def process_arm_input(self, msg):
        """Process arm extension from right joystick X."""
        if len(msg.axes) < 3:
            return
        
        # Right stick X-axis (axes[2])
        arm_input = msg.axes[2]
        
        # Apply deadzone
        if abs(arm_input) < 0.1:
            arm_input = 0.0
        
        # Calculate target position increment
        dt = 0.05
        position_delta = arm_input * self.arm_speed * dt
        
        # Update target position with limits
        self.target_arm = self.current_arm + position_delta
        self.target_arm = max(min(self.target_arm, self.arm_max), self.arm_min)
    
    def process_wrist_input(self, msg):
        """Process wrist movement from bumpers or buttons."""
        if len(msg.buttons) < 6:
            return
        
        # Wrist Yaw: Left/Right bumper or stick
        wrist_yaw_input = 0.0
        if msg.buttons[4]:  # Left bumper
            wrist_yaw_input = -1.0
        elif msg.buttons[5]:  # Right bumper
            wrist_yaw_input = 1.0
        
        # Calculate target position
        dt = 0.05
        position_delta = wrist_yaw_input * self.wrist_yaw_speed * dt
        self.target_wrist_yaw = self.current_wrist_yaw + position_delta
        self.target_wrist_yaw = max(
            min(self.target_wrist_yaw, self.wrist_yaw_max),
            self.wrist_yaw_min
        )
        
        # Wrist Pitch: (example - could use different buttons)
        # Similar implementation...
        
        # Wrist Roll: (example - could use different buttons)
        # Similar implementation...
    
    def process_head_input(self, msg):
        """Process head movement from D-pad or buttons."""
        # Head Pan/Tilt typically uses D-pad
        # Implementation depends on gamepad driver mapping
        # This is a simplified version
        pass
    
    def process_gripper_input(self, msg):
        """Process gripper from buttons."""
        if len(msg.buttons) < 3:
            return
        
        gripper_input = 0.0
        
        if msg.buttons[1]:  # B button - open
            gripper_input = -1.0
        elif msg.buttons[2]:  # X button - close
            gripper_input = 1.0
        
        # Calculate target position
        dt = 0.05
        position_delta = gripper_input * self.gripper_speed * dt
        self.target_gripper = self.current_gripper + position_delta
        self.target_gripper = max(
            min(self.target_gripper, self.gripper_max),
            self.gripper_min
        )
    
    # ============================================
    # COMMAND PUBLISHING
    # ============================================
    
    def publish_commands(self):
        """Publish commands to robot at controlled rate."""
        
        # Publish base velocity
        if hasattr(self, 'target_linear_vel') and hasattr(self, 'target_angular_vel'):
            twist = Twist()
            twist.linear.x = self.target_linear_vel
            twist.angular.z = self.target_angular_vel
            self.base_pub.publish(twist)
        
        # Publish lift command (velocity mode)
        if hasattr(self, 'target_lift'):
            # Option 1: Velocity command
            lift_vel = (self.target_lift - self.current_lift) / 0.05  # Approximate velocity
            lift_vel = max(min(lift_vel, 0.1), -0.1)  # Limit velocity
            lift_msg = Float64()
            lift_msg.data = lift_vel
            self.lift_pub.publish(lift_msg)
            
            # Option 2: Position command via trajectory (commented out)
            # self.send_trajectory_command(['joint_lift'], [self.target_lift])
        
        # Publish arm command
        if hasattr(self, 'target_arm'):
            arm_vel = (self.target_arm - self.current_arm) / 0.05
            arm_vel = max(min(arm_vel, 0.1), -0.1)
            arm_msg = Float64()
            arm_msg.data = arm_vel
            self.arm_pub.publish(arm_msg)
        
        # Publish wrist commands
        if hasattr(self, 'target_wrist_yaw'):
            wrist_yaw_vel = (self.target_wrist_yaw - self.current_wrist_yaw) / 0.05
            wrist_yaw_vel = max(min(wrist_yaw_vel, 0.5), -0.5)
            wrist_yaw_msg = Float64()
            wrist_yaw_msg.data = wrist_yaw_vel
            self.wrist_yaw_pub.publish(wrist_yaw_msg)
        
        # Publish gripper command
        if hasattr(self, 'target_gripper'):
            gripper_msg = Float64()
            gripper_msg.data = self.target_gripper
            self.gripper_pub.publish(gripper_msg)
    
    def send_trajectory_command(self, joint_names, positions, velocities=None):
        """
        Send position command via trajectory action.
        
        This is the preferred method for position control as it respects
        limits and provides better control.
        """
        if not self.trajectory_client.server_is_ready():
            return
        
        goal_msg = FollowJointTrajectory.Goal()
        trajectory = JointTrajectory()
        
        # Set joint names
        trajectory.joint_names = joint_names
        
        # Create trajectory point
        point = JointTrajectoryPoint()
        point.positions = positions
        
        if velocities:
            point.velocities = velocities
        else:
            point.velocities = []
        
        point.accelerations = []
        point.effort = []
        point.time_from_start = Duration(sec=2, nanosec=0)  # 2 second duration
        
        trajectory.points = [point]
        goal_msg.trajectory = trajectory
        
        # Send goal
        self.trajectory_client.send_goal_async(goal_msg)
    
    # ============================================
    # JOINT STATE FEEDBACK
    # ============================================
    
    def joint_state_callback(self, msg):
        """Update current joint positions from joint_states."""
        for i, joint_name in enumerate(msg.name):
            if i >= len(msg.position):
                continue
            
            if joint_name == 'joint_lift':
                self.current_lift = msg.position[i]
            elif joint_name == 'joint_arm_l0':
                self.current_arm = msg.position[i]
            elif joint_name == 'joint_wrist_yaw':
                self.current_wrist_yaw = msg.position[i]
            elif joint_name == 'joint_wrist_pitch':
                self.current_wrist_pitch = msg.position[i]
            elif joint_name == 'joint_wrist_roll':
                self.current_wrist_roll = msg.position[i]
            elif joint_name == 'joint_head_pan':
                self.current_head_pan = msg.position[i]
            elif joint_name == 'joint_head_tilt':
                self.current_head_tilt = msg.position[i]
            elif 'gripper' in joint_name.lower():
                # Gripper position handling
                pass
    
    # ============================================
    # SAFETY CHECKS
    # ============================================
    
    def check_base_timeout(self):
        """Check if base command has timed out."""
        now = self.get_clock().now()
        elapsed = (now - self.last_base_command_time).nanoseconds / 1e9
        
        if elapsed > self.base_timeout:
            # Send zero velocity command
            twist = Twist()
            twist.linear.x = 0.0
            twist.angular.z = 0.0
            self.base_pub.publish(twist)
    
    def clamp_value(self, value, min_val, max_val):
        """Clamp value to limits."""
        return max(min(value, max_val), min_val)


def main(args=None):
    rclpy.init(args=args)
    node = StretchGamepadTeleop()
    
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        node.get_logger().info('Shutting down...')
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == '__main__':
    main()

