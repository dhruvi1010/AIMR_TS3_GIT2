# Axis Alignment Analysis: Lift (Working) vs Arm (Not Working)

## Executive Summary

Both `link_lift` and `link_arm_l3` have **identical axis configurations** in Unity, but lift works while arm doesn't. The issue is **NOT axis alignment** - it's likely related to:
1. **Drive Type mismatch** (Inspector shows "Force" but scene file shows `driveType: 0` = "Target")
2. **Stiffness/Damping differences** (lift has lower values that work better)
3. **Initial position/offset handling**

---

## URDF Definitions

### Joint Lift (Working)
```xml
<joint name="joint_lift" type="prismatic">
  <origin xyz="-0.037385 0.1666 0.0" rpy="-1.5708 1.5708 0.0"/>
  <axis xyz="0.0 0.0 1.0"/>  <!-- Z-axis in ROS -->
  <limit effort="100.0" lower="0.0" upper="1.1" velocity="1.0"/>
</joint>
```

### Joint Arm L3 (Not Working)
```xml
<joint name="joint_arm_l3" type="prismatic">
  <origin xyz="0.0 0.0 0.013" rpy="~0.0 ~0.0 ~0.0"/>
  <axis xyz="0.0 0.0 1.0"/>  <!-- Z-axis in ROS (SAME as lift!) -->
  <limit effort="100.0" lower="0.0" upper="0.13" velocity="1.0"/>
</joint>
```

**Key Finding**: Both joints use **identical URDF axis** (`xyz="0.0 0.0 1.0"` = Z-axis in ROS).

---

## Unity Coordinate System Conversion

### ROS to Unity Axis Conversion

The URDF Importer uses `Ros2Unity()` transformation:

```csharp
// From Assets/Ros2ForUnity/Scripts/Transformations.cs
public static Vector3 Ros2Unity(this Vector3 vector3)
{
    return new Vector3(-vector3.y, vector3.z, vector3.x);
}
```

**For URDF axis `xyz="0.0 0.0 1.0"` (ROS Z-axis)**:
- ROS: `(x=0, y=0, z=1)`
- Unity: `(-y, z, x) = (0, 1, 0)` = **Y-axis in Unity**

### URDF Importer Logic

From `UrdfJointPrismatic.cs`:
```csharp
Vector3 axisofMotionUnity = axisofMotion.Ros2Unity();  // Converts ROS Z → Unity Y
Quaternion Motion = new Quaternion();
Motion.SetFromToRotation(new Vector3(1, 0, 0), axisofMotionUnity);  // Rotate from X to Y
unityJoint.anchorRotation = Motion;  // Results in 90° Z rotation
unityJoint.linearLockX = LimitedMotion;  // Motion along X-axis
```

