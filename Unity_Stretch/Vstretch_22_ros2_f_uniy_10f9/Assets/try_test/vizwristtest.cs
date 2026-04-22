using UnityEngine;
using ROS2;
using trajectory_msgs.msg;
using builtin_interfaces.msg;

/// <summary>
/// Test script to verify correct rotation axes for wrist joints
/// Use this to test each axis individually and compare with real robot
/// NOW INCLUDES ROS2 PUBLISHING to test with real robot!
/// </summary>
public class vizwristtest : MonoBehaviour
{
    [Header("ROS2 Settings")]
    [Tooltip("Enable ROS2 publishing to real robot")]
    public bool enableROS2Publishing = true;
    
    [Tooltip("Topic to publish wrist commands (action goal topic)")]
    public string wristCommandTopic = "/stretch_controller/follow_joint_trajectory/goal";
    
    [Tooltip("Trajectory duration (seconds)")]
    public float duration = 0.5f;

    [Header("Wrist Transform References")]
    [Tooltip("Assign link_wrist_yaw Transform")]
    public Transform WristYawTransform;
    
    [Tooltip("Assign link_wrist_pitch Transform")]
    public Transform WristPitchTransform;
    
    [Tooltip("Assign link_wrist_roll Transform")]
    public Transform WristRollTransform;

    [Header("Test Values (radians)")]
    [Tooltip("Test angle for yaw (radians)")]
    public float testYaw = 0.2f; // ~11.5 degrees
    
    [Tooltip("Test angle for pitch (radians)")]
    public float testPitch = 0.2f; // ~11.5 degrees
    
    [Tooltip("Test angle for roll (radians)")]
    public float testRoll = 0.2f; // ~11.5 degrees

    [Header("Visualization Settings")]
    [Tooltip("Smooth rotation animation (lerp) - DISABLED by default to avoid jerky movement")]
    public bool smoothRotation = false;
    
    [Tooltip("Rotation speed for smooth animation")]
    public float rotationSpeed = 5.0f;

    [Header("Test Controls")]
    [Tooltip("Press Y to test Yaw, P to test Pitch, R to test Roll")]
    public bool enableKeyboardControls = true;
    
    [Tooltip("Show debug logs")]
    public bool showDebugLogs = true;

    // ROS2 components
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<JointTrajectory> wristPublisher;
    private bool isInitialized = false;

    // Base rotations (stored at start)
    private Quaternion baseYawRotation = Quaternion.identity;
    private Quaternion basePitchRotation = Quaternion.identity;
    private Quaternion baseRollRotation = Quaternion.identity;

    // Target rotations (for smooth animation)
    private Quaternion targetYawRotation = Quaternion.identity;
    private Quaternion targetPitchRotation = Quaternion.identity;
    private Quaternion targetRollRotation = Quaternion.identity;

    // Current test state
    private bool yawTested = false;
    private bool pitchTested = false;
    private bool rollTested = false;
    private string currentTest = "None";
    
    // Current joint values (for ROS2 publishing)
    private float currentYaw = 0.0f;
    private float currentPitch = 0.0f;
    private float currentRoll = 0.0f;

    void Start()
    {
        // Store base rotations
        if (WristYawTransform != null)
        {
            baseYawRotation = WristYawTransform.localRotation;
            if (showDebugLogs)
            {
                Debug.Log($"vizwrist: Base Yaw Rotation: {baseYawRotation.eulerAngles}");
            }
        }
        else
        {
            Debug.LogWarning("vizwrist: WristYawTransform not assigned!");
        }

        if (WristPitchTransform != null)
        {
            basePitchRotation = WristPitchTransform.localRotation;
            targetPitchRotation = basePitchRotation; // Initialize target
            if (showDebugLogs)
            {
                Debug.Log($"vizwrist: Base Pitch Rotation: {basePitchRotation.eulerAngles}");
            }
        }
        else
        {
            Debug.LogWarning("vizwrist: WristPitchTransform not assigned!");
        }

        if (WristRollTransform != null)
        {
            baseRollRotation = WristRollTransform.localRotation;
            targetRollRotation = baseRollRotation; // Initialize target
            if (showDebugLogs)
            {
                Debug.Log($"vizwrist: Base Roll Rotation: {baseRollRotation.eulerAngles}");
            }
        }
        else
        {
            Debug.LogWarning("vizwrist: WristRollTransform not assigned!");
        }

        if (showDebugLogs)
        {
            Debug.Log("vizwrist: Test Script Ready!");
            Debug.Log("vizwrist: Press Y to test Yaw (X, Y, Z axes)");
            Debug.Log("vizwrist: Press P to test Pitch (X, Y, Z axes)");
            Debug.Log("vizwrist: Press R to test Roll (X, Y, Z axes)");
            Debug.Log("vizwrist: Press Space to reset all joints to base rotation");
            Debug.Log("vizwrist: Press 1/2/3 to cycle through X/Y/Z axis tests for current joint");
        }
    }

