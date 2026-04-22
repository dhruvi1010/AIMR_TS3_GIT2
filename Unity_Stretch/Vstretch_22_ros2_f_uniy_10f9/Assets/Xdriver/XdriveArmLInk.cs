using UnityEngine;
using UnityEngine.InputSystem;
using ROS2;
using trajectory_msgs.msg;
using builtin_interfaces.msg;

/// <summary>
/// Control all 4 telescoping arm segments using ArticulationBody X Drive (most accurate)
/// Uses right hand Meta joystick X-axis (left/right) to extend/retract arm
/// Publishes JointTrajectory to real Stretch3 robot via ROS2
/// Matches real robot movement exactly - all 4 segments move together proportionally
/// </summary>
public class XdriveArmLInk : MonoBehaviour
{
    [Header("ROS2 Settings")]
    [Tooltip("Topic to publish arm commands (action goal topic)")]
    public string armCommandTopic = "/stretch_controller/follow_joint_trajectory/goal";

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
    [Tooltip("Automatically find ArticulationBody components by GameObject name")]
    public bool autoFindArticulations = true;

    [Header("Joystick Input")]
    [Tooltip("Right hand joystick InputActionReference for arm control")]
    public InputActionReference rightHandJoystick;

    [Header("Movement Settings")]
    [Tooltip("Movement speed (meters per second) - Higher = faster Unity responsiveness")]
    public float armSpeed = 0.2f; // m/s

    [Header("Arm Limits (meters)")]
    [Tooltip("Minimum arm position (meters) - from URDF: 0.0 (fully retracted)")]
    public float armMinPosition = 0.0f;

    [Tooltip("Maximum arm position (meters) - from URDF: 0.52 (fully extended)")]
    public float armMaxPosition = 0.52f;

    [Tooltip("Maximum extension per joint (meters) - from URDF: 0.13m per telescoping segment")]
    public float maxExtensionPerJoint = 0.13f;

    [Header("ArticulationBody Drive Settings")]
    [Tooltip("Stiffness for ArticulationBody drive (higher = stiffer, more responsive)")]
    public float stiffness = 10000f;

    [Tooltip("Damping for ArticulationBody drive (higher = more damped, smoother)")]
    public float damping = 1000f;

    [Tooltip("Force limit for ArticulationBody drive")]
    public float forceLimit = 100f;

    [Header("Trajectory Settings")]
    [Tooltip("Trajectory duration (seconds) - Lower = faster robot movement")]
    public float duration = 0.5f;

    [Header("Publishing Settings")]
    [Tooltip("Minimum time between publishes (seconds) - Match gamepad teleop: 0.05s = 20 Hz")]
    public float publishInterval = 0.05f;

    [Tooltip("Minimum position change to trigger publish (meters)")]
    public float minPositionChange = 0.002f;

    [Header("Debug")]
    public bool showDebugLogs = true;

    // ROS2 components
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<JointTrajectory> armPublisher;
    private bool isInitialized = false;

    // Internal state - target positions for all 4 segments
    private float currentArmPosition = 0.0f; // Total arm extension (0.0 = retracted, 0.52 = extended)
    private float targetArmL3 = 0.0f; // Individual segment targets
    private float targetArmL2 = 0.0f;
    private float targetArmL1 = 0.0f;
    private float targetArmL0 = 0.0f;
    private float lastPublishedPosition = 0.0f;
    private float lastPublishTime = 0.0f;

    // Track which articulations are found
    private bool armL3Found = false;
    private bool armL2Found = false;
    private bool armL1Found = false;
    private bool armL0Found = false;

    void Start()
    {
        if (rightHandJoystick == null)
        {
            Debug.LogError("XdriveArmLInk: Right hand joystick InputActionReference not assigned!");
            return;
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

        lastPublishedPosition = currentArmPosition;

        // Initialize ROS2
        ros2Unity = GetComponent<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            ros2Unity = FindObjectOfType<ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                Debug.LogError("XdriveArmLInk: ROS2UnityComponent not found! Add ROS2UnityCore to scene.");
                return;
            }
        }

