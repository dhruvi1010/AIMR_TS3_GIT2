using UnityEngine;
using UnityEngine.InputSystem;
using ROS2;
using trajectory_msgs.msg;
using builtin_interfaces.msg;

/// <summary>
/// Control link arm expand/contract using right hand Meta joystick X-axis (left/right)
/// Publishes JointTrajectory to real Stretch3 robot via ROS2
/// Similar to fixmovelift.cs but for arm extension control
/// </summary>
public class LinkArm : MonoBehaviour
{
    [Header("ROS2 Settings")]
    [Tooltip("Topic to publish arm commands (action goal topic)")]
    public string armCommandTopic = "/stretch_controller/follow_joint_trajectory/goal";

    [Header("GameObject Reference - All 4 Telescoping Segments")]
    [Tooltip("link_arm_l3 Transform (closest to lift/base) - REQUIRED for synchronized movement")]
    public Transform ArmLinkL3;
    
    [Tooltip("link_arm_l2 Transform - REQUIRED for synchronized movement")]
    public Transform ArmLinkL2;
    
    [Tooltip("link_arm_l1 Transform - REQUIRED for synchronized movement")]
    public Transform ArmLinkL1;
    
    [Tooltip("link_arm_l0 Transform (closest to wrist) - REQUIRED for synchronized movement")]
    public Transform ArmLinkL0;
    
    [Header("Legacy Support (Optional)")]
    [Tooltip("Legacy: Single Transform reference (will use ArmLinkL0 if both set)")]
    public Transform ArmLink;
    
    [Tooltip("Legacy: Single GameObject reference (will use ArmLinkL0 if both set)")]
    public GameObject Joint_Arm;

    [Header("Unity Visualization Settings")]
    [Tooltip("Movement axis in Unity: 0=X, 1=Y, 2=Z. Or use -1 for transform.forward direction (recommended)")]
    public int movementAxis = 1; // -1 = use transform.forward, 0=X, 1=Y, 2=Z

    [Header("Joystick Input")]
    [Tooltip("Right hand joystick InputActionReference for arm control")]
    public InputActionReference rightHandJoystick;

    [Header("Movement Settings")]
    [Tooltip("Movement speed (meters per second) - Higher = faster Unity responsiveness")]
    public float armSpeed = 0.1f; // within Unity m/s - Increased for faster response (was 0.1, gamepad teleop equivalent ~0.2-0.3)

    [Header("Arm Limits (meters)")]
    [Tooltip("Minimum arm position (meters) - from URDF: 0.0 (fully retracted)")]
    public float armMinPosition = 0.0f;

    [Tooltip("Maximum arm position (meters) - from URDF: 0.52 (fully extended)")]
    public float armMaxPosition = 0.52f;

    [Tooltip("Maximum extension per joint (meters) - from URDF: 0.13m per telescoping segment")]
    public float maxExtensionPerJoint = 0.13f;

    [Header("Trajectory Settings")]
    [Tooltip("Trajectory duration (seconds) - Lower = faster robot movement. Minimum recommended: 0.3s")]
    public float duration = 0.5f; // REQUIRED: Reduced from 1.0s for faster execution (0.3-0.5s is fast, 0.5s is safe)

    [Header("Publishing Settings")]
    [Tooltip("Minimum time between publishes (seconds) - Match gamepad teleop: 0.05s = 20 Hz")]
    public float publishInterval = 0.05f; // Rate limiting: Reduced from 0.2s to 0.05s (20 Hz) to match gamepad teleop speed

    [Tooltip("Minimum position change to trigger publish (meters) - Lower = more sensitive")]
    public float minPositionChange = 0.002f; // Dead zone: Reduced from 0.01m to 0.002m for faster response

    [Header("Debug")]
    public bool showDebugLogs = true;

    // ROS2 components
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<JointTrajectory> armPublisher;
    private bool isInitialized = false;

    // Internal state
    private float currentArmPosition = 0.0f; // Current arm position (0.0 = retracted, 0.52 = extended)
    private float lastPublishedPosition = 0.0f;
    private float lastPublishTime = 0.0f;
    
    // Base positions for all 4 arm segments (when fully retracted)
    private Vector3 baseLocalPositionL3 = Vector3.zero;
    private Vector3 baseLocalPositionL2 = Vector3.zero;
    private Vector3 baseLocalPositionL1 = Vector3.zero;
    private Vector3 baseLocalPositionL0 = Vector3.zero;

