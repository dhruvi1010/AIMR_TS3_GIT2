using UnityEngine;
using ROS2;
using trajectory_msgs.msg;
using sensor_msgs.msg;
using std_msgs.msg;
using builtin_interfaces.msg;

/// <summary>
/// Clean implementation for controlling Stretch3 robot's lift joint from Unity
/// - Publishes commands to robot via ROS2
/// - Subscribes to joint_states for real-time feedback
/// - Updates Unity visualization to match robot movement
/// 
/// Based on URDF: joint_lift is prismatic joint (0.0 to 1.1m range)
/// Parent: link_mast, Child: link_lift
/// </summary>
public class LiftUnity : MonoBehaviour
{
    [Header("ROS2 Configuration")]
    [Tooltip("Topic to publish lift commands (action goal topic)")]
    public string liftCommandTopic = "/stretch_controller/follow_joint_trajectory/goal";

    [Tooltip("Topic to subscribe for joint states feedback")]
    public string jointStateTopic = "/joint_states";

    [Tooltip("Name of the lift joint in URDF")]
    public string liftJointName = "joint_lift";

    [Header("Unity Visualization")]
    [Tooltip("Transform of the lift part in Unity - ASSIGN 'link_lift' GameObject here")]
    public Transform liftTransform;

    [Tooltip("Movement axis in local space of liftTransform (default: X-axis for prismatic joint)")]
    public Vector3 movementAxis = Vector3.right; // X-axis for prismatic joint

    [Tooltip("Scale: 1.0 = 1 meter = 1 Unity unit")]
    public float unityScale = 1.0f;

    [Tooltip("Smooth movement animation")]
    public bool smoothMovement = true;

    [Tooltip("Smooth movement speed")]
    public float smoothSpeed = 5.0f;

    [Header("Lift Control")]
    [Tooltip("Amount to move per key press (meters)")]
    public float liftIncrement = 0.05f;

    [Tooltip("Speed of lift movement (m/s)")]
    public float liftSpeed = 0.1f;

    [Tooltip("Minimum lift position (meters) - from URDF: 0.0")]
    public float liftPositionMin = 0.5f;

    [Tooltip("Maximum lift position (meters) - from URDF: 1.1")]
    public float liftPositionMax = 1.0f;

    [Tooltip("Maximum velocity for trajectory")]
    public float maxVelocity = 0.5f;

    [Header("Feedback Mode")]
    [Tooltip("Use ROS2 joint_states feedback (accurate) or local simulation (fast)")]
    public bool useROSFeedback = true;

    [Header("Debug")]
    public bool showDebugInfo = true;

    // ROS2 components
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<JointTrajectory> liftPublisher;
    private ISubscription<JointState> jointStateSubscriber;

    // Internal state
    private float targetLiftPosition = 0.0f;  // Commanded position
    private float actualRobotPosition = 0.0f;  // Actual position from robot
    private Vector3 initialLiftPosition;      // Initial Unity transform position
    private bool isInitialized = false;
    private bool hasReceivedJointState = false;

    void Start()
    {
        // Initialize target position to minimum
        targetLiftPosition = liftPositionMin;
        actualRobotPosition = liftPositionMin;

        // Store initial Unity transform position
        if (liftTransform != null)
        {
            initialLiftPosition = liftTransform.localPosition;
            if (showDebugInfo)
            {
                Debug.Log($"LiftUnity: Initial lift position stored: {initialLiftPosition}");
            }
        }
        else
        {
            Debug.LogWarning("LiftUnity: Lift Transform not assigned! Visualization will not work.");
        }

        InitializeROS2();
    }

