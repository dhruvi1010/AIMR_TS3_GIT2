using UnityEngine;
using UnityEngine.InputSystem;
using ROS2;
using trajectory_msgs.msg;
using builtin_interfaces.msg;

/// <summary>
/// Control lift using right hand Meta joystick with working trajectory format
/// Matches the exact format of the working manual ros2 action send_goal command
/// </summary>
public class fixmovelift : MonoBehaviour
{
    [Header("ROS2 Settings")]
    [Tooltip("Topic to publish lift commands (action goal topic)")]
    public string liftCommandTopic = "/stretch_controller/follow_joint_trajectory/goal";

    [Header("GameObject Reference")]
    [Tooltip("The Transform or GameObject to move up/down in Unity (assign link_lift)")]
    public Transform LiftLink;
    public GameObject Joint_Lift; // Alternative: GameObject reference (like ros2LiftByJoy)

    [Header("Joystick Input")]
    [Tooltip("Right hand joystick InputActionReference for lift control")]
    public InputActionReference rightHandJoystick;

    [Header("Movement Settings")]
    [Tooltip("Movement speed (meters per second)")]
    public float liftSpeed = 0.1f; // m/s

    [Header("Lift Limits (meters)")]
    [Tooltip("Minimum lift position (meters)")]
    public float liftMinPosition = 0.5f;

    [Tooltip("Maximum lift position (meters)")]
    public float liftMaxPosition = 1.1f;

    [Header("Trajectory Settings")]
    [Tooltip("Trajectory duration (seconds) - fixed 2s like working manual command")]
    public float duration = 2.0f;

    [Header("Publishing Settings")]
    [Tooltip("Minimum time between publishes (seconds)")]
    public float publishInterval = 0.2f;

    [Tooltip("Minimum position change to trigger publish (meters)")]
    public float minPositionChange = 0.01f;

    [Header("Debug")]
    public bool showDebugLogs = true;

    // ROS2 components
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<JointTrajectory> liftPublisher;
    private bool isInitialized = false;

    // Internal state
    private float currentLiftPosition = 0.5f; // Current lift position
    private float lastPublishedPosition = 0.5f;
    private float lastPublishTime = 0.0f;

