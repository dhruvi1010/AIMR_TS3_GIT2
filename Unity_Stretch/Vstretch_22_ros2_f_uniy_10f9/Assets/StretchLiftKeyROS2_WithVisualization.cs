using UnityEngine;
using ROS2;

/// <summary>
/// Controls the Stretch robot's lift joint and visualizes it in Unity
/// Supports both ROS2 joint state feedback and local simulation
/// </summary>
public class StretchLiftKeyROS2_WithVisualization : MonoBehaviour
{
    // ROS2 components
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<trajectory_msgs.msg.JointTrajectory> liftPublisher;
    private ISubscription<sensor_msgs.msg.JointState> jointStateSubscriber;

    // Topic configuration
    [Header("ROS2 Topics")]
    [Tooltip("Topic to publish lift commands")]
    public string liftCommandTopic = "/stretch_controller/follow_joint_trajectory/goal";

    [Tooltip("Topic to subscribe to joint states (leave empty to disable feedback)")]
    public string jointStateTopic = "/stretch/joint_states";

    [Tooltip("Name of the lift joint")]
    public string liftJointName = "joint_lift";

    [Header("Unity Visualization")]
    [Tooltip("Transform to move up/down (the lift part of your robot in Unity)")]
    public Transform liftTransform;

    [Tooltip("Movement axis (default: Y-axis for vertical movement)")]
    public Vector3 movementAxis = Vector3.up;

    [Tooltip("Scale factor from meters to Unity units (1.0 = 1m = 1 Unity unit)")]
    public float unityScale = 1.0f;

    [Tooltip("Use ROS2 feedback for visualization (if false, uses local simulation)")]
    public bool useROSFeedback = true;

    [Tooltip("Smooth the visualization movement")]
    public bool smoothMovement = true;

    [Tooltip("Speed of smooth movement (higher = faster)")]
    public float smoothSpeed = 5.0f;

    [Header("Lift Control Settings")]
    [Tooltip("Amount to move lift per key press (meters)")]
    public float liftIncrement = 0.05f;

    [Tooltip("Speed of lift movement (m/s)")]
    public float liftSpeed = 0.1f;

    [Tooltip("Minimum lift position (meters)")]
    public float liftPositionMin = 0.5f;

    [Tooltip("Maximum lift position (meters) - Stretch 3 range: 0-1.1m")]
    public float liftPositionMax = 1.0f;

    [Tooltip("Maximum velocity for trajectory execution")]
    public float maxVelocity = 0.5f;

    [Header("Debug")]
    public bool showDebugInfo = true;

    // Internal state
    private float currentLiftPosition = 0.0f;
    private float targetLiftPosition = 0.0f;
    private float actualRobotPosition = 0.0f;
    private Vector3 initialLiftPosition;
    private bool isInitialized = false;
    private bool hasReceivedJointState = false;

    void Start()
    {
        // Initialize target position to minimum (realistic starting position)
        targetLiftPosition = liftPositionMin;
        actualRobotPosition = liftPositionMin;
        
        // Store initial position of lift transform
        if (liftTransform != null)
        {
            initialLiftPosition = liftTransform.localPosition;
            // Initialize visualization to match starting position
            UpdateLiftVisualization();
            Debug.Log($"Initial lift position set to {initialLiftPosition}");
        }
        else
        {
            Debug.LogWarning("Lift Transform not assigned! Visualization will not work.");
        }

        InitializeROS2();
    }

