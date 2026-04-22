using UnityEngine;
using UnityEngine.InputSystem;
using ROS2;
using trajectory_msgs.msg;
using builtin_interfaces.msg;

/// <summary>
/// Perfect sync arm control: Matches real Stretch3 robot's sequential telescoping mechanism
/// Hybrid control: Joystick input → sends commands to robot, Real robot feedback → syncs Unity visualization
/// Uses EXACT same logic as real robot: L3→L2→L1→L0 sequential extension
/// - Joystick input → Sends commands to real robot via ROS2 using sequential telescoping
/// - Real robot position from StretchJointStateSub → Syncs Unity visualization
/// This ensures Unity always shows what the robot is actually doing (perfect sync)
/// </summary>
public class XZeroLink : MonoBehaviour
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

    [Tooltip("Maximum total arm extension (meters) - from URDF: 0.52m (4 × 0.13m)")]
    public float maxTotalExtension = 0.52f;

    [Header("Debug")]
    [Tooltip("Enable debug logging")]
    public bool showDebugLogs = true;

    [Tooltip("Log frequency (frames) - logs every N frames to reduce spam")]
    public int logFrequency = 60; // Log once per second at 60fps

    // ROS2 components
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<JointTrajectory> armPublisher;
    private bool isInitialized = false;

    // Internal state - positions from real robot (for Unity visualization)
    private float realRobotL3 = 0.0f;
    private float realRobotL2 = 0.0f;
    private float realRobotL1 = 0.0f;
    private float realRobotL0 = 0.0f;
    private float currentUnityL3 = 0.0f; // Current Unity visualization positions
    private float currentUnityL2 = 0.0f;
    private float currentUnityL1 = 0.0f;
    private float currentUnityL0 = 0.0f;
    private float commandTotalExtension = 0.0f; // Position to send to robot (from joystick)
    private float lastPublishedTotalExtension = 0.0f;
    private float lastPublishTime = 0.0f;

    // Component status flags
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
            Debug.LogWarning("XZeroLink: Right hand joystick InputActionReference not assigned! Joystick control will be disabled.");
        }

        // Auto-find StretchJointStateSub component if enabled
        if (autoFindJointStateSub && jointStateSubscriber == null)
        {
            jointStateSubscriber = FindObjectOfType<StretchJointStateSub>();
            if (jointStateSubscriber != null && showDebugLogs)
            {
                Debug.Log("XZeroLink: Auto-found StretchJointStateSub component");
            }
        }

        // Validate StretchJointStateSub is found
        if (jointStateSubscriber == null)
        {
            Debug.LogError("XZeroLink: StretchJointStateSub component not found! Assign in Inspector or enable auto-find.");
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
                Debug.LogError("XZeroLink: ROS2UnityComponent not found! Add ROS2UnityCore to scene.");
            }
        }

        if (showDebugLogs)
        {
            Debug.Log("XZeroLink: Initialized with sequential telescoping logic");
            Debug.Log($"XZeroLink: Arm limits: [{armMinPosition:F2}, {armMaxPosition:F2}] meters (total extension)");
            Debug.Log($"XZeroLink: Max extension per joint: {maxExtensionPerJoint:F3}m");
            Debug.Log($"XZeroLink: Max total extension: {maxTotalExtension:F3}m");
            Debug.Log($"XZeroLink: Using REAL robot sequential telescoping: L3→L2→L1→L0");
            Debug.Log($"XZeroLink: Hybrid control - Joystick → Robot commands, Real robot → Unity sync");
            if (jointStateSubFound)
            {
                Debug.Log($"XZeroLink: StretchJointStateSub found - will sync Unity from /stretch/joint_states");
            }
            else
            {
                Debug.LogError("XZeroLink: StretchJointStateSub not found!");
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
            SyncFromRealRobot();
        }
    }

    /// <summary>
    /// Handle joystick input and send commands to real robot
    /// Uses sequential telescoping logic (same as real robot)
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
                    Debug.Log($"XZeroLink: Sent command - total extension: {commandTotalExtension:F3}m");
                }
            }
        }
    }

    /// <summary>
    /// Sync Unity visualization with real robot position from StretchJointStateSub
    /// Uses EXACT positions from real robot - perfect sync
    /// </summary>
    void SyncFromRealRobot()
    {
        // Check if StretchJointStateSub has received any messages from robot
        if (!jointStateSubscriber.HasReceivedMessage())
        {
            // Wait for first message - don't sync until we have real robot data
            if (showDebugLogs && UnityEngine.Time.frameCount % 300 == 0) // Log every 5 seconds
            {
                Debug.LogWarning("XZeroLink: Waiting for joint_states messages from robot...");
            }
            return;
        }

        // Get real robot positions from StretchJointStateSub
        realRobotL3 = jointStateSubscriber.GetJointPosition(JOINT_ARM_L3);
        realRobotL2 = jointStateSubscriber.GetJointPosition(JOINT_ARM_L2);
        realRobotL1 = jointStateSubscriber.GetJointPosition(JOINT_ARM_L1);
        realRobotL0 = jointStateSubscriber.GetJointPosition(JOINT_ARM_L0);

        // Clamp to valid ranges (each joint: 0.0 to 0.13m)
        realRobotL3 = Mathf.Clamp(realRobotL3, 0.0f, maxExtensionPerJoint);
        realRobotL2 = Mathf.Clamp(realRobotL2, 0.0f, maxExtensionPerJoint);
        realRobotL1 = Mathf.Clamp(realRobotL1, 0.0f, maxExtensionPerJoint);
        realRobotL0 = Mathf.Clamp(realRobotL0, 0.0f, maxExtensionPerJoint);

        // Update Unity visualization to match real robot EXACTLY
        currentUnityL3 = realRobotL3;
        currentUnityL2 = realRobotL2;
        currentUnityL1 = realRobotL1;
        currentUnityL0 = realRobotL0;

        // Update ArticulationBody every frame for perfect synchronization
        UpdateArticulationBodies();

        // Debug logging (throttled)
        if (showDebugLogs && UnityEngine.Time.frameCount % logFrequency == 0)
        {
            float totalRealExtension = CalculateTotalExtension(realRobotL3, realRobotL2, realRobotL1, realRobotL0);
            float totalUnityExtension = CalculateTotalExtension(currentUnityL3, currentUnityL2, currentUnityL1, currentUnityL0);
            Debug.Log($"XZeroLink: SYNCED - Real Robot Total: {totalRealExtension:F3}m | Unity Total: {totalUnityExtension:F3}m | Command: {commandTotalExtension:F3}m");
            Debug.Log($"XZeroLink: Real Robot - L3: {realRobotL3:F3}m, L2: {realRobotL2:F3}m, L1: {realRobotL1:F3}m, L0: {realRobotL0:F3}m");
            Debug.Log($"XZeroLink: Unity - L3: {currentUnityL3:F3}m, L2: {currentUnityL2:F3}m, L1: {currentUnityL1:F3}m, L0: {currentUnityL0:F3}m");
        }
    }

    /// <summary>
    /// Calculate sequential telescoping positions from total extension
    /// EXACT same logic as real Stretch3 robot: L3→L2→L1→L0 sequential extension
    /// </summary>
    /// <param name="totalExtension">Total arm extension in meters (0.0 to 0.52m)</param>
    /// <param name="l3">Output: L3 position (0.0 to 0.13m)</param>
    /// <param name="l2">Output: L2 position (0.0 to 0.13m)</param>
    /// <param name="l1">Output: L1 position (0.0 to 0.13m)</param>
    /// <param name="l0">Output: L0 position (0.0 to 0.13m)</param>
    public void CalculateSequentialTelescoping(float totalExtension, out float l3, out float l2, out float l1, out float l0)
    {
        // Clamp total extension to valid range
        totalExtension = Mathf.Clamp(totalExtension, 0.0f, maxTotalExtension);

        // Sequential telescoping: L3 extends first, then L2, then L1, then L0
        // Each link extends to maximum (0.13m) before next link starts

        // Step 1: L3 extends to maximum
        l3 = Mathf.Min(totalExtension, maxExtensionPerJoint);
        float remaining = Mathf.Max(0.0f, totalExtension - maxExtensionPerJoint);

        // Step 2: L2 extends to maximum
        l2 = Mathf.Min(remaining, maxExtensionPerJoint);
        remaining = Mathf.Max(0.0f, remaining - maxExtensionPerJoint);

        // Step 3: L1 extends to maximum
        l1 = Mathf.Min(remaining, maxExtensionPerJoint);
        remaining = Mathf.Max(0.0f, remaining - maxExtensionPerJoint);

        // Step 4: L0 extends with remainder
        l0 = remaining; // Will be ≤ maxExtensionPerJoint

        // Verify total matches
        float calculatedTotal = l3 + l2 + l1 + l0;
        if (Mathf.Abs(calculatedTotal - totalExtension) > 0.001f && showDebugLogs)
        {
            Debug.LogWarning($"XZeroLink: Calculation mismatch! Total: {totalExtension:F3}m, Calculated: {calculatedTotal:F3}m");
        }
    }

    /// <summary>
    /// Calculate total extension from individual joint positions
    /// </summary>
    public float CalculateTotalExtension(float l3, float l2, float l1, float l0)
    {
        return l3 + l2 + l1 + l0;
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
                    Debug.Log($"XZeroLink: Auto-found armL3Articulation on {l3Obj.name}");
            }
        }

        if (armL2Articulation == null)
        {
            GameObject l2Obj = GameObject.Find("link_arm_l2");
            if (l2Obj != null)
            {
                armL2Articulation = l2Obj.GetComponent<ArticulationBody>();
                if (armL2Articulation != null && showDebugLogs)
                    Debug.Log($"XZeroLink: Auto-found armL2Articulation on {l2Obj.name}");
            }
        }

        if (armL1Articulation == null)
        {
            GameObject l1Obj = GameObject.Find("link_arm_l1");
            if (l1Obj != null)
            {
                armL1Articulation = l1Obj.GetComponent<ArticulationBody>();
                if (armL1Articulation != null && showDebugLogs)
                    Debug.Log($"XZeroLink: Auto-found armL1Articulation on {l1Obj.name}");
            }
        }

        if (armL0Articulation == null)
        {
            GameObject l0Obj = GameObject.Find("link_arm_l0");
            if (l0Obj != null)
            {
                armL0Articulation = l0Obj.GetComponent<ArticulationBody>();
                if (armL0Articulation != null && showDebugLogs)
                    Debug.Log($"XZeroLink: Auto-found armL0Articulation on {l0Obj.name}");
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
            Debug.LogError("XZeroLink: Missing or invalid ArticulationBody components!");
            Debug.LogError($"  L3: {(armL3Found ? "yes" : "no")}, L2: {(armL2Found ? "yes" : "no")}, L1: {(armL1Found ? "yes" : "no")}, L0: {(armL0Found ? "yes" : "no")}");
            Debug.LogError("XZeroLink: Assign all 4 ArticulationBody components in Inspector or enable auto-find!");
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
            Debug.Log($"XZeroLink: Initialized {segmentName} drive - Stiffness: {stiffness}, Damping: {damping}, ForceLimit: {forceLimit}");
        }
    }

    /// <summary>
    /// Load current positions from ArticulationBodies
    /// </summary>
    void LoadCurrentPositions()
    {
        if (armL3Found) currentUnityL3 = GetPrismaticPosition(armL3Articulation);
        if (armL2Found) currentUnityL2 = GetPrismaticPosition(armL2Articulation);
        if (armL1Found) currentUnityL1 = GetPrismaticPosition(armL1Articulation);
        if (armL0Found) currentUnityL0 = GetPrismaticPosition(armL0Articulation);

        // Clamp to valid ranges
        currentUnityL3 = Mathf.Clamp(currentUnityL3, 0.0f, maxExtensionPerJoint);
        currentUnityL2 = Mathf.Clamp(currentUnityL2, 0.0f, maxExtensionPerJoint);
        currentUnityL1 = Mathf.Clamp(currentUnityL1, 0.0f, maxExtensionPerJoint);
        currentUnityL0 = Mathf.Clamp(currentUnityL0, 0.0f, maxExtensionPerJoint);

        // Calculate total extension from individual segments
        float totalExtension = CalculateTotalExtension(currentUnityL3, currentUnityL2, currentUnityL1, currentUnityL0);
        totalExtension = Mathf.Clamp(totalExtension, armMinPosition, armMaxPosition);

        // Initialize command position to current total extension
        commandTotalExtension = totalExtension;
        lastPublishedTotalExtension = totalExtension;

        if (showDebugLogs)
        {
            Debug.Log($"XZeroLink: Loaded positions - L3: {currentUnityL3:F3}m, L2: {currentUnityL2:F3}m, L1: {currentUnityL1:F3}m, L0: {currentUnityL0:F3}m");
            Debug.Log($"XZeroLink: Total extension: {totalExtension:F3}m");
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
        if (armL3Found) UpdateArticulationDrive(armL3Articulation, currentUnityL3);
        if (armL2Found) UpdateArticulationDrive(armL2Articulation, currentUnityL2);
        if (armL1Found) UpdateArticulationDrive(armL1Articulation, currentUnityL1);
        if (armL0Found) UpdateArticulationDrive(armL0Articulation, currentUnityL0);
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
        Debug.Log($"XZeroLink: ArticulationBody Status:");
        Debug.Log($"  L3: {(armL3Found ? $"yes {armL3Articulation.transform.name}" : "no Not found")}");
        Debug.Log($"  L2: {(armL2Found ? $"yes {armL2Articulation.transform.name}" : "no Not found")}");
        Debug.Log($"  L1: {(armL1Found ? $"yes {armL1Articulation.transform.name}" : "no Not found")}");
        Debug.Log($"  L0: {(armL0Found ? $"yes {armL0Articulation.transform.name}" : "no Not found")}");
    }

    /// <summary>
    /// Verify drive parameters are set correctly (diagnostic method)
    /// Call this from Inspector context menu or another script to check if values are applied
    /// </summary>
    [ContextMenu("Verify Drive Parameters")]
    public void VerifyDriveParameters()
    {
        Debug.Log("XZeroLink: ===== DRIVE PARAMETER VERIFICATION =====");
        
        if (armL3Found && armL3Articulation != null)
        {
            ArticulationDrive drive = armL3Articulation.xDrive;
            Debug.Log($"L3 Drive - Stiffness: {drive.stiffness} (expected: {stiffness}), Damping: {drive.damping} (expected: {damping}), Target: {drive.target}, DriveType: {drive.driveType}");
            if (drive.stiffness == 0)
                Debug.LogError("L3:  STIFFNESS IS ZERO! Position control will not work!");
        }
        
        if (armL2Found && armL2Articulation != null)
        {
            ArticulationDrive drive = armL2Articulation.xDrive;
            Debug.Log($"L2 Drive - Stiffness: {drive.stiffness} (expected: {stiffness}), Damping: {drive.damping} (expected: {damping}), Target: {drive.target}, DriveType: {drive.driveType}");
            if (drive.stiffness == 0)
                Debug.LogError("L2:  STIFFNESS IS ZERO! Position control will not work!");
        }
        
        if (armL1Found && armL1Articulation != null)
        {
            ArticulationDrive drive = armL1Articulation.xDrive;
            Debug.Log($"L1 Drive - Stiffness: {drive.stiffness} (expected: {stiffness}), Damping: {drive.damping} (expected: {damping}), Target: {drive.target}, DriveType: {drive.driveType}");
            if (drive.stiffness == 0)
                Debug.LogError("L1:  STIFFNESS IS ZERO! Position control will not work!");
        }
        
        if (armL0Found && armL0Articulation != null)
        {
            ArticulationDrive drive = armL0Articulation.xDrive;
            Debug.Log($"L0 Drive - Stiffness: {drive.stiffness} (expected: {stiffness}), Damping: {drive.damping} (expected: {damping}), Target: {drive.target}, DriveType: {drive.driveType}");
            if (drive.stiffness == 0)
                Debug.LogError("L0:  STIFFNESS IS ZERO! Position control will not work!");
        }
        
        Debug.Log("XZeroLink: ===== VERIFICATION COMPLETE =====");
    }

    /// <summary>
    /// Force re-initialization of all drive parameters
    /// Use this if values aren't being set correctly at startup
    /// </summary>
    [ContextMenu("Re-Initialize Drives")]
    public void ForceReinitializeDrives()
    {
        if (showDebugLogs)
            Debug.Log("XZeroLink: Force re-initializing all drive parameters...");
        
        InitializeArticulationDrives();
        
        if (showDebugLogs)
            Debug.Log("XZeroLink: Drive parameters re-initialized! Use 'Verify Drive Parameters' to check.");
        
        // Auto-verify after re-initialization
        VerifyDriveParameters();
    }

    /// <summary>
    /// Get current real robot positions (for external access)
    /// </summary>
    public void GetRealRobotPositions(out float l3, out float l2, out float l1, out float l0)
    {
        l3 = realRobotL3;
        l2 = realRobotL2;
        l1 = realRobotL1;
        l0 = realRobotL0;
    }

    /// <summary>
    /// Get current Unity visualization positions (for external access)
    /// </summary>
    public void GetUnityPositions(out float l3, out float l2, out float l1, out float l0)
    {
        l3 = currentUnityL3;
        l2 = currentUnityL2;
        l1 = currentUnityL1;
        l0 = currentUnityL0;
    }

    /// <summary>
    /// Get total extension from real robot
    /// </summary>
    public float GetRealRobotTotalExtension()
    {
        return CalculateTotalExtension(realRobotL3, realRobotL2, realRobotL1, realRobotL0);
    }

    /// <summary>
    /// Get total extension from Unity visualization
    /// </summary>
    public float GetUnityTotalExtension()
    {
        return CalculateTotalExtension(currentUnityL3, currentUnityL2, currentUnityL1, currentUnityL0);
    }

    void InitializeROS2()
    {
        try
        {
            // Create ROS2 node
            ros2Node = ros2Unity.CreateNode("xzerolink_node");

            // Create publisher for arm commands
            armPublisher = ros2Node.CreatePublisher<JointTrajectory>(armCommandTopic);

            if (showDebugLogs)
            {
                Debug.Log($"XZeroLink: ROS2 initialized successfully!");
                Debug.Log($"XZeroLink: Publisher created - {armCommandTopic}");
            }

            isInitialized = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"XZeroLink: Failed to initialize ROS2: {e.Message}");
            Debug.LogError($"Stack trace: {e.StackTrace}");
        }
    }

    /// <summary>
    /// Send command using sequential telescoping logic (EXACT same as real robot)
    /// Converts total extension to individual joint positions using sequential logic
    /// </summary>
    void SendCommand(float totalExtension)
    {
        if (armPublisher == null || !isInitialized)
            return;

        // Clamp total extension to valid range
        totalExtension = Mathf.Clamp(totalExtension, armMinPosition, armMaxPosition);

        // Calculate sequential telescoping positions (EXACT real robot logic)
        float l3, l2, l1, l0;
        CalculateSequentialTelescoping(totalExtension, out l3, out l2, out l1, out l0);

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
                l3,  // Sequential telescoping positions
                l2,
                l1,
                l0
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
            Debug.Log($"XZeroLink: Sent command - total extension: {totalExtension:F3}m");
            Debug.Log($"XZeroLink: Sequential positions - l3: {l3:F3}m, l2: {l2:F3}m, l1: {l1:F3}m, l0: {l0:F3}m");
            Debug.Log($"XZeroLink: Duration: {duration:F1}s");
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
