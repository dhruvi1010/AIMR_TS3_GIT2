using UnityEngine;
using UnityEngine.InputSystem;
using ROS2;
using trajectory_msgs.msg;
using builtin_interfaces.msg;

/// <summary>
/// Synchronized lift control: Joystick input controls real robot, Unity visualization syncs with real robot position
/// Uses ArticulationBody X Drive method for accurate physics-based visualization
/// - Joystick input → Sends commands to real robot via ROS2
/// - Real robot position from LiftSub → Syncs Unity visualization
/// This ensures Unity always shows what the robot is actually doing (perfect sync)
/// </summary>
public class XsyncLIft : MonoBehaviour
{
    [Header("ROS2 Settings")]
    [Tooltip("Topic to publish lift commands (action goal topic)")]
    public string liftCommandTopic = "/stretch_controller/follow_joint_trajectory/goal";

    [Header("LiftSub Reference")]
    [Tooltip("LiftSub component that subscribes to /stretch/joint_states - REQUIRED")]
    public LiftSub liftSubscriber;

    [Header("ArticulationBody Reference")]
    [Tooltip("ArticulationBody for link_lift - REQUIRED for lift visualization")]
    public ArticulationBody liftArticulation;

    [Header("Auto-Find Settings")]
    [Tooltip("Automatically find LiftSub component in scene")]
    public bool autoFindLiftSub = true;

    [Tooltip("Automatically find ArticulationBody component by GameObject name")]
    public bool autoFindArticulation = true;

    [Header("Joystick Input")]
    [Tooltip("Right hand joystick InputActionReference for lift control")]
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
    public float liftSpeed = 0.2f; // m/s
    
    [Header("Trajectory Settings")]
    [Tooltip("Trajectory duration (seconds) - Lower = faster robot movement")]
    public float duration = 0.5f;

    [Header("Publishing Settings")]
    [Tooltip("Minimum time between publishes (seconds) - Match gamepad teleop: 0.05s = 20 Hz")]
    public float publishInterval = 0.05f;

    [Tooltip("Minimum position change to trigger publish (meters)")]
    public float minPublishPositionChange = 0.002f;

    [Header("Lift Limits (meters)")]
    [Tooltip("Minimum lift position (meters) - clamp to this range")]
    public float liftMinPosition = 0.0f;

    [Tooltip("Maximum lift position (meters) - clamp to this range")]
    public float liftMaxPosition = 1.1f;

    [Header("Debug")]
    public bool showDebugLogs = true;

    // ROS2 components
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<JointTrajectory> liftPublisher;
    private bool isInitialized = false;

    // Internal state
    private float targetLiftPosition = 0.0f; // Target position from real robot (for Unity visualization)
    private float currentLiftPosition = 0.0f; // Current Unity visualization position
    private float commandPosition = 0.0f; // Position to send to robot (from joystick)
    private float lastPublishedPosition = 0.0f;
    private float lastPublishTime = 0.0f;
    private bool liftFound = false;
    private bool liftSubFound = false;
    private float lastSyncedPosition = 0.0f;

