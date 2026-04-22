using UnityEngine;
using UnityEngine.InputSystem;
using ROS2;
using trajectory_msgs.msg;
using builtin_interfaces.msg;

/// <summary>
/// BEST SYNCHRONIZATION: Optimized lift control for perfect sync between real Stretch3 and Unity
/// 
/// Based on Stretch robot specifications:
/// - Joint states published at 30 Hz (stretch_driver rate = 30.0 Hz)
/// - Commands should be sent at 20 Hz (matching gamepad teleop)
/// - Unity FixedUpdate set to 30 Hz (0.033s) to match joint_states rate
/// 
/// Optimizations:
/// 1. FixedUpdate() at 30 Hz - matches joint_states publish rate
/// 2. Velocity-based prediction - compensates for network latency
/// 3. High stiffness ArticulationBody - faster response
/// 4. Instant sync - no interpolation delay
/// 5. Process only latest message - discard old data
/// 6. Optimized for minimal latency
/// </summary>
public class Xbestsynclift : MonoBehaviour
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

    [Header("ArticulationBody Drive Settings (Optimized for Best Sync)")]
    [Tooltip("Stiffness for ArticulationBody drive - HIGH for best sync (50000+ recommended)")]
    public float stiffness = 50000f; // Increased from 10000 for faster response

    [Tooltip("Damping for ArticulationBody drive - Balanced for smooth but responsive")]
    public float damping = 2000f; // Slightly increased for stability

    [Tooltip("Force limit for ArticulationBody drive")]
    public float forceLimit = 200f; // Increased for faster movements

    [Header("Movement Settings")]
    [Tooltip("Movement speed (meters per second) - Higher = faster robot responsiveness")]
    public float liftSpeed = 0.2f; // m/s

    [Header("Trajectory Settings")]
    [Tooltip("Trajectory duration (seconds) - Lower = faster robot movement")]
    public float duration = 0.5f;

    [Header("Publishing Settings")]
    [Tooltip("Minimum time between publishes (seconds) - Match gamepad teleop: 0.05s = 20 Hz")]
    public float publishInterval = 0.05f; // 20 Hz - matches gamepad teleop

    [Tooltip("Minimum position change to trigger publish (meters)")]
    public float minPublishPositionChange = 0.002f;

    [Header("Sync Settings (Optimized)")]
    [Tooltip("Enable velocity-based prediction to compensate for network latency")]
    public bool useVelocityPrediction = true;

    [Tooltip("Estimated network latency (seconds) - used for velocity prediction")]
    public float estimatedLatency = 0.05f; // 50ms typical network latency

    [Header("Lift Limits (meters)")]
    [Tooltip("Minimum lift position (meters) - clamp to this range")]
    public float liftMinPosition = 0.3f;

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

    // Velocity-based prediction
    private float currentVelocity = 0.0f; // Will use velocity from JointState message (more accurate)

    void Start()
    {
        // Set Unity Fixed Timestep to 30 Hz (0.033s) to match joint_states rate
        // This ensures FixedUpdate() runs at the same rate as joint_states updates
        UnityEngine.Time.fixedDeltaTime = 1.0f / 30.0f; // 30 Hz = 0.033s

        if (rightHandJoystick == null)
        {
            Debug.LogError("Xbestsynclift: Right hand joystick InputActionReference not assigned!");
        }

        // Auto-find LiftSub component if enabled
        if (autoFindLiftSub && liftSubscriber == null)
        {
            liftSubscriber = FindObjectOfType<LiftSub>();
            if (liftSubscriber != null && showDebugLogs)
            {
                Debug.Log("Xbestsynclift: Auto-found LiftSub component");
            }
        }

        // Validate LiftSub is found
        if (liftSubscriber == null)
        {
            Debug.LogError("Xbestsynclift: LiftSub component not found! Assign in Inspector or enable auto-find.");
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
                Debug.LogError("Xbestsynclift: ROS2UnityComponent not found! Add ROS2UnityCore to scene.");
            }
        }

        // Velocity will be read from JointState message (more accurate than calculating from deltas)

        if (showDebugLogs)
        {
            Debug.Log("Xbestsynclift: Initialized with BEST SYNC optimizations");
            Debug.Log($"Xbestsynclift: FixedUpdate rate set to 30 Hz (matches joint_states rate)");
            Debug.Log($"Xbestsynclift: Lift limits: [{liftMinPosition:F2}, {liftMaxPosition:F2}] meters");
            Debug.Log($"Xbestsynclift: Using ArticulationBody X Drive with HIGH stiffness ({stiffness})");
            if (liftSubFound)
            {
                Debug.Log($"Xbestsynclift: LiftSub found - will sync Unity from /stretch/joint_states");
            }
            else
            {
                Debug.LogError("Xbestsynclift: LiftSub not found!");
            }
            if (liftFound)
            {
                Debug.Log($"Xbestsynclift: Lift ArticulationBody found: {liftArticulation.transform.name}");
            }
            else
            {
                Debug.LogError("Xbestsynclift: Lift ArticulationBody not found!");
            }
            if (useVelocityPrediction)
            {
                Debug.Log($"Xbestsynclift: Velocity-based prediction ENABLED (latency: {estimatedLatency * 1000:F0}ms)");
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

        // Handle joystick input and send commands to robot (in Update for responsive input)
        if (rightHandJoystick != null && isInitialized && liftPublisher != null)
        {
            HandleJoystickInput();
        }
    }

    /// <summary>
    /// BEST SYNC: Use FixedUpdate() at 30 Hz to match joint_states publish rate
    /// This ensures consistent, predictable updates aligned with robot's state updates
    /// </summary>
    void FixedUpdate()
    {
        // Sync Unity visualization with real robot position
        // FixedUpdate() runs at 30 Hz, matching joint_states rate
        if (liftSubFound && liftFound)
        {
            SyncUnityVisualization();
        }
    }

    /// <summary>
    /// Handle joystick input and send commands to real robot
    /// Runs in Update() for responsive input handling
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

            // Publish to robot with throttling (20 Hz - matches gamepad teleop)
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
                    Debug.Log($"Xbestsynclift: Sent command - joint_lift={commandPosition:F3}m");
                }
            }
        }
    }

    /// <summary>
    /// BEST SYNC: Sync Unity visualization with real robot position using velocity prediction
    /// Runs in FixedUpdate() at 30 Hz to match joint_states rate
    /// </summary>
    void SyncUnityVisualization()
    {
        // Check if LiftSub has received any messages from robot
        if (!liftSubscriber.HasReceivedMessage())
        {
            // Wait for first message - don't sync until we have real robot data
            return;
        }

        // Get real robot position and velocity from LiftSub
        // Using actual velocity from JointState message (more accurate than calculating from deltas)
        float realRobotPosition = liftSubscriber.GetCurrentJointPosition();
        currentVelocity = liftSubscriber.GetCurrentJointVelocity(); // Get actual velocity from message

        // Clamp to valid range
        realRobotPosition = Mathf.Clamp(realRobotPosition, liftMinPosition, liftMaxPosition);

        // Velocity-based prediction: predict future position to compensate for network latency
        float predictedPosition = realRobotPosition;
        if (useVelocityPrediction)
        {
            // Use actual velocity from JointState message to predict where robot will be after latency
            predictedPosition = realRobotPosition + (currentVelocity * estimatedLatency);
            predictedPosition = Mathf.Clamp(predictedPosition, liftMinPosition, liftMaxPosition);
        }

        // Update target position for Unity visualization
        targetLiftPosition = predictedPosition;

        // INSTANT SYNC - no interpolation delay (best for synchronization)
        currentLiftPosition = targetLiftPosition;

        // Always update ArticulationBody every FixedUpdate() for best synchronization
        // No position change threshold - replicates all movements, no matter how small
        UpdateArticulationDrive();

        if (showDebugLogs && UnityEngine.Time.frameCount % 180 == 0) // Log every 3 seconds at 60fps
        {
            Debug.Log($"Xbestsynclift: Synced - Real: {realRobotPosition:F3}m | Predicted: {predictedPosition:F3}m | Velocity: {currentVelocity:F3}m/s | Unity: {currentLiftPosition:F3}m");
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
                    Debug.Log($"Xbestsynclift: Auto-found liftArticulation on {liftObj.name}");
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
            Debug.LogError("Xbestsynclift: Missing or invalid ArticulationBody component!");
            Debug.LogError("Xbestsynclift: Assign lift ArticulationBody in Inspector or enable auto-find!");
            if (liftArticulation != null)
            {
                Debug.LogError($"Xbestsynclift: Found ArticulationBody but wrong type: {liftArticulation.jointType} (expected PrismaticJoint)");
            }
        }
    }

    /// <summary>
    /// Initialize ArticulationBody drive parameters with HIGH stiffness for best sync
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
            Debug.Log($"Xbestsynclift: Initialized drive - Stiffness: {stiffness}, Damping: {damping}, ForceLimit: {forceLimit}");
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

        if (showDebugLogs)
        {
            Debug.Log($"Xbestsynclift: Loaded current position: {currentLiftPosition:F3}m");
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
    /// Called every FixedUpdate() for best synchronization
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
            ros2Node = ros2Unity.CreateNode("xbestsynclift_node");

            // Create publisher for lift commands
            liftPublisher = ros2Node.CreatePublisher<JointTrajectory>(liftCommandTopic);

            if (showDebugLogs)
            {
                Debug.Log($"Xbestsynclift: ROS2 initialized successfully!");
                Debug.Log($"Xbestsynclift: Publisher created - {liftCommandTopic}");
            }

            isInitialized = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Xbestsynclift: Failed to initialize ROS2: {e.Message}");
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
