using UnityEngine;
using UnityEngine.InputSystem;
using ROS2;
using trajectory_msgs.msg;
using builtin_interfaces.msg;

/// <summary>
/// Control lift joint using ArticulationBody X Drive (most accurate)
/// Uses right hand Meta joystick Y-axis (up/down) to move lift up/down
/// Publishes JointTrajectory to real Stretch3 robot via ROS2
/// Matches real robot movement exactly - uses Unity physics simulation
/// </summary>
public class XdriveLift : MonoBehaviour
{
    [Header("ROS2 Settings")]
    [Tooltip("Topic to publish lift commands (action goal topic)")]
    public string liftCommandTopic = "/stretch_controller/follow_joint_trajectory/goal";

    [Header("ArticulationBody Reference")]
    [Tooltip("ArticulationBody for link_lift - REQUIRED for lift control")]
    public ArticulationBody liftArticulation;

    [Header("Auto-Find Settings")]
    [Tooltip("Automatically find ArticulationBody component by GameObject name")]
    public bool autoFindArticulation = true;

    [Header("Joystick Input")]
    [Tooltip("Right hand joystick InputActionReference for lift control")]
    public InputActionReference rightHandJoystick;

    [Header("Movement Settings")]
    [Tooltip("Movement speed (meters per second) - Higher = faster Unity responsiveness")]
    public float liftSpeed = 0.2f; // m/s

    [Header("Lift Limits (meters)")]
    [Tooltip("Minimum lift position (meters) - from URDF: 0.0")]
    public float liftMinPosition = 0.30f;

    [Tooltip("Maximum lift position (meters) - from URDF: 1.1")]
    public float liftMaxPosition = 1.0f;

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
    private IPublisher<JointTrajectory> liftPublisher;
    private bool isInitialized = false;

    // Internal state
    private float currentLiftPosition = 0.0f; // Current lift position (0.0 = down, 1.1 = up)
    private float lastPublishedPosition = 0.0f;
    private float lastPublishTime = 0.0f;

    // Track if articulation is found
    private bool liftFound = false;

