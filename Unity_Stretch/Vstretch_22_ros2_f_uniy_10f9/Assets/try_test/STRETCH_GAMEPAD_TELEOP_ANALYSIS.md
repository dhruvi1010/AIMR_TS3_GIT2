# Stretch Gamepad Teleoperation - Comprehensive Analysis

## Table of Contents
1. [Overview](#overview)
2. [Architecture & Components](#architecture--components)
3. [Joint Control Mapping](#joint-control-mapping)
4. [URDF Joints & Commands](#urdf-joints--commands)
5. [Joint Limits & Safety Mechanisms](#joint-limits--safety-mechanisms)
6. [Related Packages & Submodules](#related-packages--submodules)
7. [Implementation Details](#implementation-details)

---

## Overview

The `stretch_gamepad_teleop.py` script is part of the `stretch_ros` package (for ROS1) or `stretch_ros2` (for ROS2) and provides teleoperation capabilities for the Stretch RE1/RE2 mobile manipulator using a gamepad controller (typically Xbox controller).

### Key Features:
- Real-time gamepad input processing
- Multi-joint simultaneous control
- Safety limit enforcement
- Guarded contact detection
- Velocity and position control modes

---

## Architecture & Components

### Main Components:

1. **Gamepad Input Handler**
   - Subscribes to `/joy` topic (ROS1) or `/joy` topic (ROS2)
   - Processes joystick axes and button states
   - Maps inputs to robot commands

2. **Command Publishers**
   - Base velocity: `/stretch/cmd_vel` (geometry_msgs/Twist)
   - Joint commands: Individual topics for each joint
   - Action clients: For trajectory-based control

3. **Safety Systems**
   - Joint limit checking
   - Guarded contact detection
   - Velocity/acceleration limits
   - Timeout mechanisms

### Dependencies:
- `stretch_body` Python SDK (low-level hardware interface)
- `stretch_ros` or `stretch_ros2` (ROS interface layer)
- `joy` package (gamepad driver)
- `sensor_msgs/Joy` (gamepad message type)

---

## Joint Control Mapping

### Gamepad Input → Robot Command Mapping

#### **Base (Mobile Platform)**
- **Control Input**: Left joystick
  - **Y-axis (vertical)**: Forward/Backward translation
  - **X-axis (horizontal)**: Left/Right rotation
- **Command Type**: Velocity control (`geometry_msgs/Twist`)
- **Topic**: `/stretch/cmd_vel`
- **Message Format**:
  ```python
  twist.linear.x = joystick_y * max_linear_velocity
  twist.angular.z = joystick_x * max_angular_velocity
  ```
- **Limits**:
  - Max linear velocity: ~0.2 m/s (default)
  - Max angular velocity: ~0.5 rad/s (default)
  - Timeout: 1 second (robot stops if no command received)

#### **Lift (Vertical Translation)**
- **Control Input**: Right joystick Y-axis OR triggers/buttons
- **Command Type**: Position or velocity control
- **Topic**: `/stretch/joint_lift/cmd` (velocity) OR `/stretch_controller/follow_joint_trajectory` (position)
- **Joint Name**: `joint_lift`
- **URDF Limits**:
  - Minimum: 0.0 meters
  - Maximum: 1.1 meters
- **Safety**: Guarded contact detection (stops on excessive force)

#### **Arm (Telescoping Extension)**
- **Control Input**: Right joystick X-axis OR buttons
- **Command Type**: Position or velocity control
- **Topic**: `/stretch/joint_arm_l0/cmd` (velocity) OR trajectory action
- **Joint Name**: `joint_arm_l0` (prismatic joint)
- **URDF Limits**:
  - Minimum: 0.0 meters (fully retracted)
  - Maximum: ~0.52 meters (fully extended)
- **Safety**: Guarded contact detection

#### **Wrist Yaw**
- **Control Input**: Buttons or joystick
- **Command Type**: Position control
- **Topic**: `/stretch/joint_wrist_yaw/cmd` OR trajectory action
- **Joint Name**: `joint_wrist_yaw`
- **URDF Limits**:
  - Range: ±170 degrees (±2.967 rad)
  - Total range: 340 degrees

#### **Wrist Pitch** (if Dex Wrist equipped)
- **Control Input**: Buttons or joystick
- **Command Type**: Position control
- **Topic**: `/stretch/joint_wrist_pitch/cmd` OR trajectory action
- **Joint Name**: `joint_wrist_pitch`
- **URDF Limits**:
  - Range: ±50 degrees (±0.873 rad)
  - Total range: 100 degrees

#### **Wrist Roll** (if Dex Wrist equipped)
- **Control Input**: Buttons or joystick
- **Command Type**: Position control
- **Topic**: `/stretch/joint_wrist_roll/cmd` OR trajectory action
- **Joint Name**: `joint_wrist_roll`
- **URDF Limits**:
  - Range: ±170 degrees (±2.967 rad)
  - Total range: 340 degrees

#### **Head Pan**
- **Control Input**: D-pad or buttons
- **Command Type**: Position control
- **Topic**: `/stretch/joint_head_pan/cmd` OR trajectory action
- **Joint Name**: `joint_head_pan`
- **URDF Limits**:
  - Range: ±173 degrees (±3.02 rad)
  - Total range: 346 degrees

#### **Head Tilt**
- **Control Input**: D-pad or buttons
- **Command Type**: Position control
- **Topic**: `/stretch/joint_head_tilt/cmd` OR trajectory action
- **Joint Name**: `joint_head_tilt`
- **URDF Limits**:
  - Range: -5° to +110° (-0.087 to +1.92 rad)
  - Total range: 115 degrees

#### **Gripper**
- **Control Input**: Buttons (e.g., B to open, X to close)
- **Command Type**: Position control
- **Topic**: `/stretch/gripper_motor/cmd_gripper`
- **Joint Name**: `joint_gripper_finger_left`, `joint_gripper_finger_right`
- **Command Format**: Float64 value
  - Range: -100 (fully open) to +100 (fully closed)
  - Or: Position in meters (typically 0.0 to 0.17m)

---

## URDF Joints & Commands

### Complete URDF Joint List:

1. **Base Joints** (Differential Drive)
   - `joint_left_wheel` - Continuous rotation
   - `joint_right_wheel` - Continuous rotation
   - Controlled via `/stretch/cmd_vel` (Twist message)

2. **Lift Joint**
   - `joint_lift` - Prismatic joint (0.0 to 1.1m)
   - Controlled via `/stretch/joint_lift/cmd` or trajectory action

3. **Arm Joint**
   - `joint_arm_l0` - Prismatic joint (0.0 to ~0.52m)
   - Controlled via `/stretch/joint_arm_l0/cmd` or trajectory action

4. **Wrist Joints**
   - `joint_wrist_yaw` - Revolute joint (±170°)
   - `joint_wrist_pitch` - Revolute joint (±50°) [Dex Wrist only]
   - `joint_wrist_roll` - Revolute joint (±170°) [Dex Wrist only]

5. **Head Joints**
   - `joint_head_pan` - Revolute joint (±173°)
   - `joint_head_tilt` - Revolute joint (-5° to +110°)

6. **Gripper Joints**
   - `joint_gripper_finger_left` - Revolute joint
   - `joint_gripper_finger_right` - Revolute joint

### Command Topics (ROS2):

```
/stretch/cmd_vel                          # Base velocity (Twist)
/stretch/joint_lift/cmd                   # Lift velocity (Float64)
/stretch/joint_arm_l0/cmd                 # Arm velocity (Float64)
/stretch/joint_wrist_yaw/cmd              # Wrist yaw velocity (Float64)
/stretch/joint_wrist_pitch/cmd            # Wrist pitch velocity (Float64)
/stretch/joint_wrist_roll/cmd             # Wrist roll velocity (Float64)
/stretch/joint_head_pan/cmd               # Head pan velocity (Float64)
/stretch/joint_head_tilt/cmd              # Head tilt velocity (Float64)
/stretch/gripper_motor/cmd_gripper        # Gripper position (Float64)
/stretch_controller/follow_joint_trajectory  # Trajectory action (for position control)
```

### State Topics:

```
/stretch/joint_states                      # All joint states (JointState)
/stretch/odom                              # Base odometry
```

---

## Joint Limits & Safety Mechanisms

### 1. **Software Limits (Parameter-Based)**

Limits are defined in robot parameter files (YAML):
- Location: `~/.stretch_robot_config/` or package config files
- Parameters include:
  - `range_m` - Position limits for each joint
  - `max_velocity_m` - Maximum velocity
  - `max_accel_m` - Maximum acceleration
  - `contact_thresh_N` - Guarded contact threshold

Example parameter structure:
```yaml
robot:
  lift:
    range_m: [0.0, 1.1]
    max_velocity_m: 0.1
    max_accel_m: 0.15
    contact_thresh_N: 50.0
  arm:
    range_m: [0.0, 0.52]
    max_velocity_m: 0.1
    max_accel_m: 0.15
    contact_thresh_N: 50.0
```

### 2. **Hardware Limits (Physical Constraints)**

- **Mechanical End Stops**: Physical limits prevent overextension
- **Motor Encoders**: Provide position feedback
- **Current Sensing**: Detects excessive force/torque

### 3. **Guarded Contact Detection**

**How it works:**
- Monitors motor current/effort during motion
- If effort exceeds threshold → motion halts immediately
- Prevents damage from collisions or obstructions
- Applies to: Lift, Arm, and optionally other joints

**Implementation:**
```python
# Pseudo-code
if motor_current > contact_threshold:
    stop_motion()
    log_contact_detected()
```

### 4. **Velocity/Acceleration Limits**

- Commands are clamped to maximum values
- Prevents sudden jerky movements
- Smooth acceleration/deceleration profiles

### 5. **Timeout Mechanisms**

**Base Velocity Timeout:**
- If no `/stretch/cmd_vel` command received for 1 second
- Robot automatically stops (safety feature)
- Prevents runaway behavior

**Joint Command Timeout:**
- Similar timeout for individual joint commands
- Ensures robot doesn't continue moving if controller disconnects

### 6. **Position Limit Enforcement**

Commands are clamped to URDF-defined limits:
```python
# Pseudo-code
target_position = clamp(target_position, joint_min, joint_max)
```

### 7. **Rate Limiting**

- Commands published at controlled rate (typically 10-20 Hz)
- Prevents overwhelming the robot's control system
- Reduces network traffic

---

## Related Packages & Submodules

### Core Packages:

1. **`stretch_body`** (Python SDK)
   - Low-level hardware interface
   - Direct motor control
   - Parameter management
   - Guarded contact implementation
   - Location: `stretch_body` Python package

2. **`stretch_ros` / `stretch_ros2`**
   - ROS interface layer
   - Topic publishers/subscribers
   - Action servers/clients
   - URDF loading
   - Location: `stretch_ros` or `stretch_ros2` ROS packages

3. **`stretch_core`** (subpackage)
   - Core ROS nodes
   - `stretch_driver` - Main robot driver node
   - `stretch_gamepad_teleop.py` - Gamepad teleop node
   - Location: `stretch_ros/stretch_core/nodes/`

4. **`stretch_description`**
   - URDF files
   - Mesh files
   - Joint limit definitions
   - Location: `stretch_ros/stretch_description/`

5. **`joy` package**
   - Gamepad driver
   - Publishes `/joy` topic
   - Maps physical gamepad to ROS messages
   - Location: Standard ROS package

### Key Files in `stretch_gamepad_teleop.py` Dependencies:

```
stretch_ros/
├── stretch_core/
│   ├── nodes/
│   │   └── stretch_gamepad_teleop.py    # Main teleop script
│   └── scripts/
│       └── (helper scripts)
├── stretch_description/
│   ├── urdf/
│   │   └── stretch.urdf                 # URDF with joint limits
│   └── config/
│       └── (parameter files)
└── stretch_msgs/
    └── (custom message types)
```

---

## Implementation Details

### Typical Gamepad Mapping (Xbox Controller):

```
Left Joystick:
  Y-axis → Base forward/backward
  X-axis → Base rotation

Right Joystick:
  Y-axis → Lift up/down
  X-axis → Arm extend/retract

Buttons:
  A → (varies)
  B → Gripper open
  X → Gripper close
  Y → (varies)
  D-pad → Head pan/tilt
  Triggers → (varies - lift or arm)
  Bumpers → Wrist control
```

### Command Flow:

```
Gamepad → joy node → /joy topic → stretch_gamepad_teleop.py
                                              ↓
                                    Process inputs
                                              ↓
                                    Apply limits/clamping
                                              ↓
                                    Publish commands
                                              ↓
                    /stretch/cmd_vel, /stretch/joint_*/cmd, etc.
                                              ↓
                                    stretch_driver node
                                              ↓
                                    stretch_body SDK
                                              ↓
                                    Hardware (motors)
```

### Example Command Generation:

**Base Movement:**
```python
# From gamepad input
joy_msg.axes[1]  # Left joystick Y-axis (-1.0 to 1.0)

# Convert to velocity
linear_vel = joy_msg.axes[1] * max_linear_speed  # e.g., 0.2 m/s
angular_vel = joy_msg.axes[0] * max_angular_speed  # e.g., 0.5 rad/s

# Create Twist message
twist = Twist()
twist.linear.x = linear_vel
twist.angular.z = angular_vel

# Publish
publisher.publish(twist)
```

**Lift Control:**
```python
# From gamepad input
joy_msg.axes[4]  # Right joystick Y-axis or trigger

# Convert to position increment
lift_velocity = joy_msg.axes[4] * lift_speed  # e.g., 0.01 m/s

# Get current position
current_lift = get_current_lift_position()

# Calculate target
target_lift = current_lift + (lift_velocity * dt)

# Clamp to limits
target_lift = clamp(target_lift, 0.0, 1.1)

# Publish command
publish_lift_command(target_lift)
```

### Safety Checks in Code:

```python
# Pseudo-code from stretch_gamepad_teleop.py

def process_lift_command(joy_input):
    # 1. Read input
    velocity = joy_input * max_lift_velocity
    
    # 2. Get current position
    current_pos = get_joint_position('joint_lift')
    
    # 3. Calculate target
    target_pos = current_pos + (velocity * dt)
    
    # 4. Apply limits
    target_pos = max(min(target_pos, LIFT_MAX), LIFT_MIN)
    
    # 5. Check for contact (if guarded contact enabled)
    if is_contact_detected('joint_lift'):
        return  # Don't send command
    
    # 6. Publish command
    publish_lift_command(target_pos)
```

---

## Summary

### Key Takeaways:

1. **Multi-Modal Control**: Uses both velocity and position control depending on joint
2. **Safety First**: Multiple layers of limits (software, hardware, guarded contact)
3. **Real-Time Responsive**: Low-latency gamepad input processing
4. **Modular Design**: Separates gamepad input, command processing, and hardware interface
5. **URDF-Driven**: Joint limits come from URDF definitions
6. **Parameter-Based**: Configurable limits via YAML parameter files

### Joint Control Summary:

| Joint | Control Type | Topic | Limits | Safety |
|-------|-------------|-------|--------|--------|
| Base | Velocity | `/stretch/cmd_vel` | Speed limits | Timeout |
| Lift | Position/Velocity | `/stretch/joint_lift/cmd` | 0.0-1.1m | Guarded Contact |
| Arm | Position/Velocity | `/stretch/joint_arm_l0/cmd` | 0.0-0.52m | Guarded Contact |
| Wrist Yaw | Position | `/stretch/joint_wrist_yaw/cmd` | ±170° | Software limits |
| Wrist Pitch | Position | `/stretch/joint_wrist_pitch/cmd` | ±50° | Software limits |
| Wrist Roll | Position | `/stretch/joint_wrist_roll/cmd` | ±170° | Software limits |
| Head Pan | Position | `/stretch/joint_head_pan/cmd` | ±173° | Software limits |
| Head Tilt | Position | `/stretch/joint_head_tilt/cmd` | -5° to +110° | Software limits |
| Gripper | Position | `/stretch/gripper_motor/cmd_gripper` | 0.0-0.17m | Software limits |

---

## References

- Stretch Documentation: https://docs.hello-robot.com/
- Stretch Hardware Overview: https://docs.hello-robot.com/0.3/getting_started/stretch_hardware_overview/
- Stretch ROS2 Drivers: https://docs.hello-robot.com/0.3/ros2/robot_drivers/
- Stretch Body Parameters: https://docs.hello-robot.com/latest/stretch-body/docs/robot_parameters/
- GitHub: https://github.com/hello-robot/stretch_ros (ROS1) or stretch_ros2 (ROS2)

---

*Document created based on Stretch3 documentation and stretch_ros/stretch_ros2 codebase analysis*

