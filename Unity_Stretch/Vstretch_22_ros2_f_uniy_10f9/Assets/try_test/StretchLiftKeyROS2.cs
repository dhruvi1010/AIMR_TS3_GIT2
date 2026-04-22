using UnityEngine;
using ROS2;

/// <summary>
/// Controls the Stretch robot's lift joint using keyboard input via ROS2
/// Compatible with RobotecAI's ros2-for-unity package
/// </summary>
public class StretchLiftKeyROS2 : MonoBehaviour
{
    // ROS2 components
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<trajectory_msgs.msg.JointTrajectory> liftPublisher;

    // Topic configuration
    private string liftTopicName = "/stretch_controller/follow_joint_trajectory/goal";
    private string jointName = "joint_lift";

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
    private float currentLiftPosition = 0.5f;
    private bool isInitialized = false;

    void Start()
    {
        InitializeROS2();
    }

    /// <summary>
    /// Initialize ROS2 connection and publisher
    /// </summary>
    void InitializeROS2()
    {
        try
        {
            // Get or create ROS2 Unity component
            ros2Unity = GetComponent<ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                Debug.LogError("ROS2UnityComponent not found! Please add it to this GameObject.");
                return;
            }

            // Create ROS2 node
            ros2Node = ros2Unity.CreateNode("stretch_lift_controller_node");

            // Create publisher for joint trajectory
            liftPublisher = ros2Node.CreatePublisher<trajectory_msgs.msg.JointTrajectory>(
                liftTopicName
            );

            isInitialized = true;
            Debug.Log($"ROS2 Stretch Lift Controller initialized on topic: {liftTopicName}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to initialize ROS2: {e.Message}");
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
            if (showDebugInfo)
            {
                Debug.Log($"Lift UP - New position: {currentLiftPosition:F3}m");
            }
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            MoveLift(-liftIncrement);
            if (showDebugInfo)
            {
                Debug.Log($"Lift DOWN - New position: {currentLiftPosition:F3}m");
            }
        }
    }

    /// <summary>
    /// Move the lift by a delta position
    /// </summary>
    /// <param name="deltaPosition">Amount to move (positive = up, negative = down)</param>
    void MoveLift(float deltaPosition)
    {
        if (!isInitialized || liftPublisher == null)
        {
            Debug.LogWarning("ROS2 not initialized or publisher is null");
            return;
        }

        // Update and clamp position
        float previousPosition = currentLiftPosition;
        currentLiftPosition += deltaPosition;
        currentLiftPosition = Mathf.Clamp(currentLiftPosition, liftPositionMin, liftPositionMax);

        if (showDebugInfo)
        {
            Debug.Log($"Lift command: delta={deltaPosition:F3}m, " +
                     $"previous={previousPosition:F3}m, new={currentLiftPosition:F3}m");
        }

        // Create and publish trajectory message
        var trajectory = CreateLiftTrajectory(currentLiftPosition, deltaPosition);
        liftPublisher.Publish(trajectory);
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
        trajectory.Joint_names = new string[] { jointName };

        // Calculate trajectory duration based on distance and speed
        float actualDelta = Mathf.Abs(deltaPosition);
        float trajectoryDuration = actualDelta / liftSpeed;

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
    /// Reset lift to initial position
    /// </summary>
    public void ResetLift()
    {
        float resetPosition = liftPositionMin;
        float delta = resetPosition - currentLiftPosition;
        currentLiftPosition = resetPosition;

        if (isInitialized && liftPublisher != null)
        {
            var trajectory = CreateLiftTrajectory(currentLiftPosition, delta);
            liftPublisher.Publish(trajectory);
            Debug.Log($"Lift reset to {currentLiftPosition}m");
        }
    }

    /// <summary>
    /// Set lift to specific position
    /// </summary>
    public void SetLiftPosition(float position)
    {
        position = Mathf.Clamp(position, liftPositionMin, liftPositionMax);
        float delta = position - currentLiftPosition;
        MoveLift(delta);
    }

    void OnDestroy()
    {
        // Cleanup is handled automatically by ROS2UnityComponent
        if (showDebugInfo)
        {
            //Debug.Log("StretchLiftKeyROS2 destroyed");
        }
    }

    // Optional: GUI for debugging
    void OnGUI()
    {
        if (!showDebugInfo)
            return;

        GUILayout.BeginArea(new Rect(10, 10, 300, 150));
        GUILayout.Box("Stretch Lift Controller (ROS2)");
        GUILayout.Label($"Status: {(isInitialized ? "Connected" : "Not Initialized")}");
        GUILayout.Label($"Topic: {liftTopicName}");
        GUILayout.Label($"Current Position: {currentLiftPosition:F3}m");
        GUILayout.Label($"Range: [{liftPositionMin:F2}, {liftPositionMax:F2}]m");
        GUILayout.Label("Controls:  Arrow Keys");
        GUILayout.EndArea();
    }
}