    void Update()
    {
        // Try to initialize ROS2 if not already initialized
        if (enableROS2Publishing && !isInitialized)
        {
            if (ros2Unity != null && ros2Unity.Ok())
            {
                InitializeROS2();
            }
            else if (ros2Unity == null)
            {
                // Try to find ROS2UnityComponent again
                ros2Unity = FindObjectOfType<ROS2UnityComponent>();
            }
        }

        // Smooth rotation animation (only if enabled)
        if (smoothRotation)
        {
            UpdateSmoothRotations();
        }

        if (!enableKeyboardControls)
            return;

        // Test Yaw
        if (Input.GetKeyDown(KeyCode.Y))
        {
            currentTest = "Yaw";
            yawTested = false;
            TestYaw();
        }

        // Test Pitch
        if (Input.GetKeyDown(KeyCode.P))
        {
            currentTest = "Pitch";
            pitchTested = false;
            TestPitch();
        }

        // Test Roll
        if (Input.GetKeyDown(KeyCode.R))
        {
            currentTest = "Roll";
            rollTested = false;
            TestRoll();
        }

        // Reset all to base
        if (Input.GetKeyDown(KeyCode.Space))
        {
            ResetAll();
        }

        // Cycle through axis tests
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            TestAxis("X");
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            TestAxis("Y");
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            TestAxis("Z");
        }
    }

    /// <summary>
    /// Smooth rotation animation using lerp
    /// </summary>
    void UpdateSmoothRotations()
    {
        if (WristYawTransform != null)
        {
            WristYawTransform.localRotation = Quaternion.Lerp(
                WristYawTransform.localRotation,
                targetYawRotation,
                UnityEngine.Time.deltaTime * rotationSpeed
            );
        }

        if (WristPitchTransform != null)
        {
            WristPitchTransform.localRotation = Quaternion.Lerp(
                WristPitchTransform.localRotation,
                targetPitchRotation,
                UnityEngine.Time.deltaTime * rotationSpeed
            );
        }

        if (WristRollTransform != null)
        {
            WristRollTransform.localRotation = Quaternion.Lerp(
                WristRollTransform.localRotation,
                targetRollRotation,
                UnityEngine.Time.deltaTime * rotationSpeed
            );
        }
    }

    /// <summary>
    /// Initialize ROS2 publisher
    /// </summary>
    void InitializeROS2()
    {
        try
        {
            ros2Node = ros2Unity.CreateNode("vizwrist_test_node");
            wristPublisher = ros2Node.CreatePublisher<JointTrajectory>(wristCommandTopic);

            if (showDebugLogs)
            {
                Debug.Log($"vizwrist: ROS2 initialized successfully!");
                Debug.Log($"vizwrist: Publisher created - {wristCommandTopic}");
            }

            isInitialized = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"vizwrist: Failed to initialize ROS2: {e.Message}");
            enableROS2Publishing = false;
        }
    }

    /// <summary>
    /// Test Yaw with all three axes
    /// </summary>
    void TestYaw()
    {
        if (WristYawTransform == null)
        {
            Debug.LogError("vizwrist: WristYawTransform not assigned!");
            return;
        }

        float angleDeg = testYaw * Mathf.Rad2Deg;

        Debug.Log("=== TESTING YAW ===");
        Debug.Log($"Testing with angle: {testYaw} rad ({angleDeg}°)");
        Debug.Log("Compare Unity visualization with real robot movement:");
        Debug.Log("1. X-axis rotation (Euler X)");
        Debug.Log("2. Y-axis rotation (Euler Y)");
        Debug.Log("3. Z-axis rotation (Euler Z)");
        Debug.Log("Press 1/2/3 to test each axis, or Y again to cycle");

        // Test X axis first
        TestYawAxis("X");
    }

    /// <summary>
    /// Test Pitch with all three axes
    /// </summary>
    void TestPitch()
    {
        if (WristPitchTransform == null)
        {
            Debug.LogError("vizwrist: WristPitchTransform not assigned!");
            return;
        }

        float angleDeg = testPitch * Mathf.Rad2Deg;

        Debug.Log("=== TESTING PITCH ===");
        Debug.Log($"Testing with angle: {testPitch} rad ({angleDeg}°)");
        Debug.Log("Compare Unity visualization with real robot movement:");
        Debug.Log("1. X-axis rotation (Euler X)");
        Debug.Log("2. Y-axis rotation (Euler Y) - This was working before!");
        Debug.Log("3. Z-axis rotation (Euler Z)");
        Debug.Log("Press 1/2/3 to test each axis, or P again to cycle");

        // Test Y axis first (since it was working)
        TestPitchAxis("Y");
    }

    /// <summary>
    /// Test Roll with all three axes
    /// </summary>
    void TestRoll()
    {
        if (WristRollTransform == null)
        {
            Debug.LogError("vizwrist: WristRollTransform not assigned!");
            return;
        }

        float angleDeg = testRoll * Mathf.Rad2Deg;

        Debug.Log("=== TESTING ROLL ===");
        Debug.Log($"Testing with angle: {testRoll} rad ({angleDeg}°)");
        Debug.Log("Compare Unity visualization with real robot movement:");
        Debug.Log("1. X-axis rotation (Euler X)");
        Debug.Log("2. Y-axis rotation (Euler Y)");
        Debug.Log("3. Z-axis rotation (Euler Z)");
        Debug.Log("Press 1/2/3 to test each axis, or R again to cycle");

        // Test Z axis first
        TestRollAxis("Z");
    }

    /// <summary>
    /// Test specific axis for current joint
    /// </summary>
    void TestAxis(string axis)
    {
        if (currentTest == "Yaw")
        {
            TestYawAxis(axis);
        }
        else if (currentTest == "Pitch")
        {
            TestPitchAxis(axis);
        }
        else if (currentTest == "Roll")
        {
            TestRollAxis(axis);
        }
        else
        {
            Debug.LogWarning($"vizwrist: No test selected. Press Y/P/R first.");
        }
    }

    /// <summary>
    /// Test Yaw with specific axis
    /// </summary>
    void TestYawAxis(string axis)
    {
        if (WristYawTransform == null) return;

        float angleDeg = testYaw * Mathf.Rad2Deg;
        Quaternion rotation = Quaternion.identity;

        // IMPORTANT: Always set currentYaw to testYaw for ROS2 publishing
        // The axis only affects Unity visualization, but ROS2 always needs the joint value
        currentYaw = testYaw; // Store for ROS2 (same value regardless of visualization axis)

        switch (axis.ToUpper())
        {
            case "X":
                rotation = Quaternion.Euler(angleDeg, 0, 0);
                break;
            case "Y":
                rotation = Quaternion.Euler(0, angleDeg, 0);
                break;
            case "Z":
                rotation = Quaternion.Euler(0, 0, angleDeg);
                break;
        }

        // Set target rotation (for smooth animation)
        targetYawRotation = baseYawRotation * rotation;
        
        // Apply immediately if not using smooth rotation
        if (!smoothRotation)
        {
            WristYawTransform.localRotation = targetYawRotation;
        }

        // Publish to ROS2 if enabled
        if (enableROS2Publishing && isInitialized)
        {
            SendWristCommand(currentYaw, currentPitch, currentRoll);
        }

        Debug.Log($"YAW: Testing {axis}-axis rotation: {angleDeg}° ({testYaw} rad)");
        Debug.Log($"YAW: Current rotation: {targetYawRotation.eulerAngles}");
        Debug.Log($"YAW: ROS2 Published: Yaw={currentYaw:F3}rad, Pitch={currentPitch:F3}rad, Roll={currentRoll:F3}rad");
        Debug.Log("YAW: Compare Unity visualization AND real robot - does it match?");
    }

    /// <summary>
    /// Test Pitch with specific axis
    /// </summary>
    void TestPitchAxis(string axis)
    {
        if (WristPitchTransform == null) return;

        float angleDeg = testPitch * Mathf.Rad2Deg;
        Quaternion rotation = Quaternion.identity;

        // IMPORTANT: Always set currentPitch to testPitch for ROS2 publishing
        // The axis only affects Unity visualization, but ROS2 always needs the joint value
        currentPitch = testPitch; // Store for ROS2 (same value regardless of visualization axis)

        switch (axis.ToUpper())
        {
            case "X":
                rotation = Quaternion.Euler(angleDeg, 0, 0);
                break;
            case "Y":
                rotation = Quaternion.Euler(0, angleDeg, 0);
                break;
            case "Z":
                rotation = Quaternion.Euler(0, 0, angleDeg);
                break;
        }

        // Set target rotation (for smooth animation)
        targetPitchRotation = basePitchRotation * rotation;
        
        // Apply immediately if not using smooth rotation
        if (!smoothRotation)
        {
            WristPitchTransform.localRotation = targetPitchRotation;
        }

        // Publish to ROS2 if enabled
        if (enableROS2Publishing && isInitialized)
        {
            SendWristCommand(currentYaw, currentPitch, currentRoll);
        }

        Debug.Log($"PITCH: Testing {axis}-axis rotation: {angleDeg}° ({testPitch} rad)");
        Debug.Log($"PITCH: Current rotation: {targetPitchRotation.eulerAngles}");
        Debug.Log($"PITCH: ROS2 Published: Yaw={currentYaw:F3}rad, Pitch={currentPitch:F3}rad, Roll={currentRoll:F3}rad");
        Debug.Log("PITCH: Compare Unity visualization AND real robot - does it match?");
        Debug.Log("PITCH: Note: Y-axis was working before!");
    }

    /// <summary>
    /// Test Roll with specific axis
    /// </summary>
    void TestRollAxis(string axis)
    {
        if (WristRollTransform == null) return;

        float angleDeg = testRoll * Mathf.Rad2Deg;
        Quaternion rotation = Quaternion.identity;

        switch (axis.ToUpper())
        {
            case "X":
                rotation = Quaternion.Euler(angleDeg, 0, 0);
                currentRoll = testRoll; // Store for ROS2
                break;
            case "Y":
                rotation = Quaternion.Euler(0, angleDeg, 0);
                currentRoll = testRoll; // Store for ROS2
                break;
            case "Z":
                rotation = Quaternion.Euler(0, 0, angleDeg);
                currentRoll = testRoll; // Store for ROS2
                break;
        }

        // Set target rotation (for smooth animation)
        targetRollRotation = baseRollRotation * rotation;
        
        // Apply immediately if not using smooth rotation
        if (!smoothRotation)
        {
            WristRollTransform.localRotation = targetRollRotation;
        }

        // Publish to ROS2 if enabled
        if (enableROS2Publishing && isInitialized)
        {
            SendWristCommand(currentYaw, currentPitch, currentRoll);
        }

        Debug.Log($"ROLL: Testing {axis}-axis rotation: {angleDeg}° ({testRoll} rad)");
        Debug.Log($"ROLL: Current rotation: {targetRollRotation.eulerAngles}");
        Debug.Log($"ROLL: ROS2 Published: {(enableROS2Publishing && isInitialized ? "YES" : "NO")}");
        Debug.Log("ROLL: Compare Unity visualization AND real robot - does it match?");
    }

    /// <summary>
    /// Reset all joints to base rotation
    /// </summary>
    void ResetAll()
    {
        // Reset target rotations
        targetYawRotation = baseYawRotation;
        targetPitchRotation = basePitchRotation;
        targetRollRotation = baseRollRotation;

        // Reset joint values
        currentYaw = 0.0f;
        currentPitch = 0.0f;
        currentRoll = 0.0f;

        // Apply immediately (don't use smooth rotation for reset)
        if (WristYawTransform != null)
        {
            WristYawTransform.localRotation = baseYawRotation;
        }
        if (WristPitchTransform != null)
        {
            WristPitchTransform.localRotation = basePitchRotation;
        }
        if (WristRollTransform != null)
        {
            WristRollTransform.localRotation = baseRollRotation;
        }

        // Publish reset to ROS2
        SendWristCommand(0.0f, 0.0f, 0.0f);

        Debug.Log("RESET: All joints reset to base rotation (0, 0, 0)");
        Debug.Log($"RESET: ROS2 Published: {(enableROS2Publishing && isInitialized ? "YES" : "NO")}");
    }

    /// <summary>
    /// Send wrist command to ROS2 (same format as wristButton.cs)
    /// </summary>
    void SendWristCommand(float yaw, float pitch, float roll)
    {
        if (wristPublisher == null || !isInitialized)
            return;

        var trajectory = new JointTrajectory();

        // Set header - matching manual command format (empty frame_id, zero stamp)
        trajectory.Header = new std_msgs.msg.Header
        {
            Stamp = new builtin_interfaces.msg.Time
            {
                Sec = 0,
                Nanosec = 0
            },
            Frame_id = ""
        };

        // Set joint names
        trajectory.Joint_names = new string[] 
        { 
            "joint_wrist_yaw",
            "joint_wrist_pitch",
            "joint_wrist_roll"
        };

        // Create trajectory point
        var point = new JointTrajectoryPoint
        {
            Positions = new double[] { yaw, pitch, roll },
            Velocities = new double[0],
            Accelerations = new double[0],
            Effort = new double[0],
            Time_from_start = new builtin_interfaces.msg.Duration
            {
                Sec = (int)duration,
                Nanosec = 0
            }
        };

        trajectory.Points = new JointTrajectoryPoint[] { point };

        // Publish trajectory
        wristPublisher.Publish(trajectory);

        if (showDebugLogs)
        {
            Debug.Log($"vizwrist: Published to ROS2 - Yaw: {yaw:F3}rad, Pitch: {pitch:F3}rad, Roll: {roll:F3}rad");
        }
    }

    /// <summary>
    /// Test with negative angle (for sign verification)
    /// </summary>
    void TestNegative(string joint, string axis)
    {
        Transform target = null;
        Quaternion baseRot = Quaternion.identity;
        float testAngle = 0f;

        switch (joint.ToUpper())
        {
            case "YAW":
                target = WristYawTransform;
                baseRot = baseYawRotation;
                testAngle = -testYaw * Mathf.Rad2Deg;
                break;
            case "PITCH":
                target = WristPitchTransform;
                baseRot = basePitchRotation;
                testAngle = -testPitch * Mathf.Rad2Deg;
                break;
            case "ROLL":
                target = WristRollTransform;
                baseRot = baseRollRotation;
                testAngle = -testRoll * Mathf.Rad2Deg;
                break;
        }

        if (target == null) return;

        Quaternion rotation = Quaternion.identity;
        switch (axis.ToUpper())
        {
            case "X":
                rotation = Quaternion.Euler(testAngle, 0, 0);
                break;
            case "Y":
                rotation = Quaternion.Euler(0, testAngle, 0);
                break;
            case "Z":
                rotation = Quaternion.Euler(0, 0, testAngle);
                break;
        }

        target.localRotation = baseRot * rotation;
        Debug.Log($"{joint}: Testing NEGATIVE {axis}-axis rotation: {testAngle}°");
    }
}
