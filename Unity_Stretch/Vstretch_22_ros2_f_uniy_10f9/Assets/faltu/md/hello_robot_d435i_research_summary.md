# Research Summary: Hello Robot D435i Camera Integration

## Quick Reference Guide

### Essential Commands

#### Launch Camera
```bash
# Terminal 1: Start robot driver
ros2 launch stretch_core stretch_driver.launch.py

# Terminal 2: Start camera (low resolution)
ros2 launch stretch_core d435i_low_resolution.launch.py

# Terminal 2: Start camera (high resolution)
ros2 launch stretch_core d435i_high_resolution.launch.py
```

#### View Camera Data
```bash
# View images
ros2 run rqt_image_view rqt_image_view

# 3D visualization
ros2 run rviz2 rviz2

# Monitor topics
ros2 topic list | grep camera
ros2 topic hz /camera/color/image_raw
ros2 topic echo /camera/color/image_raw --no-arr
```

#### Check System Status
```bash
# List all nodes
ros2 node list

# List all topics
ros2 topic list

# View topic info
ros2 topic info /camera/color/image_raw -v

# View node graph
ros2 run rqt_graph rqt_graph
```

---

## Key Topics Reference

### RGB/Color Topics
- **`/camera/color/image_raw`** - Raw RGB images (sensor_msgs/Image)
- **`/camera/color/camera_info`** - Camera calibration (sensor_msgs/CameraInfo)

### Depth Topics
- **`/camera/depth/image_rect_raw`** - Rectified depth images (sensor_msgs/Image, 16UC1)
- **`/camera/depth/camera_info`** - Depth camera calibration
- **`/camera/depth/color/points`** - Point cloud with RGB (sensor_msgs/PointCloud2)

### IMU Topics
- **`/camera/imu`** - 6-axis IMU data (sensor_msgs/Imu)

### Calibration Topics
- **`/camera/extrinsics/depth_to_color`** - Depth-to-color transformation

---

## Architecture Overview

### Data Flow Summary
```
D435i Hardware
    ↓ (USB 3.0)
librealsense2 SDK
    ↓ (C++ API)
realsense2_camera Node
    ↓ (ROS2 Topics)
Subscribers (RViz2, rqt_image_view, User Nodes)
```

### Key Components
1. **Hardware**: Intel RealSense D435i (RGB + Depth + IMU)
2. **Driver**: librealsense2 SDK (low-level camera interface)
3. **ROS2 Node**: realsense2_camera (wraps SDK, publishes topics)
4. **Launch Files**: Stretch provides convenient launch configurations
5. **Visualization**: RViz2, rqt_image_view for viewing data

---

## Repository Structure

### Main Repository
- **stretch_ros2**: https://github.com/hello-robot/stretch_ros2
  - **stretch_core**: Core drivers and camera launch files
  - **stretch_calibration**: Camera-robot calibration tools
  - **stretch_deep_perception**: Deep learning perception models

### Dependencies
- **realsense-ros**: https://github.com/IntelRealSense/realsense-ros
  - ROS2 wrapper for RealSense cameras
  - Provides realsense2_camera node

---

## Common Use Cases

### 1. Capture Images
```python
# Subscribe to /camera/color/image_raw
# Convert using cv_bridge
# Save using OpenCV
```

### 2. Depth Perception
```python
# Subscribe to /camera/depth/image_rect_raw
# Convert to meters (divide by 1000)
# Process for obstacle detection
```

### 3. 3D Mapping
```python
# Subscribe to /camera/depth/color/points
# Use PointCloud2 for SLAM/mapping
# Integrate with navigation stack
```

### 4. Visual Servoing
- Use RGB images for visual feedback
- Combine with depth for 3D positioning
- See: stretch_visual_servoing repository

---

## Configuration Parameters

### Resolution Options
- **Low**: 640x480 @ 30fps (default for d435i_low_resolution)
- **Medium**: 1280x720 @ 15fps
- **High**: 1920x1080 @ 6fps

### Feature Flags
- `pointcloud.enable:=true` - Generate point clouds
- `align_depth.enable:=true` - Align depth to color
- `enable_depth:=true` - Enable depth stream
- `enable_color:=true` - Enable color stream
- `enable_imu:=true` - Enable IMU stream

---

## Troubleshooting Checklist

- [ ] Camera detected: `ros2 topic list | grep camera`
- [ ] Topics publishing: `ros2 topic hz /camera/color/image_raw`
- [ ] QoS compatible: `ros2 topic info -v /camera/color/image_raw`
- [ ] USB connection: Check USB 3.0 port
- [ ] Permissions: May need udev rules for USB access
- [ ] Calibration: Run stretch_calibration if needed

---

## Related Documentation

1. **Main Research Document**: `hello_robot_d435i_camera_research.md`
   - Complete overview of repository, topics, nodes, launch files

2. **Technical Implementation**: `hello_robot_d435i_technical_implementation.md`
   - Low-level details, code examples, performance optimization

3. **Official Docs**:
   - Hello Robot: https://docs.hello-robot.com/
   - RealSense ROS2: https://dev.intelrealsense.com/docs/ros2-wrapper

---

## Key Takeaways

1. **D435i provides**: RGB images, depth images, IMU data, point clouds
2. **Main node**: realsense2_camera (from realsense-ros package)
3. **Launch files**: stretch_core provides d435i_*.launch.py files
4. **Topics**: All data published to standardized ROS2 topics
5. **Visualization**: Use RViz2 for 3D, rqt_image_view for 2D
6. **Processing**: Subscribe to topics in your own nodes for custom processing

---

*Summary Document - Last Updated: January 26, 2026*
