using UnityEngine;
using UnityEngine.InputSystem;
using ROS2;
using trajectory_msgs.msg;
using builtin_interfaces.msg;

/// <summary>
/// Synchronized arm control: Joystick input controls real robot, Unity visualization syncs with real robot position
/// Uses ArticulationBody X Drive method for accurate physics-based visualization
/// Controls all 4 telescoping arm segments: joint_arm_l3, joint_arm_l2, joint_arm_l1, joint_arm_l0
/// - Joystick input → Sends commands to real robot via ROS2
/// - Real robot position from StretchJointStateSub → Syncs Unity visualization
/// This ensures Unity always shows what the robot is actually doing (perfect sync)
/// </summary>
public class XsyncLinkArm : MonoBehaviour
{
    [Header("ROS2 Settings")]
    [Tooltip("Topic to publish arm commands (action goal topic)")]
    public string armCommandTopic = "/stretch_controller/follow_joint_trajectory/goal";

    [Header("StretchJointStateSub Reference")]
    [Tooltip("StretchJointStateSub component that subscribes to /stretch/joint_states - REQUIRED")]
    public StretchJointStateSub jointStateSubscriber;

    [Header("ArticulationBody References - All 4 Telescoping Segments")]
    [Tooltip("ArticulationBody for link_arm_l3 (closest to lift/base) - REQUIRED")]
    public ArticulationBody armL3Articulation;

    [Tooltip("ArticulationBody for link_arm_l2 - REQUIRED")]
    public ArticulationBody armL2Articulation;

    [Tooltip("ArticulationBody for link_arm_l1 - REQUIRED")]
    public ArticulationBody armL1Articulation;

    [Tooltip("ArticulationBody for link_arm_l0 (closest to wrist) - REQUIRED")]
    public ArticulationBody armL0Articulation;

    [Header("Auto-Find Settings")]
    [Tooltip("Automatically find StretchJointStateSub component in scene")]
    public bool autoFindJointStateSub = true;

    [Tooltip("Automatically find ArticulationBody components by GameObject name")]
    public bool autoFindArticulations = true;

    [Header("Joystick Input")]
    [Tooltip("Right hand joystick InputActionReference for arm control")]
    public InputActionReference rightHandJoystick;

    [Header("ArticulationBody Drive Settings")]
    [Tooltip("Stiffness for ArticulationBody drive (higher = stiffer, more responsive)")]
    public float stiffness = 10000f;

    [Tooltip("Damping for ArticulationBody drive (higher = more damped, smoother)")]
    public float damping = 1000f;

    [Tooltip("Force limit for ArticulationBody drive")]
    public float forceLimit = 100f;

    [Header("Movement Settings")]
    [Tooltip("Movement speed (meters per second) - Higher = faster robot responsiveness")]
    public float armSpeed = 0.2f; // m/s

    [Header("Trajectory Settings")]
    [Tooltip("Trajectory duration (seconds) - Lower = faster robot movement")]
    public float duration = 0.5f;

    [Header("Publishing Settings")]
    [Tooltip("Minimum time between publishes (seconds) - Match gamepad teleop: 0.05s = 20 Hz")]
    public float publishInterval = 0.05f;

    [Tooltip("Minimum position change to trigger publish (meters)")]
    public float minPublishPositionChange = 0.002f;

    [Header("Arm Limits (meters)")]
    [Tooltip("Minimum arm position (meters) - from URDF: 0.0 (fully retracted)")]
    public float armMinPosition = 0.0f;

    [Tooltip("Maximum arm position (meters) - from URDF: 0.52 (fully extended)")]
    public float armMaxPosition = 0.52f;

    [Tooltip("Maximum extension per joint (meters) - from URDF: 0.13m per telescoping segment")]
    public float maxExtensionPerJoint = 0.13f;

    [Header("Debug")]
    public bool showDebugLogs = true;

    // ROS2 components
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<JointTrajectory> armPublisher;
    private bool isInitialized = false;

