# How Joint Control Works in Unity - Explanation

## Overview
This document explains how joint control works in Unity, comparing the pick_and_place project (Niryo robot) with Stretch 3 robot control.

---

## How Pick_and_Place Project Controls Joints

### 1. **Controller Component** (from Unity Robotics Hub URDF Importer)
- **Location**: Automatically added when importing URDF files
- **Path**: `Assets/Packages/URDF Importer/Runtime/Controller/Controller.cs`
- **Functionality**:
  - **Left/Right Arrow Keys**: Navigate through joints (select which joint to control)
  - **Up/Down Arrow Keys**: Control the selected joint movement
  - Shows selected joint index and name in Inspector

### 2. **Joint Types in Pick_and_Place**
The Niryo robot uses **REVOLUTE joints** (rotational):
- `shoulder_link` - Revolute joint (rotates)
- `arm_link` - Revolute joint (rotates)
- `elbow_link` - Revolute joint (rotates)
- `forearm_link` - Revolute joint (rotates)
- `wrist_link` - Revolute joint (rotates)
- `hand_link` - Revolute joint (rotates)

### 3. **How Revolute Joints Work**
```csharp
// Revolute joints rotate around an axis
ArticulationBody joint = GetComponent<ArticulationBody>();
ArticulationDrive drive = joint.xDrive;

// Target is in DEGREES (rotation angle)
drive.target = 45.0f; // Rotate 45 degrees
joint.xDrive = drive;
```

**Key Points**:
- `drive.target` is in **degrees** (rotation angle)
- Joint rotates around its axis
- Example: `target = 90` means rotate 90 degrees

---

## How Stretch 3 Lift Control Works

### 1. **Joint Type: PRISMATIC**
The Stretch 3 lift uses a **PRISMATIC joint** (linear/sliding):
- **Joint Name**: `joint_lift`
- **Link Name**: `link_lift`
- **Type**: Prismatic (slides up/down, doesn't rotate)
- **Range**: 0.0 to 1.1 meters (from URDF)

### 2. **How Prismatic Joints Work**
```csharp
// Prismatic joints slide linearly
ArticulationBody liftJoint = GetComponent<ArticulationBody>();
ArticulationDrive drive = liftJoint.xDrive;

// Target is in METERS (linear position)
drive.target = 0.5f; // Move to 0.5 meters height
liftJoint.xDrive = drive;
```

**Key Points**:
- `drive.target` is in **meters** (linear position)
- Joint slides along its axis (up/down)
- Example: `target = 0.5` means move to 0.5 meters height

### 3. **Differences from Revolute Joints**

| Aspect | Revolute Joint (Pick_and_Place) | Prismatic Joint (Stretch Lift) |
|--------|--------------------------------|--------------------------------|
| Movement Type | Rotation (spins) | Linear (slides) |
| Target Units | Degrees (°) | Meters (m) |
| URDF Type | `type="revolute"` | `type="prismatic"` |
| Example | Rotate shoulder 90° | Move lift to 0.5m height |
| Control | Change angle | Change position |

---

## Implementation: LiftInUnity.cs

### Features:
1. **Auto-Discovery**: Automatically finds the `link_lift` GameObject and its ArticulationBody
2. **Arrow Key Control**: 
   - **↑ Up Arrow**: Move lift UP (increase position)
   - **↓ Down Arrow**: Move lift DOWN (decrease position)
3. **Joint Limits**: Enforces 0.0 to 1.1 meter range (from URDF)
4. **Drive Parameters**: Configurable stiffness, damping, and force limits

### How It Works:

```csharp
void MoveLift(float deltaPosition)
{
    // Update target position
    currentTargetPosition += deltaPosition;
    
    // Clamp to limits (0.0 to 1.1 meters)
    currentTargetPosition = Mathf.Clamp(currentTargetPosition, minPosition, maxPosition);

    // Get drive settings
    ArticulationDrive drive = liftArticulationBody.xDrive;
    
    // Set target (in METERS for prismatic joint)
    drive.target = currentTargetPosition;
    
    // Apply settings
    liftArticulationBody.xDrive = drive;
}
```

### Usage:
1. **Attach Script**: Add `LiftInUnity.cs` to any GameObject in the scene (or the robot root)
2. **Auto-Find**: The script will automatically find `link_lift` GameObject
3. **Manual Assignment** (optional): Assign `liftArticulationBody` manually in Inspector
4. **Play**: Press Play, then use ↑/↓ arrow keys to control lift

---

## Comparison: Pick_and_Place vs Stretch Lift

### Pick_and_Place Approach:
```csharp
// Controller component handles multiple joints
// User selects joint with Left/Right arrows
// Then controls selected joint with Up/Down arrows

// For revolute joints:
drive.target = angleInDegrees; // Rotation
```

### Stretch Lift Approach:
```csharp
// Simple script for single joint
// Direct control with Up/Down arrows (no selection needed)

// For prismatic joint:
drive.target = positionInMeters; // Linear position
```

---

## Key Concepts

### ArticulationBody Component
- Unity's physics component for robot joints
- Handles both revolute (rotation) and prismatic (linear) joints
- Uses `xDrive` property to control joint movement

### ArticulationDrive
- Controls joint movement parameters:
  - `target`: Target position/angle
  - `stiffness`: How fast joint reaches target
  - `damping`: Prevents oscillations
  - `forceLimit`: Maximum force joint can apply

### Joint Types in URDF
- **Revolute**: Rotational joint (like shoulder, elbow)
- **Prismatic**: Linear joint (like lift, telescopic arm)
- **Fixed**: No movement (like base mount)
- **Continuous**: Unlimited rotation (like wheels)

---

## Troubleshooting

### Lift Not Moving?
1. Check if `link_lift` GameObject exists in scene
2. Verify ArticulationBody component is present
3. Check joint type is `PrismaticJoint`
4. Ensure `immovable` is NOT checked on `link_lift` (only on base)

### Wrong Movement Direction?
- Check the joint axis in URDF (`<axis xyz="..."/>`)
- Adjust `movementAxis` in script if needed
- Prismatic joints move along their axis direction

### Joint Limits Not Working?
- Verify `minPosition` and `maxPosition` match URDF limits
- Check `xDriveLimits` are set correctly
- URDF shows: `<limit lower="0.0" upper="1.1"/>`

---

## Summary

**Pick_and_Place (Niryo)**:
- Uses **Controller** component from Unity Robotics Hub
- Controls **revolute joints** (rotation)
- Left/Right arrows select joint, Up/Down arrows control it
- Target values in **degrees**

**Stretch Lift**:
- Uses **LiftInUnity** script (custom implementation)
- Controls **prismatic joint** (linear movement)
- Up/Down arrows directly control lift
- Target values in **meters**

Both use `ArticulationBody.xDrive.target`, but with different units and movement types!



