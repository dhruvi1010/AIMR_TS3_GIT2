
    
    # VR Teleoperation of Hello Robot Stretch 3 using ROS2ForUnity
    
    A Unity-based VR teleoperation system for controlling the **Hello Robot Stretch 3** mobile manipulator through a **Meta Quest 3** headset. The system uses **ROS2ForUnity** (Humble) for direct, low-latency DDS communication between Unity and the robot — no intermediate bridge servers or websockets required.
    
    ---
    
    ## Overview
    
    This project enables immersive VR teleoperation of a Stretch 3 robot. The operator wears a Meta Quest 3 headset and uses head tracking and controller inputs to command the robot's joints in real time, while receiving live camera feeds from the robot's onboard Intel RealSense cameras directly inside the headset.
    
    The communication architecture relies on **ROS2ForUnity**, which embeds a native ROS 2 (Humble) node inside the Unity application. This means Unity publishes and subscribes to ROS 2 topics over DDS, sitting on the same ROS 2 network as the robot with no translation layer.
    
    ### Key Features
    
    - **Full joint control** — Base, lift, telescoping arm (4 segments), dexterous wrist (yaw/pitch/roll), and gripper
    - **VR head tracking → robot head** — The Quest 3's head orientation drives the robot's pan/tilt camera head with calibration, dead zones, and smoothing
    - **Live camera feeds in VR** — Streams from the D435i (navigation) and D405 (gripper) cameras rendered on in-headset UI panels
    - **Bidirectional sync** — Unity visualization mirrors real robot joint positions via `/stretch/joint_states` subscriber, with velocity-based latency prediction
    - **Dual control modes** — Toggle between Base Mode (left joystick drives the mobile base) and Wrist Mode (left joystick controls wrist pitch/roll, triggers control yaw)
    - **Robot homing** — Trigger the robot's home calibration routine from within VR
    
    ---
    
    ## Architecture
    
    ```
    ┌─────────────────────────────────┐         DDS (ROS 2 Humble)         ┌──────────────────────────┐
    │         Unity (Quest 3)         │◄──────────────────────────────────►│    Stretch 3 Robot       │
    │                                 │                                    │                          │
    │  ROS2ForUnity (embedded node)   │   /stretch/cmd_vel (Twist)    ──►  │  stretch_driver          │
    │                                 │   /stretch_controller/             │  (joint control)         │
    │  Publishers:                    │     follow_joint_trajectory/       │                          │
    │   - cmd_vel (base)              │     goal (JointTrajectory)    ──►  │  unity_lift bridge ──►   │
    │   - joint trajectory (lift,     │                                    │  unity_head bridge ──►   │
    │     arm, wrist, gripper, head)  │   /stretch/joint_states       ◄──  │  (action clients)        │
    │                                 │     (JointState @ 30 Hz)           │                          │
    │  Subscribers:                   │                                    │  realsense cameras       │
    │   - joint_states (all joints)   │   /camera/.../compressed      ◄──  │  (D435i + D405)          │
    │   - D435i camera feed           │   /gripper_camera/image_raw   ◄──  │                          │
    │   - D405 camera feed            │                                    │                          │
    └─────────────────────────────────┘                                    └──────────────────────────┘
    ```
    
    ### Communication Flow
    
    **Commands (Unity → Robot):**
    Unity publishes `JointTrajectory` messages to `/stretch_controller/follow_joint_trajectory/goal`. On the robot side, lightweight Python bridge nodes (`unity_lift.py`, `unity_head.py`) subscribe to this topic and forward the trajectories as action goals to the Stretch driver's `FollowJointTrajectory` action server. Base movement is published directly as `Twist` on `/stretch/cmd_vel`.
    
    **State Feedback (Robot → Unity):**
    The robot's `stretch_driver` publishes `JointState` messages at 30 Hz on `/stretch/joint_states`. Unity subscribes to this and updates the 3D model's `ArticulationBody` joints to mirror the real robot's pose, using velocity-based prediction to compensate for network latency.
    
    **Camera Feeds (Robot → Unity):**
    RealSense camera topics are subscribed directly via ROS2ForUnity. Compressed JPEG streams are decoded in Unity and rendered onto UI panels in the VR scene.
    
    ---
    
    ## Repository Structure
    
    ```
    ├── Unity_Stretch/
    │   └── Vstretch_22_ros2_f_uniy_10f9/        # Unity project (2022.3 LTS, URP)
    │       ├── Assets/
    │       │   ├── Ros2ForUnity/                 # ROS2ForUnity plugin (Humble, v1.3.0)
    │       │   ├── Stretch files/                # URDF-based robot description
    │       │   ├── stretch.prefab                # Stretch 3 prefab with ArticulationBodies
    │       │   │
    │       │   ├── Xdriver/                      # ★ Primary joint controllers (ROS2)
    │       │   │   ├── Xbestsynclift.cs          #   Lift — joystick + sync from joint_states
    │       │   │   ├── XdriveArmLInk.cs          #   Arm — 4-segment telescoping extension
    │       │   │   ├── xdrivewrist.cs            #   Wrist — yaw/pitch/roll (dexterous wrist)
    │       │   │   ├── xdrivegripper.cs          #   Gripper — button-triggered open/close
    │       │   │   └── XsyncLIft.cs              #   Lift sync variant
    │       │   │
    │       │   ├── r2u_Scripts/                  # Base & head controllers (ROS2)
    │       │   │   ├── ros2baseKeyController.cs  #   Keyboard-based base movement
    │       │   │   ├── ros2basejoystickcontroller.cs  # Joystick-based base movement
    │       │   │   └── ros2 HeadController.cs    #   VR head tracking → robot pan/tilt
    │       │   │
    │       │   ├── uni_manager/                  # Joint state subscribers
    │       │   │   ├── LiftSub.cs                #   Subscribes to joint_states (single joint)
    │       │   │   ├── StretchJointStateSub.cs   #   Subscribes to joint_states (all joints)
    │       │   │   └── ControlModeManager.cs     #   Base/Wrist mode toggle
    │       │   │
    │       │   ├── Camera_feed/                  # Camera display scripts (ROS2)
    │       │   │   ├── ros2d435iListner.cs       #   D435i navigation camera subscriber
    │       │   │   ├── ros2d405sub.cs            #   D405 gripper camera subscriber
    │       │   │   ├── DisplayManager.cs         #   Camera view switching
    │       │   │   └── CameraPositionManager.cs  #   Camera panel positioning in VR
    │       │   │
    │       │   ├── try_test/                     # Development/test scripts
    │       │   │   ├── CallTheHome.cs            #   Trigger robot homing
    │       │   │   ├── HomeRobot.cs              #   ROS2 home command publisher
    │       │   │   └── ...                       #   Various iteration scripts
    │       │   │
    │       │   ├── direct/                       # Alternative direct-control scripts
    │       │   │   ├── BaseJoyContoller.cs        #   Base joystick (with mode toggle)
    │       │   │   ├── fixmovelift.cs            #   Lift control variant
    │       │   │   ├── LinkArm.cs                #   Arm control variant
    │       │   │   ├── grippercontrolviz.cs      #   Gripper with visualization
    │       │   │   ├── wristButton.cs            #   Wrist with button controls
    │       │   │   └── syncliftbyjoy.cs          #   Lift with joystick sync
    │       │   │
    │       │   ├── XR/                           # XR Interaction Toolkit settings
    │       │   ├── XRI/                          # XR input action maps
    │       │   └── Scenes/                       # Unity scenes
    │       │
    │       ├── Packages/                         # Unity package manifest
    │       └── ProjectSettings/                  # Unity project settings
    │
    └── ament_ws_3018/                            # ROS 2 workspace (runs on robot)
        └── src/
            ├── unity_stretch/                    # ★ Custom bridge nodes
            │   └── unity_stretch/
            │       ├── unity_lift.py             #   Topic → Action bridge for lift/arm/wrist/gripper
            │       ├── unity_head.py             #   Topic → Action bridge for head pan/tilt
            │       ├── stretch_home_topic.py     #   Home command handler
            │       ├── stretch_home_service.py   #   Home service variant
            │       └── stretch_head_state_reader.py  # Head state diagnostic
            │
            ├── stretch_ros2/                     # Hello Robot's ROS 2 driver stack
            ├── realsense-ros/                    # Intel RealSense ROS 2 wrapper
            ├── sllidar_ros2/                     # Slamtec LIDAR driver
            ├── rosbridge_suite/                  # Rosbridge (not used in ROS2ForUnity method)
            ├── audio_common/                     # Audio capture/playback
            ├── respeaker_ros2/                   # ReSpeaker mic array
            ├── ros2_numpy/                       # NumPy message conversion utilities
            ├── stretch_web_teleop/               # Web teleop interface (reference)
            └── tf2_web_republisher_py/           # TF2 republisher
    ```
    
    ---
    
    ## Prerequisites
    
    ### Robot Side (Stretch 3)
    
    - **Hello Robot Stretch 3** with Ubuntu 22.04
    - **ROS 2 Humble** installed
    - **stretch_ros2** driver stack
    - **Intel RealSense SDK** + `realsense-ros` for D435i and D405 cameras
    - Python packages: `stretch_body`, `control_msgs`, `trajectory_msgs`
    
    ### Development Machine / VR Side
    
    - **Unity 2022.3 LTS** (URP 14.x)
    - **ROS2ForUnity** plugin (Humble, v1.3.0 — included in repo)
    - **Meta Quest 3** headset
    - **OpenXR** runtime + **XR Interaction Toolkit 2.6.5**
    - Both machines must be on the **same network** with DDS multicast enabled (or use a shared `ROS_DOMAIN_ID`)
    
    ---
    
    ## Setup
    
    ### 1. Robot — Build the ROS 2 Workspace
    
    ```bash
    cd ament_ws_3018
    colcon build
    source install/setup.bash
    ```
    
    ### 2. Robot — Launch the Stretch Driver
    
    ```bash
    # Start the Stretch driver in navigation mode
    ros2 launch stretch_core stretch_driver.launch.py mode:=navigation
    
    # Start RealSense cameras
    ros2 launch realsense2_camera rs_launch.py
    ```
    
    ### 3. Robot — Run the Bridge Nodes
    
    The bridge nodes convert Unity's `JointTrajectory` topic publishes into action goals that the Stretch driver accepts:
    
    ```bash
    # Terminal 1: Lift/Arm/Wrist/Gripper bridge
    ros2 run unity_stretch unity_lift
    
    # Terminal 2: Head pan/tilt bridge
    ros2 run unity_stretch unity_head
    
    # Terminal 3 (optional): Homing command listener
    ros2 run unity_stretch stretch_home_topic
    ```
    
    ### 4. Unity — Open and Configure the Project
    
    1. Open the `Unity_Stretch/Vstretch_22_ros2_f_uniy_10f9` project in Unity 2022.3 LTS
    2. Ensure the **ROS2ForUnity** plugin is present in `Assets/Ros2ForUnity/`
    3. Set `ROS_DOMAIN_ID` to match the robot (default: 0)
    4. Verify the scene has a `ROS2UnityComponent` attached to the manager GameObject
    5. Build and deploy to **Meta Quest 3** via Android/OpenXR build target
    
    ---
    
    ## VR Controls
    
    | Input | Action |
    | --- | --- |
    | **Left Joystick** (Base Mode) | Drive mobile base — forward/back and rotate |
    | **Left Joystick** (Wrist Mode) | Control wrist pitch and roll |
    | **Left/Right Triggers** (Wrist Mode) | Control wrist yaw |
    | **Right Joystick Y-axis** | Raise/lower the lift |
    | **Right Joystick X-axis** | Extend/retract the telescoping arm |
    | **Right Hand Button (A/B)** | Open/close gripper |
    | **Head Movement** | Pan and tilt the robot's head camera |
    | **Primary Button (Right)** | Recalibrate head tracking center |
    | **Mode Toggle Button** | Switch between Base Mode and Wrist Mode |
    | **Enter Key** (keyboard) | Home the robot |
    
    ---
    
    ## ROS 2 Topics
    
    ### Published by Unity
    
    | Topic | Type | Description |
    | --- | --- | --- |
    | `/stretch/cmd_vel` | `geometry_msgs/Twist` | Mobile base velocity commands |
    | `/stretch_controller/follow_joint_trajectory/goal` | `trajectory_msgs/JointTrajectory` | Joint commands for lift, arm, wrist, gripper, and head |
    | `/stretch/home_command` | `std_msgs/String` | Trigger robot homing |
    
    ### Subscribed by Unity
    
    | Topic | Type | Description |
    | --- | --- | --- |
    | `/stretch/joint_states` | `sensor_msgs/JointState` | All joint positions, velocities, and efforts (30 Hz) |
    | `/camera/camera/color/image_raw/compressed` | `sensor_msgs/CompressedImage` | D435i navigation camera feed |
    | `/gripper_camera/color/image_rect_raw` | `sensor_msgs/Image` | D405 gripper camera feed |
    
    ---
    
    ## Key Design Decisions
    
    **Why ROS2ForUnity over TCP/UDP/Rosbridge?** ROS2ForUnity embeds a full ROS 2 participant inside Unity, communicating over DDS. This eliminates serialization/deserialization overhead, avoids maintaining a separate bridge server, and gives native access to ROS 2 QoS profiles. The result is lower latency and simpler deployment compared to TCP socket or websocket approaches.
    
    **Topic-to-Action Bridge Pattern:** ROS2ForUnity can publish to topics but does not natively support ROS 2 action clients. The `unity_lift.py` and `unity_head.py` bridge nodes solve this by subscribing to a topic and forwarding each message as a `FollowJointTrajectory` action goal to the Stretch driver.
    
    **ArticulationBody for Visualization:** The Unity-side Stretch model uses `ArticulationBody` components with prismatic and revolute joints. Joint targets are set from incoming `/stretch/joint_states` data, ensuring the VR model mirrors the physical robot. High stiffness drive parameters (50,000) provide near-instant visual response.
    
    **Velocity Prediction:** To compensate for network latency (~50 ms), the lift and arm sync scripts use the velocity field from `JointState` messages to predict the robot's position slightly ahead, reducing perceived lag.
    
    ---
    
    ## Acknowledgments
    
    - [ROS2ForUnity](https://github.com/RobotecAI/ros2-for-unity) by [Robotec.AI](http://robotec.ai/) — ROS 2 integration plugin for Unity
    - [Hello Robot](https://hello-robot.com/) — Stretch 3 robot platform and `stretch_ros2` driver stack
    - [Intel RealSense](https://github.com/IntelRealSense/realsense-ros) — RealSense ROS 2 wrapper
    
    ---
    
    ## License
    
    This project is for academic and research purposes. Third-party packages (`stretch_ros2`, `realsense-ros`, `ROS2ForUnity`, etc.) retain their original licenses.