    void InitializeROS2()
    {
        try
        {
            // Get or find ROS2 Unity component
            ros2Unity = GetComponent<ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                ros2Unity = FindObjectOfType<ROS2UnityComponent>();
                if (ros2Unity == null)
                {
                    Debug.LogError("ROS2UnityComponent not found! Add ROS2UnityCore to scene.");
                    return;
                }
            }

            // Create ROS2 node
            ros2Node = ros2Unity.CreateNode("stretch_lift_visualizer_node");

            // Sanitize topic names (remove double slashes)
            liftCommandTopic = SanitizeTopicName(liftCommandTopic);
            jointStateTopic = SanitizeTopicName(jointStateTopic);

            // Create publisher for joint trajectory
            liftPublisher = ros2Node.CreatePublisher<trajectory_msgs.msg.JointTrajectory>(
                liftCommandTopic
            );

            // Create subscriber for joint states if feedback is enabled
            if (useROSFeedback && !string.IsNullOrEmpty(jointStateTopic))
            {
                jointStateSubscriber = ros2Node.CreateSubscription<sensor_msgs.msg.JointState>(
                    jointStateTopic,
                    OnJointStateReceived
                );
                Debug.Log($"Subscribed to joint states: {jointStateTopic}");
            }

            isInitialized = true;
            Debug.Log($"ROS2 Stretch Lift Controller initialized!");
            Debug.Log($"  Command topic: {liftCommandTopic}");
            Debug.Log($"  Feedback mode: {(useROSFeedback ? "ROS2 Subscription" : "Local Simulation")}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to initialize ROS2: {e.Message}");
        }
    }

    /// <summary>
    /// Callback for joint state messages from ROS2
    /// </summary>
    void OnJointStateReceived(sensor_msgs.msg.JointState msg)
    {
        // Find the lift joint in the message
        for (int i = 0; i < msg.Name.Length; i++)
        {
            if (msg.Name[i] == liftJointName)
            {
                if (i < msg.Position.Length)
                {
                    actualRobotPosition = (float)msg.Position[i];
                    targetLiftPosition = actualRobotPosition;
                    hasReceivedJointState = true;

                    if (showDebugInfo && Time.frameCount % 60 == 0)
                    {
                        Debug.Log($"Received joint state - {liftJointName}: {actualRobotPosition:F3}m");
                    }
                }
                break;
            }
        }
    }

    void Update()
    {
        if (!isInitialized)
            return;

        // Handle keyboard input
        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            MoveLift(liftIncrement);
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            MoveLift(-liftIncrement);
        }

        // Update visualization
        UpdateLiftVisualization();
    }

    /// <summary>
    /// Update the Unity transform based on lift position
    /// </summary>
    void UpdateLiftVisualization()
    {
        if (liftTransform == null)
            return;

        float displayPosition;

        if (useROSFeedback && hasReceivedJointState)
        {
            // Use actual robot feedback
            displayPosition = actualRobotPosition;
        }
        else
        {
            // Use local simulation - always update based on target position
            displayPosition = targetLiftPosition;
        }

        // Calculate target Unity position
        Vector3 offset = movementAxis.normalized * (displayPosition * unityScale);
        Vector3 targetPosition = initialLiftPosition + offset;

        // Apply movement (smooth or instant)
        if (smoothMovement)
        {
            liftTransform.localPosition = Vector3.Lerp(
                liftTransform.localPosition,
                targetPosition,
                Time.deltaTime * smoothSpeed
            );
        }
        else
        {
            liftTransform.localPosition = targetPosition;
        }
    }

    /// <summary>
    /// Move the lift by a delta position
    /// </summary>
    void MoveLift(float deltaPosition)
    {
        if (!isInitialized || liftPublisher == null)
        {
            Debug.LogWarning("ROS2 not initialized or publisher is null");
            return;
        }

        // Update target position
        float previousPosition = targetLiftPosition;
        targetLiftPosition += deltaPosition;
        targetLiftPosition = Mathf.Clamp(targetLiftPosition, liftPositionMin, liftPositionMax);

        if (showDebugInfo)
        {
            Debug.Log($"Lift command: {(deltaPosition > 0 ? "UP" : "DOWN")} " +
                     $"| Target: {targetLiftPosition:F3}m " +
                     $"| Delta: {deltaPosition:F3}m");
        }

        // Create and publish trajectory message
        var trajectory = CreateLiftTrajectory(targetLiftPosition, deltaPosition);
        // -------------------------------------------------------------------------------------to uncomntent the line below to enable publishing
        liftPublisher.Publish(trajectory);

        // Update visualization immediately (works for both ROS feedback and local simulation)
        // When ROS feedback is enabled, it will be overridden by actual robot position when received
        currentLiftPosition = targetLiftPosition;
        
        // Force immediate visualization update for local simulation
        if (!useROSFeedback || !hasReceivedJointState)
        {
            UpdateLiftVisualization();
        }
    }

    /// <summary>
    /// Create a JointTrajectory message for the lift
    /// </summary>
    trajectory_msgs.msg.JointTrajectory CreateLiftTrajectory(float targetPosition, float deltaPosition)
    {
        var trajectory = new trajectory_msgs.msg.JointTrajectory();

        // Set header
        trajectory.Header = new std_msgs.msg.Header
        {
            Stamp = GetCurrentROSTime(),
            Frame_id = "base_link"
        };

        // Set joint name
        trajectory.Joint_names = new string[] { liftJointName };

        // Calculate trajectory duration based on distance and speed
        float actualDelta = Mathf.Abs(deltaPosition);
        float trajectoryDuration = Mathf.Max(actualDelta / liftSpeed, 0.01f); // Minimum 0.01s

        // Create trajectory point
        var point = new trajectory_msgs.msg.JointTrajectoryPoint
        {
            Positions = new double[] { targetPosition },
            Velocities = new double[] { maxVelocity },
            Accelerations = new double[] { },
            Effort = new double[] { },
            Time_from_start = new builtin_interfaces.msg.Duration
            {
                Sec = (int)trajectoryDuration,
                Nanosec = (uint)((trajectoryDuration - (int)trajectoryDuration) * 1e9)
            }
        };

        trajectory.Points = new trajectory_msgs.msg.JointTrajectoryPoint[] { point };

        return trajectory;
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
    /// Get current ROS time
    /// </summary>
    builtin_interfaces.msg.Time GetCurrentROSTime()
    {
        double currentTime = Time.timeAsDouble;
        return new builtin_interfaces.msg.Time
        {
            Sec = (int)currentTime,
            Nanosec = (uint)((currentTime - (int)currentTime) * 1e9)
        };
    }

    /// <summary>
    /// Set lift to specific position
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
        Debug.Log($"Lift reset to {liftPositionMin}m");
    }

    void OnDestroy()
    {
        if (showDebugInfo)
        {
            Debug.Log("StretchLiftKeyROS2 destroyed");
        }
    }

    // Debug GUI
    void OnGUI()
    {
        if (!showDebugInfo)
            return;

        GUILayout.BeginArea(new Rect(10, 10, 350, 200));
        GUILayout.Box("=== Stretch Lift Controller (ROS2) ===");
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
        GUILayout.Label("Controls:  Arrow Keys");
        GUILayout.EndArea();
    }

    // Gizmo to show movement axis in editor
    void OnDrawGizmosSelected()
    {
        if (liftTransform == null)
            return;

        Gizmos.color = Color.green;
        Vector3 start = liftTransform.position;
        Vector3 direction = movementAxis.normalized;

        // Draw movement range
        Gizmos.DrawLine(start, start + direction * liftPositionMin * unityScale);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(start + direction * liftPositionMin * unityScale,
                       start + direction * liftPositionMax * unityScale);

        // Draw current position
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(start + direction * targetLiftPosition * unityScale, 0.02f);
    }
}