    void Start()
    {
        if (rightHandJoystick == null)
        {
            Debug.LogError("LinkArm: Right hand joystick InputActionReference not assigned!");
            return;
        }

        // Initialize Unity visualization position
        // Store base positions for all 4 arm segments (when arm is at 0 extension)
        bool hasAllSegments = (ArmLinkL3 != null && ArmLinkL2 != null && ArmLinkL1 != null && ArmLinkL0 != null);
        
        if (hasAllSegments)
        {
            // Store base positions for all 4 segments
            baseLocalPositionL3 = ArmLinkL3.localPosition;
            baseLocalPositionL2 = ArmLinkL2.localPosition;
            baseLocalPositionL1 = ArmLinkL1.localPosition;
            baseLocalPositionL0 = ArmLinkL0.localPosition;
            currentArmPosition = 0.0f; // Start at fully retracted
            
            if (showDebugLogs)
            {
                Debug.Log("LinkArm: All 4 arm segments assigned - synchronized movement enabled");
            }
        }
        else if (ArmLink != null)
        {
            // Legacy: Single link support (only moves link_arm_l0)
            baseLocalPositionL0 = ArmLink.localPosition;
            currentArmPosition = 0.0f;
            
            if (showDebugLogs)
            {
                Debug.LogWarning("LinkArm: Only single link assigned. Assign all 4 segments (L3, L2, L1, L0) for full synchronized movement!");
            }
        }
        else if (Joint_Arm != null)
        {
            // Legacy: GameObject reference
            baseLocalPositionL0 = Joint_Arm.transform.localPosition;
            currentArmPosition = 0.0f;
            
            if (showDebugLogs)
            {
                Debug.LogWarning("LinkArm: Only single GameObject assigned. Assign all 4 segments (L3, L2, L1, L0) for full synchronized movement!");
            }
        }
        else
        {
            Debug.LogWarning("LinkArm: No arm link GameObjects assigned! Visualization will not work.");
        }

        lastPublishedPosition = currentArmPosition;

        // Initialize ROS2
        ros2Unity = GetComponent<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            ros2Unity = FindObjectOfType<ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                Debug.LogError("LinkArm: ROS2UnityComponent not found! Add ROS2UnityCore to scene.");
                return;
            }
        }

        if (showDebugLogs)
        {
            Debug.Log("LinkArm: Initialized");
            Debug.Log($"LinkArm: Arm limits: [{armMinPosition:F2}, {armMaxPosition:F2}] meters (total extension)");
            Debug.Log($"LinkArm: Max extension per joint: {maxExtensionPerJoint:F3}m (4 telescoping segments)");
            Debug.Log($"LinkArm: Using working trajectory format (zero timestamp, empty arrays)");
            Debug.Log($"LinkArm: Will send all 4 telescoping joints: joint_arm_l3, joint_arm_l2, joint_arm_l1, joint_arm_l0");
            
            // Log movement axis setting
            if (movementAxis == -1)
                Debug.Log($"LinkArm: Movement axis: Transform.forward (auto-detected from GameObject rotation)");
            else if (movementAxis == 0)
                Debug.Log($"LinkArm: Movement axis: X (right)");
            else if (movementAxis == 1)
                Debug.Log($"LinkArm: Movement axis: Y (up)");
            else
                Debug.Log($"LinkArm: Movement axis: Z (forward)");
            
            // Log which segments are assigned
            bool hasAll = (ArmLinkL3 != null && ArmLinkL2 != null && ArmLinkL1 != null && ArmLinkL0 != null);
            if (hasAll)
            {
                Debug.Log($"LinkArm:  All 4 segments assigned - synchronized movement enabled");
                Debug.Log($"LinkArm:   L3: {ArmLinkL3.name}, L2: {ArmLinkL2.name}, L1: {ArmLinkL1.name}, L0: {ArmLinkL0.name}");
            }
            else
            {
                Debug.LogWarning($"LinkArm:  Not all segments assigned! Assign all 4 (L3, L2, L1, L0) for full visualization.");
                Debug.LogWarning($"LinkArm:   L3: {(ArmLinkL3 != null ? ArmLinkL3.name : "NULL")}, L2: {(ArmLinkL2 != null ? ArmLinkL2.name : "NULL")}, L1: {(ArmLinkL1 != null ? ArmLinkL1.name : "NULL")}, L0: {(ArmLinkL0 != null ? ArmLinkL0.name : "NULL")}");
            }
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
        currentArmPosition += horizontalInput * armSpeed * UnityEngine.Time.deltaTime; // armSpeed controls Unity responsiveness
        currentArmPosition = Mathf.Clamp(currentArmPosition, armMinPosition, armMaxPosition);

        // Update Unity visualization - move ALL 4 segments proportionally
        // Each segment extends by totalExtension/4, up to 0.13m max per segment
        float extensionPerJoint = currentArmPosition / 4.0f;
        extensionPerJoint = Mathf.Clamp(extensionPerJoint, 0.0f, maxExtensionPerJoint);
        
        // Check if all 4 segments are assigned
        bool hasAllSegments = (ArmLinkL3 != null && ArmLinkL2 != null && ArmLinkL1 != null && ArmLinkL0 != null);
        
        if (hasAllSegments)
        {
            // Move all 4 segments together (synchronized telescoping)
            ArmLinkL3.localPosition = CalculateNewPosition(extensionPerJoint, ArmLinkL3, baseLocalPositionL3);
            ArmLinkL2.localPosition = CalculateNewPosition(extensionPerJoint, ArmLinkL2, baseLocalPositionL2);
            ArmLinkL1.localPosition = CalculateNewPosition(extensionPerJoint, ArmLinkL1, baseLocalPositionL1);
            ArmLinkL0.localPosition = CalculateNewPosition(extensionPerJoint, ArmLinkL0, baseLocalPositionL0);
        }
        else if (ArmLink != null)
        {
            // Legacy: Only move single link (link_arm_l0)
            ArmLink.localPosition = CalculateNewPosition(currentArmPosition, ArmLink, baseLocalPositionL0);
        }
        else if (Joint_Arm != null)
        {
            // Legacy: GameObject reference
            Joint_Arm.transform.localPosition = CalculateNewPosition(currentArmPosition, Joint_Arm.transform, baseLocalPositionL0);
        }

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

        if (showDebugLogs && Mathf.Abs(horizontalInput) > 0.01f)
        {
            Debug.Log($"LinkArm: Joystick Input: {horizontalInput:F2} | Position: {currentArmPosition:F3}m");
        }
    }

    void InitializeROS2()
    {
        try
        {
            // Create ROS2 node
            ros2Node = ros2Unity.CreateNode("linkarm_node");

            
            armPublisher = ros2Node.CreatePublisher<JointTrajectory>(armCommandTopic);

            if (showDebugLogs)
            {
                Debug.Log($"LinkArm: ROS2 initialized successfully!");
                Debug.Log($"LinkArm: Publisher created - {armCommandTopic}");
            }

            isInitialized = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"LinkArm: Failed to initialize ROS2: {e.Message}");
            Debug.LogError($"Stack trace: {e.StackTrace}");
        }
    }

    /// <summary>
    /// Get the movement direction vector based on movementAxis setting
    /// </summary>
    Vector3 GetMovementDirection(Transform transform)
    {
        if (movementAxis == -1)
        {
            // Use transform's local forward direction (respects GameObject rotation)
            // This is the most accurate - arm extends along its forward direction
            if (transform.parent != null)
            {
                // Get forward direction in parent's local space
                return transform.parent.InverseTransformDirection(transform.forward).normalized;
            }
            else
            {
                // No parent, use world forward converted to local
                return transform.forward.normalized;
            }
        }
        else if (movementAxis == 0)
            return Vector3.right; // X axis
        else if (movementAxis == 1)
            return Vector3.up; // Y axis
        else // movementAxis == 2
            return Vector3.forward; // Z axis
    }

    /// <summary>
    /// Calculate new local position based on arm extension
    /// </summary>
    Vector3 CalculateNewPosition(float extension, Transform transform, Vector3 basePosition)
    {
        Vector3 direction = GetMovementDirection(transform);
        return basePosition + direction * extension;
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
            Debug.Log($"LinkArm: Sent command - total extension: {totalExtension:F3}m");
            Debug.Log($"LinkArm: Joint positions - l3: {jointL3Position:F3}m, l2: {jointL2Position:F3}m, l1: {jointL1Position:F3}m, l0: {jointL0Position:F3}m");
            Debug.Log($"LinkArm: Duration: {duration:F1}s");
        }
    }
}