**Result**: 
- URDF Z-axis → Unity Y-axis (via Ros2Unity)
- Unity motion axis: **X-axis** (always)
- Anchor rotation: **90° around Z** (to align X-axis with URDF's Z-axis direction)

---

## Unity Scene Configuration Comparison

### Link Lift (Working)

**From Unity Scene File:**
```
m_AnchorRotation: {x: 0, y: 0, z: 0.7071068, w: 0.7071068}  // 90° Z rotation
m_LinearX: 1  // Motion along X-axis
m_XDrive:
  lowerLimit: 0
  upperLimit: 1.1
  stiffness: 10
  damping: 1000
  forceLimit: 1000
  target: 10  // NOTE: Non-zero target!
  driveType: 0  // Target (position control)
```

**From Inspector (Screenshot):**
- Axis: **X**
- Anchor Rotation: **Z: 90°**
- Drive Type: **Force** (⚠️ Inspector shows "Force" but scene file shows `driveType: 0` = "Target")
- Stiffness: **10**
- Damping: **1000**
- Target: **10** (non-zero, but lift still works!)

### Link Arm L3 (Not Working)

**From Unity Scene File:**
```
m_AnchorRotation: {x: 0, y: 0, z: 0.7071068, w: 0.7071068}  // 90° Z rotation (SAME!)
m_LinearX: 1  // Motion along X-axis (SAME!)
m_XDrive:
  lowerLimit: 0
  upperLimit: 0.13
  stiffness: 0  // ⚠️ ZERO stiffness!
  damping: 0    // ⚠️ ZERO damping!
  forceLimit: 100
  target: 0
  driveType: 0  // Target (position control)
```

**From Inspector (Screenshot):**
- Axis: **X** (SAME as lift)
- Anchor Rotation: **Z: 90°** (SAME as lift)
- Drive Type: **Force** (⚠️ Inspector shows "Force" but scene file shows `driveType: 0` = "Target")
- Stiffness: **25000** (⚠️ Inspector shows 25000, but scene file shows 0!)
- Damping: **200** (⚠️ Inspector shows 200, but scene file shows 0!)
- Target: **0**

---

## Critical Differences Found

### 1. **Stiffness/Damping Mismatch**

| Setting | Scene File | Inspector | Impact |
|---------|------------|-----------|--------|
| **Lift Stiffness** | 10 | 10 | ✅ Matches |
| **Lift Damping** | 1000 | 1000 | ✅ Matches |
| **Arm L3 Stiffness** | **0** | **25000** | ❌ **MISMATCH!** |
| **Arm L3 Damping** | **0** | **200** | ❌ **MISMATCH!** |

**Root Cause**: The scene file has **stiffness=0 and damping=0** for arm links, which means:
- **No position control** (stiffness=0 = no spring force)
- **No damping** (damping=0 = no resistance)
- Joint cannot reach target position!

**Why Inspector shows different values**: 
- Inspector values are **overridden at runtime** by `XZeroLink.cs` script
- But scene file values are loaded first, and if script doesn't run or fails, joints remain with stiffness=0

### 2. **Drive Type Display Issue**

Both joints show `driveType: 0` in scene file (= "Target"), but Inspector shows "Force". This is likely:
- **Inspector display bug** or
- **Runtime override** by another script

### 3. **Target Value Difference**

- **Lift**: `target: 10` (non-zero, but works because stiffness=10)
- **Arm L3**: `target: 0` (zero, but doesn't work because stiffness=0)

---

## Why Lift Works But Arm Doesn't

### Lift (Working)
1. ✅ **Stiffness = 10** (provides spring force to reach target)
2. ✅ **Damping = 1000** (prevents oscillations)
3. ✅ **Target = 10** (non-zero, but within limits 0-1.1)
4. ✅ **Drive Type = Target** (position control enabled)

### Arm L3 (Not Working)
1. ❌ **Stiffness = 0** (NO spring force - cannot reach target!)
2. ❌ **Damping = 0** (no damping)
3. ⚠️ **Target = 0** (zero position)
4. ✅ **Drive Type = Target** (but ineffective with stiffness=0)

---

## Axis Alignment Verification

### Both Joints Have Identical Axis Configuration:

| Aspect | Lift | Arm L3 | Match? |
|--------|------|--------|--------|
| URDF Axis | `xyz="0.0 0.0 1.0"` (Z) | `xyz="0.0 0.0 1.0"` (Z) | ✅ |
| Unity Motion Axis | X | X | ✅ |
| Anchor Rotation | 90° Z | 90° Z | ✅ |
| Parent Anchor Position | `(-0.1666, 0, -0.037385)` | `(0, 0.013, 0)` | Different (expected) |
| Transform Position | `(-0.1666, 0, -0.037385)` | `(0, 0.013, 0)` | Different (expected) |

**Conclusion**: Axis alignment is **CORRECT** for both joints. The issue is **NOT axis-related**.

---

## URDF Origin Offset Impact

### Joint Arm L3
- URDF: `<origin xyz="0.0 0.0 0.013"/>`
- Unity Transform: `Y: 0.01300006` ✅ Matches URDF offset
- **Meaning**: Joint position 0.0 = 0.013m offset in world space (expected)

### Joint Lift
- URDF: `<origin xyz="-0.037385 0.1666 0.0" rpy="-1.5708 1.5708 0.0"/>`
- Unity Transform: `X: -0.1666, Y: 0, Z: -0.037385` ✅ Matches URDF offset
- **Meaning**: Joint position 0.0 = offset position in world space (expected)

**Conclusion**: URDF offsets are correctly applied. "Zero" in joint space ≠ "zero" in world space (this is expected URDF behavior).

---

## Root Cause Summary

### Primary Issue: **Stiffness = 0 in Scene File**

The Unity scene file has `stiffness: 0` and `damping: 0` for arm links, which means:
- **No position control** (stiffness=0 = no spring force to reach target)
- Joint cannot move to target position, even if Drive Type = "Target"

### Secondary Issue: **Inspector vs Scene File Mismatch**

- Inspector shows: `stiffness: 25000, damping: 200`
- Scene file shows: `stiffness: 0, damping: 0`
- **Script override** (`XZeroLink.cs`) sets values at runtime, but if script fails or doesn't run, joints remain with stiffness=0

### Why Lift Works:
- Scene file has `stiffness: 10, damping: 1000` (non-zero values)
- Even with Drive Type = "Force" in Inspector, position control works because stiffness > 0

---

## Recommended Fixes

### 1. **Fix Scene File Stiffness/Damping** (Highest Priority)
   - Open Unity scene
   - Select all 4 arm links (`link_arm_l3`, `link_arm_l2`, `link_arm_l1`, `link_arm_l0`)
   - Set ArticulationBody X Drive:
     - Stiffness: **10000** (match lift or use higher for arm)
     - Damping: **1000** (match lift)
     - Force Limit: **100** (from URDF)
   - **Save scene** to persist changes

### 2. **Verify Script Initialization**
   - Ensure `XZeroLink.cs` runs in `Start()` or `Awake()`
   - Add debug logs to confirm `InitializeDrive()` is called
   - Verify stiffness/damping are set correctly

### 3. **Check Drive Type**
   - Verify Drive Type = "Target" in Inspector (not "Force")
   - If Inspector shows "Force" but scene file shows `driveType: 0`, this is a display issue
   - The actual value is in scene file (`driveType: 0` = Target)

### 4. **Test with Non-Zero Target**
   - Try setting `target: 0.05` (small extension) instead of `0.0`
   - If this works, the issue is related to zero position handling

---

## Axis Alignment Conclusion

✅ **Axis alignment is CORRECT** for both lift and arm joints:
- Both use URDF Z-axis (`xyz="0.0 0.0 1.0"`)
- Both converted to Unity X-axis motion (via Ros2Unity + anchor rotation)
- Both have identical anchor rotation (90° Z rotation)
- URDF offsets are correctly applied

❌ **The issue is NOT axis-related** - it's **stiffness/damping = 0** preventing position control.

---

## Next Steps

1. **Fix scene file stiffness/damping** for all 4 arm links
2. **Verify script initialization** in `XZeroLink.cs`
3. **Test with non-zero target** to confirm position control works
4. **Compare with working lift** settings and replicate for arm links