    void Start()
    {
        if (rightHandJoystick == null)
        {
            Debug.LogError("fixmovelift: Right hand joystick InputActionReference not assigned!");
            return;
        }

        // Initialize Unity visualization position
        if (LiftLink != null)
        {
            currentLiftPosition = LiftLink.position.y;
            currentLiftPosition = Mathf.Clamp(currentLiftPosition, liftMinPosition, liftMaxPosition);
        }
        else if (Joint_Lift != null)
        {
            currentLiftPosition = Joint_Lift.transform.localPosition.y;
            currentLiftPosition = Mathf.Clamp(currentLiftPosition, liftMinPosition, liftMaxPosition);
        }

        lastPublishedPosition = currentLiftPosition;

        // Initialize ROS2
        ros2Unity = GetComponent<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            ros2Unity = FindObjectOfType<ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                Debug.LogError("fixmovelift: ROS2UnityComponent not found! Add ROS2UnityCore to scene.");
                return;
            }
        }

        if (showDebugLogs)
        {
            Debug.Log("fixmovelift: Initialized");
            Debug.Log($"fixmovelift: Lift limits: [{liftMinPosition:F2}, {liftMaxPosition:F2}] meters");
            Debug.Log($"fixmovelift: Using working trajectory format (zero timestamp, empty arrays)");
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

        if (!isInitialized || liftPublisher == null)
            return;

        // Get joystick input (y-axis for up/down movement)
        Vector2 joystickInput = rightHandJoystick.action.ReadValue<Vector2>();
        float verticalInput = joystickInput.y; // y-axis is up/down on joystick

        // Update target position based on input
        float previousPosition = currentLiftPosition;
        currentLiftPosition += verticalInput * liftSpeed * UnityEngine.Time.deltaTime;
        currentLiftPosition = Mathf.Clamp(currentLiftPosition, liftMinPosition, liftMaxPosition);

        // Update Unity visualization
        if (LiftLink != null)
        {
            LiftLink.position = new Vector3(
                LiftLink.position.x,
                currentLiftPosition,
                LiftLink.position.z
            );
        }
        else if (Joint_Lift != null)
        {
            Joint_Lift.transform.localPosition = new Vector3(
                Joint_Lift.transform.localPosition.x,
                currentLiftPosition,
                Joint_Lift.transform.localPosition.z
            );
        }

        // Publish to robot with throttling
        if (Mathf.Abs(verticalInput) > 0.01f)
        {
            float timeSinceLastPublish = UnityEngine.Time.time - lastPublishTime;
            float positionChange = Mathf.Abs(currentLiftPosition - lastPublishedPosition);

            if (timeSinceLastPublish >= publishInterval && positionChange >= minPositionChange)
            {
                SendCommand(currentLiftPosition);
                lastPublishedPosition = currentLiftPosition;
                lastPublishTime = UnityEngine.Time.time;
            }
        }

        if (showDebugLogs && Mathf.Abs(verticalInput) > 0.01f)
        {
            Debug.Log($"fixmovelift: Joystick Input: {verticalInput:F2} | Position: {currentLiftPosition:F3}m");
        }
    }

    void InitializeROS2()
    {
        try
        {
            // Create ROS2 node
            ros2Node = ros2Unity.CreateNode("fixmovelift_node");

            // Create publisher for lift commands
            liftPublisher = ros2Node.CreatePublisher<JointTrajectory>(liftCommandTopic);

            if (showDebugLogs)
            {
                Debug.Log($"fixmovelift: ROS2 initialized successfully!");
                Debug.Log($"fixmovelift: Publisher created - {liftCommandTopic}");
            }

            isInitialized = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"fixmovelift: Failed to initialize ROS2: {e.Message}");
            Debug.LogError($"Stack trace: {e.StackTrace}");
        }
    }

    /// <summary>
    /// Send command matching the exact format of the working manual command
    /// This is the KEY difference - uses zero timestamp and empty arrays
    /// </summary>
    void SendCommand(float position)
    {
        if (liftPublisher == null || !isInitialized)
            return;

        var trajectory = new JointTrajectory();

        // Set header - matching manual command format (empty frame_id, zero stamp)
        // THIS IS THE KEY DIFFERENCE from syncliftbyjoy.cs!
        trajectory.Header = new std_msgs.msg.Header
        {
            Stamp = new builtin_interfaces.msg.Time
            {
                Sec = 0,
                Nanosec = 0
            },
            Frame_id = "" // Empty frame_id like manual command (not "base_link")
        };

        // Set joint name (only lift joint) - matching manual command
        trajectory.Joint_names = new string[] { "joint_lift" };

        // Create trajectory point - matching exact manual command format
        // Manual command shows: positions: [0.8], time_from_start: sec: 2, nanosec: 0
        // velocities, accelerations, effort are empty arrays in manual command
        var point = new JointTrajectoryPoint
        {
            Positions = new double[] { position },
            // Empty arrays to match manual command exactly (not arrays with 0.0 values!)
            Velocities = new double[0], // Empty array - KEY DIFFERENCE
            Accelerations = new double[0], // Empty array - KEY DIFFERENCE
            Effort = new double[0], // Empty array - KEY DIFFERENCE
            Time_from_start = new builtin_interfaces.msg.Duration
            {
                Sec = (int)duration,
                Nanosec = 0 // Exactly 0 like manual command (not calculated)
            }
        };

        trajectory.Points = new JointTrajectoryPoint[] { point };

        // Publish trajectory
        liftPublisher.Publish(trajectory);

        if (showDebugLogs)
        {
            Debug.Log($"fixmovelift: Sent command - joint_lift={position:F3}m, duration={duration:F1}s");
        }
    }
}