    void Start()
    {
        if (rightHandJoystick == null)
        {
            Debug.LogError("XdriveLift: Right hand joystick InputActionReference not assigned!");
            return;
        }

        // Auto-find ArticulationBody component if enabled
        if (autoFindArticulation)
        {
            FindLiftArticulation();
        }

        // Validate articulation is found
        ValidateArticulation();

        // Initialize ArticulationBody drive parameters
        if (liftFound)
        {
            InitializeDrive();
        }

        // Load current position from ArticulationBody
        if (liftFound)
        {
            LoadCurrentPosition();
        }

        lastPublishedPosition = currentLiftPosition;

        // Initialize ROS2
        ros2Unity = GetComponent<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            ros2Unity = FindObjectOfType<ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                Debug.LogError("XdriveLift: ROS2UnityComponent not found! Add ROS2UnityCore to scene.");
                return;
            }
        }

        if (showDebugLogs)
        {
            Debug.Log("XdriveLift: Initialized");
            Debug.Log($"XdriveLift: Lift limits: [{liftMinPosition:F2}, {liftMaxPosition:F2}] meters");
            Debug.Log($"XdriveLift: Using ArticulationBody X Drive (most accurate visualization)");
            Debug.Log($"XdriveLift: Will send joint_lift trajectory command");
            if (liftFound)
            {
                Debug.Log($"XdriveLift: ✓ Lift ArticulationBody found: {liftArticulation.transform.name}");
            }
            else
            {
                Debug.LogError("XdriveLift: ✗ Lift ArticulationBody not found! Assign in Inspector or enable auto-find.");
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

        if (!isInitialized || liftPublisher == null)
            return;

        // Get joystick input (y-axis for up/down movement)
        Vector2 joystickInput = rightHandJoystick.action.ReadValue<Vector2>();
        float verticalInput = joystickInput.y; // y-axis is up/down on joystick

        // Update target position based on input
        // Up (positive y) = lift up, Down (negative y) = lift down
        float previousPosition = currentLiftPosition;
        currentLiftPosition += verticalInput * liftSpeed * UnityEngine.Time.deltaTime;
        currentLiftPosition = Mathf.Clamp(currentLiftPosition, liftMinPosition, liftMaxPosition);

        // Update ArticulationBody X Drive target (Unity visualization)
        if (liftFound)
        {
            UpdateArticulationDrive();
        }

        // Publish to robot with throttling
        if (Mathf.Abs(verticalInput) > 0.01f)
        {
            float timeSinceLastPublish = UnityEngine.Time.time - lastPublishTime;
            float positionChange = Mathf.Abs(currentLiftPosition - lastPublishedPosition);

            // Rate limiting: Only publish if enough time passed AND position changed significantly
            if (timeSinceLastPublish >= publishInterval && positionChange >= minPositionChange)
            {
                SendCommand(currentLiftPosition);
                lastPublishedPosition = currentLiftPosition;
                lastPublishTime = UnityEngine.Time.time;
            }
        }

        if (showDebugLogs && Mathf.Abs(verticalInput) > 0.01f && UnityEngine.Time.frameCount % 60 == 0)
        {
            Debug.Log($"XdriveLift: Joystick Input: {verticalInput:F2} | Position: {currentLiftPosition:F3}m");
        }
    }

    /// <summary>
    /// Auto-find ArticulationBody component by GameObject name
    /// </summary>
    void FindLiftArticulation()
    {
        if (liftArticulation == null)
        {
            GameObject liftObj = GameObject.Find("link_lift");
            if (liftObj != null)
            {
                liftArticulation = liftObj.GetComponent<ArticulationBody>();
                if (liftArticulation != null && showDebugLogs)
                {
                    Debug.Log($"XdriveLift: Auto-found liftArticulation on {liftObj.name}");
                }
            }
        }
    }

    /// <summary>
    /// Validate that ArticulationBody is found and is a prismatic joint
    /// </summary>
    void ValidateArticulation()
    {
        liftFound = liftArticulation != null && liftArticulation.jointType == ArticulationJointType.PrismaticJoint;

        if (!liftFound)
        {
            Debug.LogError("XdriveLift: Missing or invalid ArticulationBody component!");
            Debug.LogError("XdriveLift: Assign lift ArticulationBody in Inspector or enable auto-find!");
            if (liftArticulation != null)
            {
                Debug.LogError($"XdriveLift: Found ArticulationBody but wrong type: {liftArticulation.jointType} (expected PrismaticJoint)");
            }
        }
    }

    /// <summary>
    /// Initialize ArticulationBody drive parameters
    /// </summary>
    void InitializeDrive()
    {
        if (liftArticulation == null) return;

        ArticulationDrive drive = liftArticulation.xDrive;
        drive.stiffness = stiffness;
        drive.damping = damping;
        drive.forceLimit = forceLimit;
        liftArticulation.xDrive = drive;

        if (showDebugLogs)
        {
            Debug.Log($"XdriveLift: Initialized drive - Stiffness: {stiffness}, Damping: {damping}, ForceLimit: {forceLimit}");
        }
    }

    /// <summary>
    /// Load current position from ArticulationBody
    /// </summary>
    void LoadCurrentPosition()
    {
        if (liftArticulation == null) return;

        currentLiftPosition = GetPrismaticPosition(liftArticulation);
        currentLiftPosition = Mathf.Clamp(currentLiftPosition, liftMinPosition, liftMaxPosition);

        if (showDebugLogs)
        {
            Debug.Log($"XdriveLift: Loaded current position: {currentLiftPosition:F3}m");
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
    /// Update ArticulationBody X Drive target (Unity visualization)
    /// </summary>
    void UpdateArticulationDrive()
    {
        if (liftArticulation == null) return;

        ArticulationDrive drive = liftArticulation.xDrive;
        drive.target = currentLiftPosition; // Target in meters for prismatic joints
        liftArticulation.xDrive = drive;
    }

    void InitializeROS2()
    {
        try
        {
            // Create ROS2 node
            ros2Node = ros2Unity.CreateNode("xdrivelift_node");

            // Create publisher for lift commands
            liftPublisher = ros2Node.CreatePublisher<JointTrajectory>(liftCommandTopic);

            if (showDebugLogs)
            {
                Debug.Log($"XdriveLift: ROS2 initialized successfully!");
                Debug.Log($"XdriveLift: Publisher created - {liftCommandTopic}");
            }

            isInitialized = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"XdriveLift: Failed to initialize ROS2: {e.Message}");
            Debug.LogError($"Stack trace: {e.StackTrace}");
        }
    }

    /// <summary>
    /// Send command matching the exact format of the working manual command
    /// This uses zero timestamp and empty arrays like fixmovelift.cs
    /// </summary>
    void SendCommand(float position)
    {
        if (liftPublisher == null || !isInitialized)
            return;

        // Clamp position to valid range
        position = Mathf.Clamp(position, liftMinPosition, liftMaxPosition);

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
                Sec = (int)duration, // duration controls real robot movement speed (lower = faster)
                Nanosec = 0 // Exactly 0 like manual command (not calculated)
            }
        };

        trajectory.Points = new JointTrajectoryPoint[] { point };

        // Publish trajectory
        liftPublisher.Publish(trajectory);

        if (showDebugLogs)
        {
            Debug.Log($"XdriveLift: Sent command - joint_lift={position:F3}m, duration={duration:F1}s");
        }
    }
}