    void Start()
    {
        if (rightHandJoystick == null)
        {
            Debug.LogError("XsyncLIft: Right hand joystick InputActionReference not assigned!");
        }

        // Auto-find LiftSub component if enabled
        if (autoFindLiftSub && liftSubscriber == null)
        {
            liftSubscriber = FindObjectOfType<LiftSub>();
            if (liftSubscriber != null && showDebugLogs)
            {
                Debug.Log("XsyncLIft: Auto-found LiftSub component");
            }
        }

        // Validate LiftSub is found
        if (liftSubscriber == null)
        {
            Debug.LogError("XsyncLIft: LiftSub component not found! Assign in Inspector or enable auto-find.");
        }
        else
        {
            liftSubFound = true;
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
            LoadCurrentPosition();
        }

        // Initialize ROS2
        ros2Unity = GetComponent<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            ros2Unity = FindObjectOfType<ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                Debug.LogError("XsyncLIft: ROS2UnityComponent not found! Add ROS2UnityCore to scene.");
            }
        }

        if (showDebugLogs)
        {
            Debug.Log("XsyncLIft: Initialized");
            Debug.Log($"XsyncLIft: Lift limits: [{liftMinPosition:F2}, {liftMaxPosition:F2}] meters");
            Debug.Log($"XsyncLIft: Using ArticulationBody X Drive for synchronized control");
            if (liftSubFound)
            {
                Debug.Log($"XsyncLIft: LiftSub found - will sync Unity from /stretch/joint_states");
            }
            else
            {
                Debug.LogError("XsyncLIft:  LiftSub not found!");
            }
            if (liftFound)
            {
                Debug.Log($"XsyncLIft:  Lift ArticulationBody found: {liftArticulation.transform.name}");
            }
            else
            {
                Debug.LogError("XsyncLIft:  Lift ArticulationBody not found!");
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
        }

        // Handle joystick input and send commands to robot
        if (rightHandJoystick != null && isInitialized && liftPublisher != null)
        {
            HandleJoystickInput();
        }

        // Sync Unity visualization with real robot position
        if (liftSubFound && liftFound)
        {
            SyncUnityVisualization();
        }
    }

    /// <summary>
    /// Handle joystick input and send commands to real robot
    /// </summary>
    void HandleJoystickInput()
    {
        // Get joystick input (y-axis for up/down movement)
        Vector2 joystickInput = rightHandJoystick.action.ReadValue<Vector2>();
        float verticalInput = joystickInput.y; // y-axis is up/down on joystick

        // Update command position based on input
        // Up (positive y) = lift up, Down (negative y) = lift down
        if (Mathf.Abs(verticalInput) > 0.01f)
        {
            commandPosition += verticalInput * liftSpeed * UnityEngine.Time.deltaTime;
            commandPosition = Mathf.Clamp(commandPosition, liftMinPosition, liftMaxPosition);

            // Publish to robot with throttling
            float timeSinceLastPublish = UnityEngine.Time.time - lastPublishTime;
            float positionChange = Mathf.Abs(commandPosition - lastPublishedPosition);

            // Rate limiting: Only publish if enough time passed AND position changed significantly
            if (timeSinceLastPublish >= publishInterval && positionChange >= minPublishPositionChange)
            {
                SendCommand(commandPosition);
                lastPublishedPosition = commandPosition;
                lastPublishTime = UnityEngine.Time.time;

                if (showDebugLogs && UnityEngine.Time.frameCount % 60 == 0)
                {
                    Debug.Log($"XsyncLIft: Sent command - joint_lift={commandPosition:F3}m");
                }
            }
        }
    }

    /// <summary>
    /// Sync Unity visualization with real robot position from LiftSub
    /// </summary>
    void SyncUnityVisualization()
    {
        // Check if LiftSub has received any messages from robot
        if (!liftSubscriber.HasReceivedMessage())
        {
            // Wait for first message - don't sync until we have real robot data
            return;
        }

        // Get real robot position from LiftSub
        float realRobotPosition = liftSubscriber.GetCurrentJointPosition();

        // Clamp to valid range
        realRobotPosition = Mathf.Clamp(realRobotPosition, liftMinPosition, liftMaxPosition);

        // Update target position for Unity visualization
        targetLiftPosition = realRobotPosition;

        // Instant sync - Unity visualization immediately matches real robot position
        currentLiftPosition = targetLiftPosition;

        // Always update ArticulationBody every frame for best synchronization
        // No position change threshold - replicates all movements, no matter how small
        UpdateArticulationDrive();
        lastSyncedPosition = currentLiftPosition;

        if (showDebugLogs && UnityEngine.Time.frameCount % 60 == 0) // Log once per second
        {
            Debug.Log($"XsyncLIft: Synced - Real Robot: {realRobotPosition:F3}m | Unity: {currentLiftPosition:F3}m | Command: {commandPosition:F3}m");
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
                    Debug.Log($"XsyncLIft: Auto-found liftArticulation on {liftObj.name}");
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
            Debug.LogError("XsyncLIft: Missing or invalid ArticulationBody component!");
            Debug.LogError("XsyncLIft: Assign lift ArticulationBody in Inspector or enable auto-find!");
            if (liftArticulation != null)
            {
                Debug.LogError($"XsyncLIft: Found ArticulationBody but wrong type: {liftArticulation.jointType} (expected PrismaticJoint)");
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
            Debug.Log($"XsyncLIft: Initialized drive - Stiffness: {stiffness}, Damping: {damping}, ForceLimit: {forceLimit}");
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
        targetLiftPosition = currentLiftPosition;
        lastSyncedPosition = currentLiftPosition;

        if (showDebugLogs)
        {
            Debug.Log($"XsyncLIft: Loaded current position: {currentLiftPosition:F3}m");
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
            ros2Node = ros2Unity.CreateNode("xsynclift_node");

            // Create publisher for lift commands
            liftPublisher = ros2Node.CreatePublisher<JointTrajectory>(liftCommandTopic);

            if (showDebugLogs)
            {
                Debug.Log($"XsyncLIft: ROS2 initialized successfully!");
                Debug.Log($"XsyncLIft: Publisher created - {liftCommandTopic}");
            }

            isInitialized = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"XsyncLIft: Failed to initialize ROS2: {e.Message}");
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
    }
}
