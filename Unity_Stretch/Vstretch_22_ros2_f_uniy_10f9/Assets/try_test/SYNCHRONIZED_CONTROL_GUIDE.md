# Synchronized Arm Control Guide - ro2LiftArm.cs

## Overview

`ro2LiftArm.cs` implements **Approach 4: Hybrid Control** - the best solution for synchronized movement between Unity visualization and the real Stretch 3 robot.

### Key Features

✅ **Dual Control**: Controls both Unity ArticulationBodies (visualization) and real robot simultaneously  
✅ **Synchronized Movement**: Unity and robot move together in real-time  
✅ **ROS2 Integration**: Uses existing `unity_lift.py` bridge on robot  
✅ **Feedback Loop**: Subscribes to `/joint_states` to sync Unity from robot  
✅ **Complete Arm Control**: Controls lift, all arm segments (L3-L0), wrist (yaw/pitch/roll), and gripper  
✅ **Keyboard & VR Support**: Works with keyboard and Meta Quest controllers  

## How It Works

### Architecture

```
Unity (ro2LiftArm.cs)
    │
    ├─→ Updates Unity ArticulationBodies (immediate visualization)
    │
    ├─→ Publishes JointTrajectory to ROS2
    │   Topic: /stretch_controller/follow_joint_trajectory/goal
    │
    └─→ Subscribes to /joint_states (feedback from robot)
        │
        └─→ Updates Unity visualization to match robot
```

### Control Flow

1. **User Input** (keyboard/VR controller)
2. **Update Unity** (immediate visual feedback)
3. **Publish to ROS2** (JointTrajectory with ALL joints)
4. **Robot Moves** (via `unity_lift.py` bridge)
5. **Feedback Received** (`/joint_states` subscription)
6. **Unity Syncs** (optional - can be disabled)

## Setup Instructions

### 1. Robot Setup (Stretch 3)

Ensure `unity_lift.py` is running on the robot:

```bash
# On Stretch 3 robot
cd ~/path/to/unity_lift.py
python3 unity_lift.py
```

The bridge should show:
```
unity_stretch_lift_bridge, waiting for action server...
Action server connected!
```

### 2. Unity Setup

1. **Add Script to Scene:**
   - Create empty GameObject (or use existing)
   - Add `ro2LiftArm` component
   - Ensure `ROS2UnityCore` is in scene

2. **Configure ROS2:**
   - Set `armCommandTopic` = `/stretch_controller/follow_joint_trajectory/goal`
   - Set `jointStateTopic` = `/joint_states`
   - Enable `publishToRobot` = true
   - Enable `useROSFeedback` = true (for synchronization)

3. **Auto-Discovery:**
   - Enable `autoFindJoints` = true (recommended)
   - Script will find all joints automatically
   - Or manually assign joints in Inspector

4. **Test Connection:**
   - Enter Play Mode
   - Check Console for "ROS2 initialized successfully!"
   - Check Console for joint discovery results

## Keyboard Controls

| Key | Action |
|-----|--------|
| ↑/↓ | Move entire arm UP/DOWN (lift joint) |
| Q/A | Extend/Retract arm (all segments together) |
| W/S | Wrist Yaw (left/right rotation) |
| E/D | Wrist Pitch (up/down rotation) |
| R/F | Wrist Roll (twist rotation) |
| G/H | Open/Close Gripper |
| 1/2/3/4 | Control individual arm segments (L3/L2/L1/L0) |

## VR Controller Controls (Meta Quest)

Enable `enableVRControllers` in Inspector:

- **Left Thumbstick Y**: Lift Up/Down
- **Left Thumbstick X**: Arm Extend/Retract
- **Right Thumbstick X**: Wrist Yaw
- **Right Thumbstick Y**: Wrist Pitch
- **Right Grip**: Close Gripper
- **Right Trigger**: Open Gripper

## Joint Names (Must Match URDF)

The script uses these joint names (from Stretch 3 URDF):

- `joint_lift` (prismatic, 0.0-1.1m)
- `joint_arm_l3` (prismatic, 0.0-0.13m)
- `joint_arm_l2` (prismatic, 0.0-0.13m)
- `joint_arm_l1` (prismatic, 0.0-0.13m)
- `joint_arm_l0` (prismatic, 0.0-0.13m)
- `joint_wrist_yaw` (revolute, -1.75 to 4.0 rad)
- `joint_wrist_pitch` (revolute, -1.57 to 0.56 rad)
- `joint_wrist_roll` (revolute, -3.14 to 3.14 rad)

**Note**: Gripper joints (`joint_gripper_finger_right/left`) might not be in the `follow_joint_trajectory` controller. They may need separate control via `/stretch/gripper_motor/cmd_gripper` topic.

## Synchronization Modes

### Mode 1: Unity Leads (Default)
- Unity updates immediately on input
- Robot follows via ROS2
- Robot feedback updates Unity (optional)