    // Internal state - target positions from real robot (for Unity visualization)
    private float targetArmL3 = 0.0f;
    private float targetArmL2 = 0.0f;
    private float targetArmL1 = 0.0f;
    private float targetArmL0 = 0.0f;
    private float currentArmL3 = 0.0f; // Current Unity visualization positions
    private float currentArmL2 = 0.0f;
    private float currentArmL1 = 0.0f;
    private float currentArmL0 = 0.0f;
    private float commandTotalExtension = 0.0f; // Position to send to robot (from joystick)
    private float lastPublishedTotalExtension = 0.0f;
    private float lastPublishTime = 0.0f;
    private bool armL3Found = false;
    private bool armL2Found = false;
    private bool armL1Found = false;
    private bool armL0Found = false;
    private bool jointStateSubFound = false;

    // Joint names from URDF
    private const string JOINT_ARM_L3 = "joint_arm_l3";
    private const string JOINT_ARM_L2 = "joint_arm_l2";
    private const string JOINT_ARM_L1 = "joint_arm_l1";
    private const string JOINT_ARM_L0 = "joint_arm_l0";

    void Start()
    {
        if (rightHandJoystick == null)
        {
            Debug.LogError("XsyncLinkArm: Right hand joystick InputActionReference not assigned!");
        }

        // Auto-find StretchJointStateSub component if enabled
        if (autoFindJointStateSub && jointStateSubscriber == null)
        {
            jointStateSubscriber = FindObjectOfType<StretchJointStateSub>();
            if (jointStateSubscriber != null && showDebugLogs)
            {
                Debug.Log("XsyncLinkArm: Auto-found StretchJointStateSub component");
            }
        }

        // Validate StretchJointStateSub is found
        if (jointStateSubscriber == null)
        {
            Debug.LogError("XsyncLinkArm: StretchJointStateSub component not found! Assign in Inspector or enable auto-find.");
        }
        else
        {
            jointStateSubFound = true;
        }

        // Auto-find ArticulationBody components if enabled
        if (autoFindArticulations)
        {
            FindArticulationBodies();
        }

        // Validate all articulations are found
        ValidateArticulations();

        // Initialize ArticulationBody drive parameters
        InitializeArticulationDrives();

        // Load current positions from ArticulationBodies
        LoadCurrentPositions();

        // Initialize ROS2
        ros2Unity = GetComponent<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            ros2Unity = FindObjectOfType<ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                Debug.LogError("XsyncLinkArm: ROS2UnityComponent not found! Add ROS2UnityCore to scene.");
            }
        }

        if (showDebugLogs)
        {
            Debug.Log("XsyncLinkArm: Initialized");
            Debug.Log($"XsyncLinkArm: Arm limits: [{armMinPosition:F2}, {armMaxPosition:F2}] meters (total extension)");
            Debug.Log($"XsyncLinkArm: Max extension per joint: {maxExtensionPerJoint:F3}m (4 telescoping segments)");
            Debug.Log($"XsyncLinkArm: Using ArticulationBody X Drive for synchronized control");
            Debug.Log($"XsyncLinkArm: Will send all 4 telescoping joints: {JOINT_ARM_L3}, {JOINT_ARM_L2}, {JOINT_ARM_L1}, {JOINT_ARM_L0}");
            if (jointStateSubFound)
            {
                Debug.Log($"XsyncLinkArm: StretchJointStateSub found - will sync Unity from /stretch/joint_states");
            }
            else
            {
                Debug.LogError("XsyncLinkArm: StretchJointStateSub not found!");
            }
            LogArticulationStatus();
        }
    }

    void Update()
    {
        // Try to initialize ROS2 if not already initialized
        if (!isInitialized)
        {
            if (ros2Unity != null && ros2Unity.Ok())
            {
                InitializeROS2();
            }
        }

        // Handle joystick input and send commands to robot
        if (rightHandJoystick != null && isInitialized && armPublisher != null)
        {
            HandleJoystickInput();
        }

        // Sync Unity visualization with real robot position
        if (jointStateSubFound && (armL3Found || armL2Found || armL1Found || armL0Found))
        {
            SyncUnityVisualization();
        }
    }

    /// <summary>
    /// Handle joystick input and send commands to real robot
    /// </summary>
    void HandleJoystickInput()
    {
        // Get joystick input (x-axis for left/right movement = expand/contract)
        Vector2 joystickInput = rightHandJoystick.action.ReadValue<Vector2>();
        float horizontalInput = joystickInput.x; // x-axis is left/right on joystick

        // Update command position based on input
        // Right (positive x) = extend arm, Left (negative x) = retract arm
        if (Mathf.Abs(horizontalInput) > 0.01f)
        {
            commandTotalExtension += horizontalInput * armSpeed * UnityEngine.Time.deltaTime;
            commandTotalExtension = Mathf.Clamp(commandTotalExtension, armMinPosition, armMaxPosition);

            // Publish to robot with throttling
            float timeSinceLastPublish = UnityEngine.Time.time - lastPublishTime;
            float positionChange = Mathf.Abs(commandTotalExtension - lastPublishedTotalExtension);

            // Rate limiting: Only publish if enough time passed AND position changed significantly
            if (timeSinceLastPublish >= publishInterval && positionChange >= minPublishPositionChange)
            {
                SendCommand(commandTotalExtension);
                lastPublishedTotalExtension = commandTotalExtension;
                lastPublishTime = UnityEngine.Time.time;

                if (showDebugLogs && UnityEngine.Time.frameCount % 60 == 0)
                {
                    Debug.Log($"XsyncLinkArm: Sent command - total extension: {commandTotalExtension:F3}m");
                }
            }
        }
    }

    /// <summary>
    /// Sync Unity visualization with real robot position from StretchJointStateSub
    /// </summary>
    void SyncUnityVisualization()
    {
        // Check if StretchJointStateSub has received any messages from robot
        if (!jointStateSubscriber.HasReceivedMessage())
        {
            // Wait for first message - don't sync until we have real robot data
            if (showDebugLogs && UnityEngine.Time.frameCount % 300 == 0) // Log every 5 seconds
            {
                Debug.LogWarning("XsyncLinkArm: Waiting for joint_states messages from robot...");
            }
            return;
        }

        // Check if joints exist in joint_states (first time only)
        if (showDebugLogs && UnityEngine.Time.frameCount % 300 == 0) // Log every 5 seconds
        {
            bool hasL3 = jointStateSubscriber.HasJoint(JOINT_ARM_L3);
            bool hasL2 = jointStateSubscriber.HasJoint(JOINT_ARM_L2);
            bool hasL1 = jointStateSubscriber.HasJoint(JOINT_ARM_L1);
            bool hasL0 = jointStateSubscriber.HasJoint(JOINT_ARM_L0);
            Debug.Log($"XsyncLinkArm: Joint availability - L3: {hasL3}, L2: {hasL2}, L1: {hasL1}, L0: {hasL0}");
            Debug.Log($"XsyncLinkArm: ArticulationBody status - L3: {armL3Found}, L2: {armL2Found}, L1: {armL1Found}, L0: {armL0Found}");
        }

        // Get real robot positions from StretchJointStateSub
        float realRobotL3 = jointStateSubscriber.GetJointPosition(JOINT_ARM_L3);
        float realRobotL2 = jointStateSubscriber.GetJointPosition(JOINT_ARM_L2);
        float realRobotL1 = jointStateSubscriber.GetJointPosition(JOINT_ARM_L1);
        float realRobotL0 = jointStateSubscriber.GetJointPosition(JOINT_ARM_L0);

        // Clamp to valid ranges (each joint: 0.0 to 0.13m)
        realRobotL3 = Mathf.Clamp(realRobotL3, 0.0f, maxExtensionPerJoint);
        realRobotL2 = Mathf.Clamp(realRobotL2, 0.0f, maxExtensionPerJoint);
        realRobotL1 = Mathf.Clamp(realRobotL1, 0.0f, maxExtensionPerJoint);
        realRobotL0 = Mathf.Clamp(realRobotL0, 0.0f, maxExtensionPerJoint);

        // Update target positions for Unity visualization
        targetArmL3 = realRobotL3;
        targetArmL2 = realRobotL2;
        targetArmL1 = realRobotL1;
        targetArmL0 = realRobotL0;

        // Instant sync - Unity visualization immediately matches real robot position
        currentArmL3 = targetArmL3;
        currentArmL2 = targetArmL2;
        currentArmL1 = targetArmL1;
        currentArmL0 = targetArmL0;

        // Always update ArticulationBody every frame for best synchronization
        UpdateArticulationBodies();

        if (showDebugLogs && UnityEngine.Time.frameCount % 60 == 0) // Log once per second
        {
            float totalRealExtension = realRobotL3 + realRobotL2 + realRobotL1 + realRobotL0;
            float totalUnityExtension = currentArmL3 + currentArmL2 + currentArmL1 + currentArmL0;
            Debug.Log($"XsyncLinkArm: Synced - Real Robot Total: {totalRealExtension:F3}m | Unity Total: {totalUnityExtension:F3}m | Command: {commandTotalExtension:F3}m");
            Debug.Log($"XsyncLinkArm: Real Robot - L3: {realRobotL3:F3}m, L2: {realRobotL2:F3}m, L1: {realRobotL1:F3}m, L0: {realRobotL0:F3}m");
        }
    }

    /// <summary>
    /// Auto-find ArticulationBody components by GameObject name
    /// </summary>
    void FindArticulationBodies()
    {
        if (armL3Articulation == null)
        {
            GameObject l3Obj = GameObject.Find("link_arm_l3");
            if (l3Obj != null)
            {
                armL3Articulation = l3Obj.GetComponent<ArticulationBody>();
                if (armL3Articulation != null && showDebugLogs)
                    Debug.Log($"XsyncLinkArm: Auto-found armL3Articulation on {l3Obj.name}");
            }
        }

        if (armL2Articulation == null)
        {
            GameObject l2Obj = GameObject.Find("link_arm_l2");
            if (l2Obj != null)
            {
                armL2Articulation = l2Obj.GetComponent<ArticulationBody>();
                if (armL2Articulation != null && showDebugLogs)
                    Debug.Log($"XsyncLinkArm: Auto-found armL2Articulation on {l2Obj.name}");
            }
        }

        if (armL1Articulation == null)
        {
            GameObject l1Obj = GameObject.Find("link_arm_l1");
            if (l1Obj != null)
            {
                armL1Articulation = l1Obj.GetComponent<ArticulationBody>();
                if (armL1Articulation != null && showDebugLogs)
                    Debug.Log($"XsyncLinkArm: Auto-found armL1Articulation on {l1Obj.name}");
            }
        }

        if (armL0Articulation == null)
        {
            GameObject l0Obj = GameObject.Find("link_arm_l0");
            if (l0Obj != null)
            {
                armL0Articulation = l0Obj.GetComponent<ArticulationBody>();
                if (armL0Articulation != null && showDebugLogs)
                    Debug.Log($"XsyncLinkArm: Auto-found armL0Articulation on {l0Obj.name}");
            }
        }
    }

    /// <summary>
    /// Validate that all required ArticulationBodies are found and are prismatic joints
    /// </summary>
    void ValidateArticulations()
    {
        armL3Found = armL3Articulation != null && armL3Articulation.jointType == ArticulationJointType.PrismaticJoint;
        armL2Found = armL2Articulation != null && armL2Articulation.jointType == ArticulationJointType.PrismaticJoint;
        armL1Found = armL1Articulation != null && armL1Articulation.jointType == ArticulationJointType.PrismaticJoint;
        armL0Found = armL0Articulation != null && armL0Articulation.jointType == ArticulationJointType.PrismaticJoint;

        if (!armL3Found || !armL2Found || !armL1Found || !armL0Found)
        {
            Debug.LogError("XsyncLinkArm: Missing or invalid ArticulationBody components!");
            Debug.LogError($"  L3: {(armL3Found ? "yes" : "no")}, L2: {(armL2Found ? "yes" : "no")}, L1: {(armL1Found ? "yes" : "no")}, L0: {(armL0Found ? "yes" : "no")}");
            Debug.LogError("XsyncLinkArm: Assign all 4 ArticulationBody components in Inspector or enable auto-find!");
        }
    }

    /// <summary>
    /// Initialize ArticulationBody drive parameters for all segments
    /// </summary>
    void InitializeArticulationDrives()
    {
        if (armL3Found) InitializeDrive(armL3Articulation, "L3");
        if (armL2Found) InitializeDrive(armL2Articulation, "L2");
        if (armL1Found) InitializeDrive(armL1Articulation, "L1");
        if (armL0Found) InitializeDrive(armL0Articulation, "L0");
    }

    /// <summary>
    /// Initialize drive parameters for a single ArticulationBody
    /// </summary>
    void InitializeDrive(ArticulationBody joint, string segmentName)
    {
        if (joint == null) return;

        ArticulationDrive drive = joint.xDrive;
        drive.stiffness = stiffness;
        drive.damping = damping;
        drive.forceLimit = forceLimit;
        joint.xDrive = drive;

        if (showDebugLogs)
        {
            Debug.Log($"XsyncLinkArm: Initialized {segmentName} drive - Stiffness: {stiffness}, Damping: {damping}, ForceLimit: {forceLimit}");
        }
    }

    /// <summary>
    /// Load current positions from ArticulationBodies
    /// </summary>
    void LoadCurrentPositions()
    {
        if (armL3Found) currentArmL3 = GetPrismaticPosition(armL3Articulation);
        if (armL2Found) currentArmL2 = GetPrismaticPosition(armL2Articulation);
        if (armL1Found) currentArmL1 = GetPrismaticPosition(armL1Articulation);
        if (armL0Found) currentArmL0 = GetPrismaticPosition(armL0Articulation);

        // Clamp to valid ranges
        currentArmL3 = Mathf.Clamp(currentArmL3, 0.0f, maxExtensionPerJoint);
        currentArmL2 = Mathf.Clamp(currentArmL2, 0.0f, maxExtensionPerJoint);
        currentArmL1 = Mathf.Clamp(currentArmL1, 0.0f, maxExtensionPerJoint);
        currentArmL0 = Mathf.Clamp(currentArmL0, 0.0f, maxExtensionPerJoint);

        // Calculate total extension from individual segments
        float totalExtension = currentArmL3 + currentArmL2 + currentArmL1 + currentArmL0;
        totalExtension = Mathf.Clamp(totalExtension, armMinPosition, armMaxPosition);

        // Initialize command position to current total extension
        commandTotalExtension = totalExtension;
        lastPublishedTotalExtension = totalExtension;

        // Initialize target positions
        targetArmL3 = currentArmL3;
        targetArmL2 = currentArmL2;
        targetArmL1 = currentArmL1;
        targetArmL0 = currentArmL0;

        if (showDebugLogs)
        {
            Debug.Log($"XsyncLinkArm: Loaded positions - L3: {currentArmL3:F3}m, L2: {currentArmL2:F3}m, L1: {currentArmL1:F3}m, L0: {currentArmL0:F3}m");
            Debug.Log($"XsyncLinkArm: Total extension: {totalExtension:F3}m");
        }
    }

    /// <summary>
    /// Get current position of a prismatic joint from ArticulationBody
    /// </summary>
    float GetPrismaticPosition(ArticulationBody joint)
    {
        if (joint == null) return 0.0f;
        return joint.jointPosition[0]; // Prismatic joints use jointPosition[0]
    }

    /// <summary>
    /// Update all ArticulationBody X Drive targets (Unity visualization)
    /// </summary>
    void UpdateArticulationBodies()
    {
        if (armL3Found) UpdateArticulationDrive(armL3Articulation, currentArmL3);
        if (armL2Found) UpdateArticulationDrive(armL2Articulation, currentArmL2);
        if (armL1Found) UpdateArticulationDrive(armL1Articulation, currentArmL1);
        if (armL0Found) UpdateArticulationDrive(armL0Articulation, currentArmL0);
    }

    /// <summary>
    /// Update X Drive target for a single ArticulationBody
    /// </summary>
    void UpdateArticulationDrive(ArticulationBody joint, float targetPosition)
    {
        if (joint == null) return;

        ArticulationDrive drive = joint.xDrive;
        drive.target = targetPosition; // Target in meters for prismatic joints
        joint.xDrive = drive;
    }

    /// <summary>
    /// Log status of all ArticulationBody components
    /// </summary>
    void LogArticulationStatus()
    {
        Debug.Log($"XsyncLinkArm: ArticulationBody Status:");
        Debug.Log($"  L3: {(armL3Found ? $"yes {armL3Articulation.transform.name}" : "no Not found")}");
        Debug.Log($"  L2: {(armL2Found ? $"yes {armL2Articulation.transform.name}" : "no Not found")}");
        Debug.Log($"  L1: {(armL1Found ? $"yes {armL1Articulation.transform.name}" : "no Not found")}");
        Debug.Log($"  L0: {(armL0Found ? $"yes {armL0Articulation.transform.name}" : "no Not found")}");
    }

    void InitializeROS2()
    {
        try
        {
            // Create ROS2 node
            ros2Node = ros2Unity.CreateNode("xsynclinkarm_node");

            // Create publisher for arm commands
            armPublisher = ros2Node.CreatePublisher<JointTrajectory>(armCommandTopic);

            if (showDebugLogs)
            {
                Debug.Log($"XsyncLinkArm: ROS2 initialized successfully!");
                Debug.Log($"XsyncLinkArm: Publisher created - {armCommandTopic}");
            }

            isInitialized = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"XsyncLinkArm: Failed to initialize ROS2: {e.Message}");
            Debug.LogError($"Stack trace: {e.StackTrace}");
        }
    }

    /// <summary>
    /// Send command matching the exact format of the working manual command
    /// This uses zero timestamp and empty arrays like XdriveArmLInk.cs
    /// CRITICAL: Stretch driver requires ALL 4 telescoping joints to be present
    /// </summary>
    void SendCommand(float totalExtension)
    {
        if (armPublisher == null || !isInitialized)
            return;

        // Clamp total extension to valid range
        totalExtension = Mathf.Clamp(totalExtension, armMinPosition, armMaxPosition);

        // Distribute total extension across 4 telescoping joints proportionally
        // Each joint can extend 0.0 to 0.13m, total arm can extend 0.0 to 0.52m (4 × 0.13m)
        // For synchronized telescoping: each joint extends by totalExtension/4, clamped to 0.13m max
        float extensionPerJoint = totalExtension / 4.0f;
        extensionPerJoint = Mathf.Clamp(extensionPerJoint, 0.0f, maxExtensionPerJoint);

        // Calculate positions for all 4 telescoping joints
        // Order: l3 (closest to lift/base), l2, l1, l0 (closest to wrist)
        double jointL3Position = extensionPerJoint;
        double jointL2Position = extensionPerJoint;
        double jointL1Position = extensionPerJoint;
        double jointL0Position = extensionPerJoint;

        var trajectory = new JointTrajectory();

        // Set header - matching manual command format (empty frame_id, zero stamp)
        trajectory.Header = new std_msgs.msg.Header
        {
            Stamp = new builtin_interfaces.msg.Time
            {
                Sec = 0,
                Nanosec = 0
            },
            Frame_id = "" // Empty frame_id like manual command (not "base_link")
        };

        // CRITICAL: Stretch driver requires ALL 4 telescoping joints to be present
        // Order must match: joint_arm_l3, joint_arm_l2, joint_arm_l1, joint_arm_l0
        trajectory.Joint_names = new string[] 
        { 
            JOINT_ARM_L3,  // Closest to lift/base
            JOINT_ARM_L2,
            JOINT_ARM_L1,
            JOINT_ARM_L0   // Closest to wrist
        };

        // Create trajectory point - matching exact manual command format
        var point = new JointTrajectoryPoint
        {
            Positions = new double[] 
            { 
                jointL3Position,
                jointL2Position,
                jointL1Position,
                jointL0Position
            },
            // Empty arrays to match manual command exactly (not arrays with 0.0 values!)
            Velocities = new double[0], // Empty array - KEY DIFFERENCE
            Accelerations = new double[0], // Empty array - KEY DIFFERENCE
            Effort = new double[0], // Empty array - KEY DIFFERENCE
            Time_from_start = new builtin_interfaces.msg.Duration
            {
                Sec = (int)duration, // duration controls real robot movement speed (lower = faster)
                Nanosec = 0 // Exactly 0 like manual command (not calculated)
            }
        };

        trajectory.Points = new JointTrajectoryPoint[] { point };

        // Publish trajectory
        armPublisher.Publish(trajectory);

        if (showDebugLogs)
        {
            Debug.Log($"XsyncLinkArm: Sent command - total extension: {totalExtension:F3}m");
            Debug.Log($"XsyncLinkArm: Joint positions - l3: {jointL3Position:F3}m, l2: {jointL2Position:F3}m, l1: {jointL1Position:F3}m, l0: {jointL0Position:F3}m");
            Debug.Log($"XsyncLinkArm: Duration: {duration:F1}s");
        }
    }

    void OnDestroy()
    {
        if (armPublisher != null)
        {
            armPublisher.Dispose();
        }
    }
}
