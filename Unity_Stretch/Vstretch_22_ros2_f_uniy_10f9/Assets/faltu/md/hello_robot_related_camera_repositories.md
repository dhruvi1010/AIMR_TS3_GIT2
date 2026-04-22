# Related Camera Repositories: Hello Robot Ecosystem

## Overview

Beyond the main `stretch_ros2` repository, Hello Robot maintains several related repositories that utilize cameras for various applications.

---

## 1. stretch_visual_servoing

### Repository
- **URL**: https://github.com/hello-robot/stretch_visual_servoing
- **Purpose**: Example code for visual servoing using Stretch 3's gripper camera

### Key Features
- **Gripper Camera**: Uses RealSense D405 camera mounted on gripper
- **Visual Servoing**: Position-based visual servoing for manipulation
- **Helper Modules**: Includes `d405_helpers.py` for D405 camera integration

### Camera Details
- **Model**: Intel RealSense D405 (smaller form factor than D435i)
- **Mounting**: Gripper-mounted for close-up manipulation tasks
- **Use Case**: Visual feedback for precise manipulation

### Integration
- Works alongside head-mounted D435i
- Provides different perspective (gripper view vs. head view)
- Enables manipulation with visual feedback

---

## 2. stretch_ai

### Repository
- **URL**: https://github.com/hello-robot/stretch_ai
- **Stars**: 210
- **Purpose**: AI-related packages for Stretch

### Camera Integration
- **Deep Learning Models**: Perception models using camera data
- **Data Collection**: Tools for collecting camera data for training
- **Model Deployment**: Running AI models on Stretch robots

### Related to stretch_deep_perception
- The `stretch_deep_perception` package in stretch_ros2 likely uses models from this repository
- Provides pre-trained models for object detection, recognition, etc.

---

## 3. stretch_tool_share

### Repository
- **URL**: https://github.com/hello-robot/stretch_tool_share
- **Purpose**: Alternative grippers, tools, and accessories

### Camera-Related Tools
- May include camera mounts or accessories
- Alternative camera configurations
- Tool-specific camera integrations

---

## 4. stretch_diagnostics

### Repository
- **URL**: https://github.com/hello-robot/stretch_diagnostics
- **Purpose**: Testing and debugging tools

### Camera Diagnostics
- **RealSense Tests**: Specific test suites for RealSense cameras
- **Camera Health Checks**: Verify camera functionality
- **Calibration Verification**: Test camera calibration accuracy

### Test Suites Include
- RealSense camera connectivity
- Image quality checks
- Depth sensor accuracy
- IMU functionality
- Frame rate verification

---

## Camera Comparison: D435i vs D405

### D435i (Head Camera)
- **Location**: Mounted on robot head
- **Use Case**: General perception, navigation, mapping
- **Field of View**: Wider FOV for general observation
- **Resolution**: Higher resolution options
- **IMU**: Includes 6-axis IMU

### D405 (Gripper Camera)
- **Location**: Mounted on gripper
- **Use Case**: Close-up manipulation, visual servoing
- **Field of View**: Optimized for close-range work
- **Form Factor**: Smaller, more compact
- **Integration**: Used in stretch_visual_servoing

---

## Multi-Camera Setup

### Typical Configuration
1. **Head D435i**: General perception, navigation, SLAM
2. **Gripper D405**: Manipulation, visual servoing, close-up tasks

### Coordination
- Both cameras can run simultaneously
- Different namespaces: `/head_camera/` and `/gripper_camera/`
- Independent launch files for each camera
- Can be used together for complex manipulation tasks

---

## Integration Patterns

### Pattern 1: Head Camera for Navigation
```
Head D435i → SLAM → Navigation Stack → Path Planning
```

### Pattern 2: Gripper Camera for Manipulation
```
Gripper D405 → Visual Servoing → Gripper Control → Manipulation
```

### Pattern 3: Combined Perception
```
Head D435i → Object Detection → Planning
Gripper D405 → Visual Feedback → Execution
```

---

## Related Documentation

### Visual Servoing
- See stretch_visual_servoing repository for examples
- Demonstrates gripper camera usage
- Shows integration with manipulation pipeline

### AI/Perception
- stretch_ai for deep learning models
- stretch_deep_perception for perception pipelines
- Data collection workflows

### Diagnostics
- stretch_diagnostics for camera health checks
- Calibration verification tools
- Performance monitoring

---

## Key Takeaways

1. **Multiple Cameras**: Stretch supports head and gripper cameras
2. **Different Models**: D435i for head, D405 for gripper
3. **Specialized Use**: Each camera optimized for different tasks
4. **AI Integration**: Camera data used in deep learning pipelines
5. **Diagnostics**: Tools available for camera health monitoring

---

*Related Repositories Research - Last Updated: January 26, 2026*
