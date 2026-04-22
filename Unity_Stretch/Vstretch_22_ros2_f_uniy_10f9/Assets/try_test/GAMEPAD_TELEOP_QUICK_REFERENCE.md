# Stretch Gamepad Teleop - Quick Reference Guide

## 🎮 Gamepad Control Mapping

### Base (Mobile Platform)
- **Left Stick Y-axis** → Forward/Backward (linear velocity)
- **Left Stick X-axis** → Rotate Left/Right (angular velocity)
- **Topic**: `/stretch/cmd_vel` (Twist message)
- **Limits**: 
  - Linear: 0.2 m/s max
  - Angular: 0.5 rad/s max
  - **Timeout**: 1 second (auto-stop if no command)

### Lift (Vertical)
- **Right Stick Y-axis** OR **Triggers** → Up/Down
- **Topic**: `/stretch/joint_lift/cmd` (velocity) OR trajectory action
- **Joint**: `joint_lift`
- **Limits**: 0.0 m (min) to 1.1 m (max)
- **Safety**: Guarded contact detection

### Arm (Extension)
- **Right Stick X-axis** → Extend/Retract
- **Topic**: `/stretch/joint_arm_l0/cmd` (velocity) OR trajectory action
- **Joint**: `joint_arm_l0`
- **Limits**: 0.0 m (min) to 0.52 m (max)
- **Safety**: Guarded contact detection

### Wrist Yaw
- **Bumpers** OR **Buttons** → Rotate left/right
- **Topic**: `/stretch/joint_wrist_yaw/cmd`
- **Joint**: `joint_wrist_yaw`
- **Limits**: ±170° (±2.967 rad)

### Wrist Pitch (Dex Wrist)
- **Buttons** → Pitch up/down
- **Topic**: `/stretch/joint_wrist_pitch/cmd`
- **Joint**: `joint_wrist_pitch`
- **Limits**: ±50° (±0.873 rad)

### Wrist Roll (Dex Wrist)
- **Buttons** → Roll left/right
- **Topic**: `/stretch/joint_wrist_roll/cmd`
- **Joint**: `joint_wrist_roll`
- **Limits**: ±170° (±2.967 rad)

### Head Pan
- **D-pad Left/Right** → Pan left/right
- **Topic**: `/stretch/joint_head_pan/cmd`
- **Joint**: `joint_head_pan`
- **Limits**: ±173° (±3.02 rad)

### Head Tilt
- **D-pad Up/Down** → Tilt up/down
- **Topic**: `/stretch/joint_head_tilt/cmd`
- **Joint**: `joint_head_tilt`
- **Limits**: -5° to +110° (-0.087 to +1.92 rad)

### Gripper
- **B Button** → Open
- **X Button** → Close
- **Topic**: `/stretch/gripper_motor/cmd_gripper`
- **Command Range**: -100 (open) to +100 (closed)

---

## 📊 Command Topics Summary

### Velocity Commands (Direct Control)
```
/stretch/cmd_vel                    # Base (Twist)
/stretch/joint_lift/cmd             # Lift (Float64)
/stretch/joint_arm_l0/cmd           # Arm (Float64)
/stretch/joint_wrist_yaw/cmd        # Wrist yaw (Float64)
/stretch/joint_wrist_pitch/cmd      # Wrist pitch (Float64)
/stretch/joint_wrist_roll/cmd       # Wrist roll (Float64)
/stretch/joint_head_pan/cmd         # Head pan (Float64)
/stretch/joint_head_tilt/cmd        # Head tilt (Float64)
/stretch/gripper_motor/cmd_gripper  # Gripper (Float64)
```

### Position Commands (Trajectory Action)
```
/stretch_controller/follow_joint_trajectory  # Action server
  - Goal: JointTrajectory with joint names and positions
  - Better for position control with limits
```

### State Feedback
```
/stretch/joint_states  # All joint positions/velocities (JointState)
```

---

## 🛡️ Safety Limits & Mechanisms

### 1. **Software Limits** (Parameter Files)
- Defined in YAML config files
- Position limits: `range_m: [min, max]`
- Velocity limits: `max_velocity_m`
- Acceleration limits: `max_accel_m`
- Contact threshold: `contact_thresh_N`

### 2. **URDF Limits** (Hardware Definition)
- Joint limits defined in URDF
- Enforced by controller
- Cannot be exceeded