        if (showDebugLogs)
        {
            Debug.Log("XdriveArmLInk: Initialized");
            Debug.Log($"XdriveArmLInk: Arm limits: [{armMinPosition:F2}, {armMaxPosition:F2}] meters (total extension)");
            Debug.Log($"XdriveArmLInk: Max extension per joint: {maxExtensionPerJoint:F3}m (4 telescoping segments)");
            Debug.Log($"XdriveArmLInk: Using ArticulationBody X Drive (most accurate visualization)");
            Debug.Log($"XdriveArmLInk: Will send all 4 telescoping joints: joint_arm_l3, joint_arm_l2, joint_arm_l1, joint_arm_l0");
            LogArticulationStatus();
        }
    }

    void Update()
    {
        if (rightHandJoystick == null)
            return;

        // Try to initialize ROS2 if not already initialized
        if (!isInitialized)
        {
            if (ros2Unity != null && ros2Unity.Ok())
            {
                InitializeROS2();
            }
        }

        if (!isInitialized || armPublisher == null)
            return;

        // Get joystick input (x-axis for left/right movement = expand/contract)
        Vector2 joystickInput = rightHandJoystick.action.ReadValue<Vector2>();
        float horizontalInput = joystickInput.x; // x-axis is left/right on joystick

        // Update target position based on input
        // Right (positive x) = extend arm, Left (negative x) = retract arm
        float previousPosition = currentArmPosition;
        currentArmPosition += horizontalInput * armSpeed * UnityEngine.Time.deltaTime;
        currentArmPosition = Mathf.Clamp(currentArmPosition, armMinPosition, armMaxPosition);

        // Distribute total extension across all 4 segments proportionally
        float extensionPerJoint = currentArmPosition / 4.0f;
        extensionPerJoint = Mathf.Clamp(extensionPerJoint, 0.0f, maxExtensionPerJoint);

        // Update target positions for all 4 segments
        targetArmL3 = extensionPerJoint;
        targetArmL2 = extensionPerJoint;
        targetArmL1 = extensionPerJoint;
        targetArmL0 = extensionPerJoint;

        // Update ArticulationBody X Drive targets (Unity visualization)
        UpdateArticulationBodies();

        // Publish to robot with throttling
        if (Mathf.Abs(horizontalInput) > 0.01f)
        {
            float timeSinceLastPublish = UnityEngine.Time.time - lastPublishTime;
            float positionChange = Mathf.Abs(currentArmPosition - lastPublishedPosition);

            // Rate limiting: Only publish if enough time passed AND position changed significantly
            if (timeSinceLastPublish >= publishInterval && positionChange >= minPositionChange)
            {
                SendCommand(currentArmPosition);
                lastPublishedPosition = currentArmPosition;
                lastPublishTime = UnityEngine.Time.time;
            }
        }

        if (showDebugLogs && Mathf.Abs(horizontalInput) > 0.01f && UnityEngine.Time.frameCount % 60 == 0)
        {
            Debug.Log($"XdriveArmLInk: Joystick Input: {horizontalInput:F2} | Total Extension: {currentArmPosition:F3}m | Per Joint: {extensionPerJoint:F3}m");
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
                    Debug.Log($"XdriveArmLInk: Auto-found armL3Articulation on {l3Obj.name}");
            }
        }

        if (armL2Articulation == null)
        {
            GameObject l2Obj = GameObject.Find("link_arm_l2");
            if (l2Obj != null)
            {
                armL2Articulation = l2Obj.GetComponent<ArticulationBody>();
                if (armL2Articulation != null && showDebugLogs)
                    Debug.Log($"XdriveArmLInk: Auto-found armL2Articulation on {l2Obj.name}");
            }
        }

        if (armL1Articulation == null)
        {
            GameObject l1Obj = GameObject.Find("link_arm_l1");
            if (l1Obj != null)
            {
                armL1Articulation = l1Obj.GetComponent<ArticulationBody>();
                if (armL1Articulation != null && showDebugLogs)
                    Debug.Log($"XdriveArmLInk: Auto-found armL1Articulation on {l1Obj.name}");
            }
        }

        if (armL0Articulation == null)
        {
            GameObject l0Obj = GameObject.Find("link_arm_l0");
            if (l0Obj != null)
            {
                armL0Articulation = l0Obj.GetComponent<ArticulationBody>();
                if (armL0Articulation != null && showDebugLogs)
                    Debug.Log($"XdriveArmLInk: Auto-found armL0Articulation on {l0Obj.name}");
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
            Debug.LogError("XdriveArmLInk: Missing or invalid ArticulationBody components!");
            Debug.LogError($"  L3: {(armL3Found ? "yes" : "no")}, L2: {(armL2Found ? "yes" : "NO")}, L1: {(armL1Found ? "yes" : "NO")}, L0: {(armL0Found ? "yes" : "NO")}");
            Debug.LogError("XdriveArmLInk: Assign all 4 ArticulationBody components in Inspector or enable auto-find!");
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
            Debug.Log($"XdriveArmLInk: Initialized {segmentName} drive - Stiffness: {stiffness}, Damping: {damping}, ForceLimit: {forceLimit}");
        }
    }

    /// <summary>
    /// Load current positions from ArticulationBodies
    /// </summary>
    void LoadCurrentPositions()
    {
        if (armL3Found) targetArmL3 = GetPrismaticPosition(armL3Articulation);
        if (armL2Found) targetArmL2 = GetPrismaticPosition(armL2Articulation);
        if (armL1Found) targetArmL1 = GetPrismaticPosition(armL1Articulation);
        if (armL0Found) targetArmL0 = GetPrismaticPosition(armL0Articulation);

        // Calculate total extension from individual segments
        currentArmPosition = targetArmL3 + targetArmL2 + targetArmL1 + targetArmL0;
        currentArmPosition = Mathf.Clamp(currentArmPosition, armMinPosition, armMaxPosition);

        if (showDebugLogs)
        {
            Debug.Log($"XdriveArmLInk: Loaded positions - L3: {targetArmL3:F3}m, L2: {targetArmL2:F3}m, L1: {targetArmL1:F3}m, L0: {targetArmL0:F3}m");
            Debug.Log($"XdriveArmLInk: Total extension: {currentArmPosition:F3}m");
        }
    }

    /// Get current position of a prismatic joint from ArticulationBody

    float GetPrismaticPosition(ArticulationBody joint)
    {
        if (joint == null) return 0.0f;
        return joint.jointPosition[0]; // Prismatic joints use jointPosition[0]
    }


    /// Update all ArticulationBody X Drive targets (Unity visualization)

    void UpdateArticulationBodies()
    {
        if (armL3Found) UpdateArticulationDrive(armL3Articulation, targetArmL3);
        if (armL2Found) UpdateArticulationDrive(armL2Articulation, targetArmL2);
        if (armL1Found) UpdateArticulationDrive(armL1Articulation, targetArmL1);
        if (armL0Found) UpdateArticulationDrive(armL0Articulation, targetArmL0);
    }


    /// Update X Drive target for a single ArticulationBody

    void UpdateArticulationDrive(ArticulationBody joint, float targetPosition)
    {
        if (joint == null) return;

        ArticulationDrive drive = joint.xDrive;
        drive.target = targetPosition; // Target in meters for prismatic joints
        joint.xDrive = drive;
    }


    /// Log status of all ArticulationBody components

    void LogArticulationStatus()
    {
        Debug.Log($"XdriveArmLInk: ArticulationBody Status:");
        Debug.Log($"  L3: {(armL3Found ? $"yes {armL3Articulation.transform.name}" : "  Not found")}");
        Debug.Log($"  L2: {(armL2Found ? $"yes {armL2Articulation.transform.name}" : " Not found")}");
        Debug.Log($"  L1: {(armL1Found ? $"yes {armL1Articulation.transform.name}" : " Not found")}");
        Debug.Log($"  L0: {(armL0Found ? $"yes {armL0Articulation.transform.name}" : " Not found")}");
    }

    void InitializeROS2()
    {
        try
        {
            // Create ROS2 node
            ros2Node = ros2Unity.CreateNode("xdrivearmlink_node");

            // Create publisher for arm commands
            armPublisher = ros2Node.CreatePublisher<JointTrajectory>(armCommandTopic);

            if (showDebugLogs)
            {
                Debug.Log($"XdriveArmLInk: ROS2 initialized successfully!");
                Debug.Log($"XdriveArmLInk: Publisher created - {armCommandTopic}");
            }

            isInitialized = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"XdriveArmLInk: Failed to initialize ROS2: {e.Message}");
            Debug.LogError($"Stack trace: {e.StackTrace}");
        }
    }

    /// <summary>
    /// Send command matching the exact format of the working manual command
    /// This uses zero timestamp and empty arrays like fixmovelift.cs
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
        // THIS IS THE KEY - uses zero timestamp and empty frame_id like fixmovelift.cs
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
            "joint_arm_l3",  // Closest to lift/base
            "joint_arm_l2",
            "joint_arm_l1",
            "joint_arm_l0"   // Closest to wrist
        };

        // Create trajectory point - matching exact manual command format
        // Manual command shows: positions: [0.3], time_from_start: sec: 2, nanosec: 0
        // velocities, accelerations, effort are empty arrays in manual command
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
            Debug.Log($"XdriveArmLInk: Sent command - total extension: {totalExtension:F3}m");
            Debug.Log($"XdriveArmLInk: Joint positions - l3: {jointL3Position:F3}m, l2: {jointL2Position:F3}m, l1: {jointL1Position:F3}m, l0: {jointL0Position:F3}m");
            Debug.Log($"XdriveArmLInk: Duration: {duration:F1}s");
        }
    }
}