**Settings:**
- `publishToRobot` = true
- `useROSFeedback` = true
- `updateUnityFromRobot` = true

### Mode 2: Robot Leads
- Robot moves independently
- Unity syncs from robot feedback only

**Settings:**
- `publishToRobot` = false
- `useROSFeedback` = true
- `updateUnityFromRobot` = true

### Mode 3: Unity Only (Simulation)
- No ROS2 connection
- Unity visualization only

**Settings:**
- `publishToRobot` = false
- `useROSFeedback` = false

## Trajectory Publishing

The script publishes **complete arm trajectories** with ALL joints together:

```csharp
JointTrajectory {
    joint_names: ["joint_lift", "joint_arm_l3", "joint_arm_l2", 
                  "joint_arm_l1", "joint_arm_l0", "joint_wrist_yaw",
                  "joint_wrist_pitch", "joint_wrist_roll"]
    points: [{
        positions: [lift, l3, l2, l1, l0, yaw, pitch, roll]
        velocities: [maxVelocity, ...]
        time_from_start: calculated_duration
    }]
}
```

This ensures **synchronized movement** - all joints move together, just like the real robot.

## Troubleshooting

### ROS2 Not Connecting

1. **Check ROS2UnityCore:**
   - Ensure `ROS2UnityCore` GameObject is in scene
   - Check ROS2 settings (IP, port, etc.)

2. **Check Robot Bridge:**
   - Verify `unity_lift.py` is running on robot
   - Check robot terminal for connection messages

3. **Check Topics:**
   - Verify topic names match:
     - Command: `/stretch_controller/follow_joint_trajectory/goal`
     - Feedback: `/joint_states`

### Joints Not Found

1. **Check URDF Import:**
   - Ensure Stretch URDF is imported correctly
   - Joints should be named `link_lift`, `link_arm_l3`, etc.

2. **Manual Assignment:**
   - Disable `autoFindJoints`
   - Manually assign joints in Inspector

3. **Check Console:**
   - Look for "Joint Discovery Results" in Console
   - Missing joints will show "✗ Not Found"

### Robot Not Moving

1. **Check Publisher:**
   - Verify `publishToRobot` = true
   - Check Console for "Published trajectory" messages

2. **Check Robot Bridge:**
   - Verify `unity_lift.py` is receiving messages
   - Check robot terminal for "Sending command" messages

3. **Check Action Server:**
   - Verify Stretch controller is running
   - Check `/stretch_controller/follow_joint_trajectory` action server

### Unity Not Syncing from Robot

1. **Check Subscriber:**
   - Verify `useROSFeedback` = true
   - Verify `updateUnityFromRobot` = true

2. **Check Joint States:**
   - Verify `/joint_states` topic is publishing
   - Use `ros2 topic echo /joint_states` on robot

3. **Check Console:**
   - Look for "Feedback: ✓ Active" in GUI
   - Check for joint state messages in Console

## Comparison with Other Approaches

| Approach | Unity Control | Robot Control | Synchronization | Best For |
|----------|---------------|---------------|-----------------|----------|
| **Approach 1** (ArmUnityKeyJoy) | ✅ | ❌ | ❌ | Local simulation only |
| **Approach 2** (ROS2 only) | ❌ | ✅ | ✅ | Robot control only |
| **Approach 3** (Separate scripts) | ✅ | ✅ | ⚠️ Partial | Separate control |
| **Approach 4** (ro2LiftArm) | ✅ | ✅ | ✅ | **Synchronized control** |

## Best Practices

1. **Start with Unity Only:**
   - Test controls in Unity first
   - Verify all joints work correctly
   - Then enable ROS2 publishing

2. **Use Feedback:**
   - Enable `useROSFeedback` for accurate sync
   - Helps catch robot errors/limits

3. **Monitor Console:**
   - Watch for "Published trajectory" messages
   - Check for joint state updates
   - Monitor for errors

4. **Adjust Speeds:**
   - Tune `liftSpeed`, `armSpeed`, `wristSpeed`
   - Match robot's actual capabilities
   - Prevent jerky movements

5. **Test Incrementally:**
   - Start with lift only
   - Add arm segments
   - Add wrist joints
   - Test gripper separately

## Next Steps

1. **Gripper Control:**
   - Implement separate gripper topic (`/stretch/gripper_motor/cmd_gripper`)
   - Use Float64 message (-100 to 100)

2. **Advanced Features:**
   - Add trajectory smoothing
   - Add collision detection
   - Add safety limits

3. **VR Teleoperation:**
   - Enhance VR controller mapping
   - Add haptic feedback
   - Add visual indicators

## References

- **Stretch URDF**: `Assets/Stretch files/stretch_description/urdf/stretch.urdf`
- **Unity Bridge**: `Assets/try_test/unity_lift.py`
- **Local Control**: `Assets/try_test/ArmUnityKeyJoy.cs`
- **Lift Control**: `Assets/try_test/LiftUnity.cs`