    void InitializeROS2()
    {
        try
        {
            // Find ROS2 Unity component
            ros2Unity = GetComponent<ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                ros2Unity = FindObjectOfType<ROS2UnityComponent>();
                if (ros2Unity == null)
                {
                    Debug.LogError("LiftUnity: ROS2UnityComponent not found! Add ROS2UnityCore to scene.");
                    return;
                }
            }

            // Wait for ROS2 to be ready
            if (!ros2Unity.Ok())
            {
                if (showDebugInfo)
                {
                    Debug.LogWarning("LiftUnity: ROS2 not ready yet, will initialize in Update()");
                }
                return;
            }

            // Create ROS2 node
            ros2Node = ros2Unity.CreateNode("lift_unity_node");

            // Sanitize topic names (remove double slashes)
            liftCommandTopic = SanitizeTopicName(liftCommandTopic);
            jointStateTopic = SanitizeTopicName(jointStateTopic);

            // Create publisher for lift commands
            liftPublisher = ros2Node.CreatePublisher<JointTrajectory>(liftCommandTopic);
            if (showDebugInfo)
            {
                Debug.Log($"LiftUnity: Publisher created - {liftCommandTopic}");
            }

            // Create subscriber for joint states if feedback enabled
            if (useROSFeedback && !string.IsNullOrEmpty(jointStateTopic))
            {
                jointStateSubscriber = ros2Node.CreateSubscription<JointState>(
                    jointStateTopic,
                    OnJointStateReceived
                );
                if (showDebugInfo)
                {
                    Debug.Log($"LiftUnity: Subscribed to joint states - {jointStateTopic}");
                    Debug.Log($"LiftUnity: Looking for joint: {liftJointName}");
                }
            }

            isInitialized = true;
            if (showDebugInfo)
            {
                Debug.Log("LiftUnity: ROS2 initialized successfully!");
                Debug.Log($"  Command topic: {liftCommandTopic}");
                Debug.Log($"  Feedback mode: {(useROSFeedback ? "ROS2 Subscription" : "Local Simulation")}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"LiftUnity: Failed to initialize ROS2: {e.Message}");
            Debug.LogError($"Stack trace: {e.StackTrace}");
        }
    }

    /// <summary>
    /// Sanitize topic name to prevent double slashes
    /// </summary>
    string SanitizeTopicName(string topicName)
    {
        if (string.IsNullOrEmpty(topicName))
            return topicName;

        // Remove double slashes (but keep leading slash)
        string sanitized = topicName.Replace("//", "/");
        
        // Ensure it starts with / if it's not empty
        if (!string.IsNullOrEmpty(sanitized) && !sanitized.StartsWith("/"))
        {
            sanitized = "/" + sanitized;
        }

        return sanitized;
    }

    /// <summary>
    /// Callback when joint state message is received from robot
    /// </summary>
    void OnJointStateReceived(JointState msg)
    {
        // Find joint_lift in the message
        for (int i = 0; i < msg.Name.Length; i++)
        {
            if (msg.Name[i] == liftJointName)
            {
                if (i < msg.Position.Length)
                {
                    actualRobotPosition = (float)msg.Position[i];
                    hasReceivedJointState = true;

                    if (showDebugInfo && UnityEngine.Time.frameCount % 120 == 0) // Log every ~2 seconds at 60fps
                    {
                        Debug.Log($"LiftUnity: Joint state received - {liftJointName} = {actualRobotPosition:F3}m");
                    }
                }
                break;
            }
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
            else
            {
                // Still update visualization even if ROS2 not ready
                UpdateLiftVisualization();
                return;
            }
        }

        // Handle keyboard input
        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            MoveLift(liftIncrement);
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            MoveLift(-liftIncrement);
        }

        // Update Unity visualization every frame
        UpdateLiftVisualization();
    }

    /// <summary>
    /// Move lift by delta position (positive = up, negative = down)
    /// </summary>
    void MoveLift(float deltaPosition)
    {
        if (!isInitialized || liftPublisher == null)
        {
            Debug.LogWarning("LiftUnity: ROS2 not initialized or publisher is null");
            return;
        }

        // Update target position
        float previousPosition = targetLiftPosition;
        targetLiftPosition += deltaPosition;
        targetLiftPosition = Mathf.Clamp(targetLiftPosition, liftPositionMin, liftPositionMax);

        if (showDebugInfo)
        {
            Debug.Log($"LiftUnity: Command {(deltaPosition > 0 ? "UP" : "DOWN")} " +
                     $"| Target: {targetLiftPosition:F3}m " +
                     $"| Delta: {deltaPosition:F3}m");
        }

        // Create and publish trajectory message
        var trajectory = CreateLiftTrajectory(targetLiftPosition, deltaPosition);
        liftPublisher.Publish(trajectory);

        // Update visualization immediately for local simulation or if ROS feedback not yet available
        if (!useROSFeedback || !hasReceivedJointState)
        {
            actualRobotPosition = targetLiftPosition;
        }
        
        // Force immediate visualization update
        UpdateLiftVisualization();
    }