### 3. **Guarded Contact** (Force Detection)
- Monitors motor current/effort
- Stops motion if force exceeds threshold
- Applies to: Lift, Arm
- Prevents damage from collisions

### 4. **Timeouts**
- **Base**: 1 second timeout → auto-stop
- **Joints**: Similar timeout mechanisms
- Prevents runaway if controller disconnects

### 5. **Rate Limiting**
- Commands published at 10-20 Hz
- Prevents overwhelming control system

### 6. **Position Clamping**
- All commands clamped to limits before sending
- `target = clamp(target, min_limit, max_limit)`

---

## 🔧 How Limits Are Imposed

### At Command Generation Level:
```python
# 1. Read gamepad input
input_value = gamepad_axis_value  # -1.0 to 1.0

# 2. Calculate target
target = current_position + (input_value * speed * dt)

# 3. Apply limits (clamping)
target = max(min(target, max_limit), min_limit)

# 4. Check guarded contact (if enabled)
if contact_detected:
    return  # Don't send command

# 5. Publish command
publish_command(target)
```

### At Controller Level:
- Stretch driver node enforces limits
- Hardware controller checks limits
- Guarded contact monitoring active

### At Hardware Level:
- Physical end stops
- Motor encoders provide feedback
- Current sensing for contact detection

---

## 📦 Key Packages & Files

### Core Packages:
1. **`stretch_body`** - Low-level Python SDK
2. **`stretch_ros`** / **`stretch_ros2`** - ROS interface
3. **`stretch_core`** - Core nodes (includes gamepad teleop)
4. **`stretch_description`** - URDF files with limits
5. **`joy`** - Gamepad driver package

### Important Files:
- `stretch_gamepad_teleop.py` - Main teleop script
- `stretch.urdf` - Robot description with joint limits
- `robot_params.yaml` - Parameter configuration
- `stretch_driver` - Main robot driver node

---

## 🔄 Command Flow Diagram

```
Gamepad Hardware
    ↓
joy Node (/joy topic)
    ↓
stretch_gamepad_teleop.py
    ├─ Process inputs
    ├─ Apply limits/clamping
    ├─ Check safety (contact, timeouts)
    └─ Publish commands
        ↓
    /stretch/cmd_vel, /stretch/joint_*/cmd
        ↓
stretch_driver Node
    ├─ Validate limits
    ├─ Check guarded contact
    └─ Send to hardware
        ↓
stretch_body SDK
    ├─ Motor control
    ├─ Encoder feedback
    └─ Current sensing
        ↓
Physical Hardware (Motors, Sensors)
```

---

## 💡 Key Implementation Details

### Velocity vs Position Control:
- **Base**: Always velocity control (Twist)
- **Lift/Arm**: Can use velocity OR position (trajectory)
- **Wrist/Head**: Typically position control
- **Gripper**: Position control

### Deadzone Handling:
- Joystick inputs below threshold (e.g., 0.1) are ignored
- Prevents drift from slight stick movement

### Synchronization:
- Reads `/stretch/joint_states` to know current positions
- Syncs target positions with actual robot state
- Prevents large position jumps

### Rate Control:
- Commands published at fixed rate (10-20 Hz)
- Smooth motion, not overwhelming system

---

## 🚨 Common Issues & Solutions

### Issue: Robot doesn't respond
- Check: Is `/joy` topic publishing?
- Check: Is `stretch_driver` node running?
- Check: Are limits preventing movement?

### Issue: Joint hits limit and stops
- Normal behavior - limits are enforced
- Check current position vs limits
- Use guarded contact to detect obstructions

### Issue: Base keeps moving after releasing stick
- Check timeout mechanism
- Should auto-stop after 1 second
- Verify `/stretch/cmd_vel` is being published

### Issue: Guarded contact triggers too easily
- Adjust `contact_thresh_N` parameter
- Lower threshold = more sensitive
- Higher threshold = less sensitive

---

## 📚 Additional Resources

- Full Analysis: See `STRETCH_GAMEPAD_TELEOP_ANALYSIS.md`
- Reference Code: See `stretch_gamepad_teleop_reference.py`
- Official Docs: https://docs.hello-robot.com/
- GitHub: https://github.com/hello-robot/stretch_ros2

---

*Quick reference for Stretch3 gamepad teleoperation*