    /// <summary>
    /// Create JointTrajectory message for lift command
    /// </summary>
    JointTrajectory CreateLiftTrajectory(float targetPosition, float deltaPosition)
    {
        var trajectory = new JointTrajectory();

        // Set header
        trajectory.Header = new Header
        {
            Stamp = GetCurrentROSTime(),
            Frame_id = "base_link"
        };

        // Set joint name
        trajectory.Joint_names = new string[] { liftJointName };

        // Calculate trajectory duration
        float actualDelta = Mathf.Abs(deltaPosition);
        float trajectoryDuration = Mathf.Max(actualDelta / liftSpeed, 0.01f); // Minimum 0.01s

        // Create trajectory point
        var point = new JointTrajectoryPoint
        {
            Positions = new double[] { targetPosition },
            Velocities = new double[] { maxVelocity },
            Accelerations = new double[] { },
            Effort = new double[] { },
            Time_from_start = new Duration
            {
                Sec = (int)trajectoryDuration,
                Nanosec = (uint)((trajectoryDuration - (int)trajectoryDuration) * 1e9)
            }
        };

        trajectory.Points = new JointTrajectoryPoint[] { point };

        return trajectory;
    }

    /// <summary>
    /// Get current ROS time
    /// </summary>
    builtin_interfaces.msg.Time GetCurrentROSTime()
    {
        double currentTime = UnityEngine.Time.timeAsDouble;
        return new builtin_interfaces.msg.Time
        {
            Sec = (int)currentTime,
            Nanosec = (uint)((currentTime - (int)currentTime) * 1e9)
        };
    }

    /// <summary>
    /// Update Unity visualization based on lift position
    /// </summary>
    void UpdateLiftVisualization()
    {
        if (liftTransform == null)
            return;

        float displayPosition;

        if (useROSFeedback && hasReceivedJointState)
        {
            // Use actual robot feedback (most accurate)
            displayPosition = actualRobotPosition;
        }
        else
        {
            // Use local simulation (immediate feedback)
            displayPosition = targetLiftPosition;
        }

        // Calculate target Unity position
        // Movement axis is in local space of liftTransform (matches ArticulationBody axis)
        // Since we're modifying localPosition, we use the movement axis directly
        Vector3 offset = movementAxis.normalized * (displayPosition * unityScale);
        Vector3 targetPosition = initialLiftPosition + offset;

        // Apply movement (smooth or instant)
        if (smoothMovement)
        {
            liftTransform.localPosition = Vector3.Lerp(
                liftTransform.localPosition,
                targetPosition,
                UnityEngine.Time.deltaTime * smoothSpeed
            );
        }
        else
        {
            liftTransform.localPosition = targetPosition;
        }
    }

    /// <summary>
    /// Set lift to specific position programmatically
    /// </summary>
    public void SetLiftPosition(float position)
    {
        position = Mathf.Clamp(position, liftPositionMin, liftPositionMax);
        float delta = position - targetLiftPosition;
        MoveLift(delta);
    }

    /// <summary>
    /// Reset lift to minimum position
    /// </summary>
    public void ResetLift()
    {
        SetLiftPosition(liftPositionMin);
        if (showDebugInfo)
        {
            Debug.Log($"LiftUnity: Lift reset to {liftPositionMin}m");
        }
    }

    // Debug GUI overlay
    void OnGUI()
    {
        if (!showDebugInfo)
            return;

        GUILayout.BeginArea(new Rect(10, 10, 400, 250));
        GUILayout.Box("=== Lift Unity Controller ===");
        GUILayout.Label($"Status: {(isInitialized ? " Connected" : " Not Initialized")}");
        GUILayout.Label($"Command Topic: {liftCommandTopic}");
        
        if (useROSFeedback)
        {
            GUILayout.Label($"Feedback: ROS2 ({(hasReceivedJointState ? " Active" : " No Data")})");
            GUILayout.Label($"Robot Position: {actualRobotPosition:F3}m");
        }
        else
        {
            GUILayout.Label($"Feedback: Local Simulation");
        }

        GUILayout.Label($"Target Position: {targetLiftPosition:F3}m");
        GUILayout.Label($"Range: [{liftPositionMin:F2}, {liftPositionMax:F2}]m");
        GUILayout.Label($"Lift Transform: {(liftTransform != null ? " Assigned" : " Not Assigned")}");
        GUILayout.Label("--------");
        GUILayout.Label("Controls: up down Arrow Keys");
        GUILayout.EndArea();
    }

    // Draw gizmos in editor to show movement range
    void OnDrawGizmosSelected()
    {
        if (liftTransform == null)
            return;

        Gizmos.color = Color.green;
        Vector3 start = liftTransform.position;
        Vector3 direction = movementAxis.normalized;

        // Draw minimum position
        Gizmos.DrawLine(start, start + direction * liftPositionMin * unityScale);
        
        // Draw movement range
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(
            start + direction * liftPositionMin * unityScale,
            start + direction * liftPositionMax * unityScale
        );

        // Draw current target position
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(start + direction * targetLiftPosition * unityScale, 0.02f);
    }
}
