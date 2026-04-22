using UnityEngine;
using UnityEngine.InputSystem;
using ROS2;
using trajectory_msgs.msg;
using builtin_interfaces.msg;

/// <summary>
/// Synchronized gripper control: Button input controls real robot, Unity visualization syncs with real robot position
/// Uses ArticulationBody X Drive method for accurate physics-based visualization
/// - Button input → Sends commands to real robot via ROS2
/// - Real robot position from StretchJointStateSub → Syncs Unity visualization
/// This ensures Unity always shows what the robot is actually doing (perfect sync)
/// </summary>
public class xdrivegripper : MonoBehaviour
{
    [Header("ROS2 Settings")]
    [Tooltip("Topic to publish gripper commands (action goal topic)")]
    public string gripperCommandTopic = "/stretch_controller/follow_joint_trajectory/goal";

    [Header("StretchJointStateSub Reference")]
    [Tooltip("StretchJointStateSub component that subscribes to /stretch/joint_states - REQUIRED")]
    public StretchJointStateSub jointStateSubscriber;

    [Header("ArticulationBody Reference (Optional)")]
    [Tooltip("ArticulationBody for link_gripper_finger_left - Optional, will use Transform manipulation if not set")]
    public ArticulationBody gripperArticulation;

    [Header("GameObject References - Gripper Fingers (Alternative to ArticulationBody)")]
    [Tooltip("Left gripper finger Transform - REQUIRED if ArticulationBody not used")]
    public Transform GripperFingerLeft;
    
    [Tooltip("Right gripper finger Transform - REQUIRED if ArticulationBody not used")]
    public Transform GripperFingerRight;

    [Header("Unity Visualization Settings (for Transform manipulation)")]
    [Tooltip("Movement axis in Unity: 0=X, 1=Y, 2=Z. Or use -1 for transform.right direction")]
    public int movementAxis = 2; // 0=X (left/right), -1 = use transform.right

    [Tooltip("Maximum finger separation distance (meters) - how far fingers move apart when fully open")]
    public float maxFingerSeparation = 0.1f; // 10cm separation when fully open

    [Header("Auto-Find Settings")]
    [Tooltip("Automatically find StretchJointStateSub component in scene")]
    public bool autoFindJointStateSub = true;

    [Tooltip("Automatically find ArticulationBody component by GameObject name")]
    public bool autoFindArticulation = true;

    [Header("Joystick Input")]
    [Tooltip("Right hand gripper button InputActionReference for gripper control")]
    public InputActionReference gripperButton;

    [Header("ArticulationBody Drive Settings")]
    [Tooltip("Stiffness for ArticulationBody drive (higher = stiffer, more responsive) - Typical range: 100-1000 for revolute joints")]
    public float stiffness = 500f;

    [Tooltip("Damping for ArticulationBody drive (higher = more damped, smoother) - Typical range: 10-100 for revolute joints")]
    public float damping = 50f;

    [Tooltip("Force limit for ArticulationBody drive (torque limit in N⋅m) - Typical range: 10-50 for revolute joints")]
    public float forceLimit = 20f;

    [Header("Gripper Settings")]
    [Tooltip("Gripper joint name to use (joint_gripper_finger_left)")]
    public string gripperJointName = "joint_gripper_finger_left";

    [Header("Gripper Limits (radians)")]
    [Tooltip("Gripper position when open (radians) - From URDF: 0.6 rad = ~34.4° fully open")]
    public float gripperOpenPosition = 0.6f;

    [Tooltip("Gripper position when closed (radians) - From URDF: 0.0 rad = fully closed")]
    public float gripperClosedPosition = 0.0f;

    [Tooltip("Gripper minimum position (radians) - From URDF: -0.6 rad = ~-34.4°")]
    public float gripperMinPosition = -0.6f;

    [Tooltip("Gripper maximum position (radians) - From URDF: 0.6 rad = ~34.4°")]
    public float gripperMaxPosition = 0.6f;

    [Header("Trajectory Settings")]
    [Tooltip("Trajectory duration (seconds) - Lower = faster robot movement")]
    public float duration = 2.0f;

    [Header("Publishing Settings")]
    [Tooltip("Minimum time between publishes (seconds) - Match gamepad teleop: 0.05s = 20 Hz")]
    public float publishInterval = 0.2f;

    [Tooltip("Button press threshold (0.0 to 1.0) - button value above this is considered pressed")]
    public float buttonPressThreshold = 0.5f;

    [Header("Debug")]
    public bool showDebugLogs = true;

    // ROS2 components
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<JointTrajectory> gripperPublisher;
    private bool isInitialized = false;

    // Internal state
    private float targetGripperPosition = 0.0f; // Target position from real robot (for Unity visualization)
    private float currentGripperPosition = 0.0f; // Current Unity visualization position
    private float commandPosition = 0.0f; // Position to send to robot (from button)
    private float lastPublishedPosition = 0.0f;
    private float lastPublishTime = 0.0f;
    private bool gripperFound = false;
    private bool jointStateSubFound = false;
    private bool lastButtonState = false; // Last button state to detect changes
    
    // Visualization mode
    private bool useArticulationBody = false; // True if using ArticulationBody, false if using Transform manipulation
    
    // Base positions for gripper fingers (when fully closed) - for Transform manipulation
    private Vector3 baseLocalPositionLeft = Vector3.zero;
    private Vector3 baseLocalPositionRight = Vector3.zero;

    void Start()
    {
        if (gripperButton == null)
        {
            Debug.LogError("xdrivegripper: Gripper button InputActionReference not assigned!");
        }

        // Auto-find StretchJointStateSub component if enabled
        if (autoFindJointStateSub && jointStateSubscriber == null)
        {
            jointStateSubscriber = FindObjectOfType<StretchJointStateSub>();
            if (jointStateSubscriber != null && showDebugLogs)
            {
                Debug.Log("xdrivegripper: Auto-found StretchJointStateSub component");
            }
        }

        // Validate StretchJointStateSub is found
        if (jointStateSubscriber == null)
        {
            Debug.LogError("xdrivegripper: StretchJointStateSub component not found! Assign in Inspector or enable auto-find.");
        }
        else
        {
            jointStateSubFound = true;
        }

        // Auto-find ArticulationBody component if enabled
        if (autoFindArticulation)
        {
            FindGripperArticulation();
        }

        // Validate articulation is found
        ValidateArticulation();

        // Determine visualization mode
        useArticulationBody = (gripperArticulation != null && gripperFound);
        
        // If not using ArticulationBody, check for Transform references
        if (!useArticulationBody)
        {
            bool hasBothFingers = (GripperFingerLeft != null && GripperFingerRight != null);
            if (hasBothFingers)
            {
                // Store base positions for both fingers (when gripper is closed)
                baseLocalPositionLeft = GripperFingerLeft.localPosition;
                baseLocalPositionRight = GripperFingerRight.localPosition;
                if (showDebugLogs)
                {
                    Debug.Log("xdrivegripper: Using Transform manipulation for visualization");
                }
            }
            else
            {
                Debug.LogWarning("xdrivegripper: No ArticulationBody found and no finger Transforms assigned! Visualization will not work.");
            }
        }

        // Initialize ArticulationBody drive parameters (if using ArticulationBody)
        if (useArticulationBody)
        {
            InitializeDrive();
            LoadCurrentPosition();
        }

        // Initialize gripper position to closed
        commandPosition = gripperClosedPosition;
        currentGripperPosition = gripperClosedPosition;
        targetGripperPosition = gripperClosedPosition;

        // Initialize ROS2
        ros2Unity = GetComponent<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            ros2Unity = FindObjectOfType<ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                Debug.LogError("xdrivegripper: ROS2UnityComponent not found! Add ROS2UnityCore to scene.");
            }
        }

        if (showDebugLogs)
        {
            Debug.Log("xdrivegripper: Initialized");
            Debug.Log($"xdrivegripper: Gripper working range: [{gripperClosedPosition:F3}, {gripperOpenPosition:F3}] rad ({gripperClosedPosition * Mathf.Rad2Deg:F1}° to {gripperOpenPosition * Mathf.Rad2Deg:F1}°)");
            Debug.Log($"xdrivegripper: Gripper joint limits: [{gripperMinPosition:F3}, {gripperMaxPosition:F3}] rad ({gripperMinPosition * Mathf.Rad2Deg:F1}° to {gripperMaxPosition * Mathf.Rad2Deg:F1}°)");
            Debug.Log($"xdrivegripper: Using ArticulationBody X Drive for synchronized control");
            Debug.Log($"xdrivegripper: Using joint name: {gripperJointName}");
            if (jointStateSubFound)
            {
                Debug.Log($"xdrivegripper: StretchJointStateSub found - will sync Unity from /stretch/joint_states");
            }
            else
            {
                Debug.LogError("xdrivegripper: StretchJointStateSub not found!");
            }
            if (useArticulationBody)
            {
                Debug.Log($"xdrivegripper: Using ArticulationBody for visualization: {gripperArticulation.transform.name}");
            }
            else if (GripperFingerLeft != null && GripperFingerRight != null)
            {
                Debug.Log($"xdrivegripper: Using Transform manipulation for visualization");
                Debug.Log($"xdrivegripper:   Left: {GripperFingerLeft.name}, Right: {GripperFingerRight.name}");
            }
            else
            {
                Debug.LogError("xdrivegripper: No visualization method available! Assign ArticulationBody or finger Transforms.");
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

        // Handle button input and send commands to robot
        if (gripperButton != null && isInitialized && gripperPublisher != null)
        {
            HandleButtonInput();
        }

        // Sync Unity visualization with real robot position
        if (jointStateSubFound && (useArticulationBody || (GripperFingerLeft != null && GripperFingerRight != null)))
        {
            SyncUnityVisualization();
        }
    }

    /// <summary>
    /// Handle button input and send commands to real robot
    /// </summary>
    void HandleButtonInput()
    {
        // Get gripper button input (0 for released, 1 for pressed)
        float buttonValue = gripperButton.action.ReadValue<float>();
        bool buttonPressed = buttonValue > buttonPressThreshold;

        // Detect button state change (toggle gripper on button press)
        if (buttonPressed && !lastButtonState)
        {
            // Button just pressed - toggle gripper
            ToggleGripper();
            lastPublishTime = UnityEngine.Time.time;
        }

        lastButtonState = buttonPressed;

        if (showDebugLogs && buttonPressed)
        {
            Debug.Log($"xdrivegripper: Button pressed (value: {buttonValue:F2}) | Current position: {currentGripperPosition:F2}");
        }
    }

    /// <summary>
    /// Toggle gripper between open and closed positions
    /// </summary>
    void ToggleGripper()
    {
        // Toggle between open and closed
        // If current position is closer to closed, open it; otherwise close it
        float distanceToClosed = Mathf.Abs(commandPosition - gripperClosedPosition);
        float distanceToOpen = Mathf.Abs(commandPosition - gripperOpenPosition);

        if (distanceToClosed < distanceToOpen)
        {
            // Currently closer to closed, so open it
            OpenGripper();
        }
        else
        {
            // Currently closer to open, so close it
            CloseGripper();
        }
    }

    /// <summary>
    /// Open the gripper
    /// </summary>
    public void OpenGripper()
    {
        // Set to open position
        commandPosition = gripperOpenPosition;
        SendCommand(commandPosition);

        if (showDebugLogs)
        {
            Debug.Log($"xdrivegripper: Opening gripper to position: {commandPosition:F3}");
        }
    }

    /// <summary>
    /// Close the gripper
    /// </summary>
    public void CloseGripper()
    {
        // Set to closed position
        commandPosition = gripperClosedPosition;
        SendCommand(commandPosition);

        if (showDebugLogs)
        {
            Debug.Log($"xdrivegripper: Closing gripper to position: {commandPosition:F3}");
        }
    }

    /// <summary>
    /// Sync Unity visualization with real robot position from StretchJointStateSub
    /// </summary>
    void SyncUnityVisualization()
    {
        // Check if StretchJointStateSub has received any messages from robot
        if (!jointStateSubscriber.HasReceivedMessage())
        {
            // Wait for first message - don't sync until we have real robot data
            if (showDebugLogs && UnityEngine.Time.frameCount % 300 == 0) // Log every 5 seconds
            {
                Debug.LogWarning("xdrivegripper: Waiting for joint_states messages from robot...");
            }
            return;
        }

        // Check if joint exists in joint_states
        bool hasGripper = jointStateSubscriber.HasJoint(gripperJointName);
        
        if (!hasGripper)
        {
            // Joint not found - log warning periodically
            if (showDebugLogs && UnityEngine.Time.frameCount % 300 == 0) // Log every 5 seconds
            {
                Debug.LogWarning($"xdrivegripper: Joint '{gripperJointName}' not found in joint_states! Available joints:");
                var allJoints = jointStateSubscriber.GetAllJointNames();
                foreach (var joint in allJoints)
                {
                    if (joint.ToLower().Contains("gripper"))
                    {
                        Debug.LogWarning($"xdrivegripper:   Found gripper-related joint: {joint}");
                    }
                }
            }
            return; // Can't sync without joint data
        }

        // Get real robot position from StretchJointStateSub
        float realRobotPosition = jointStateSubscriber.GetJointPosition(gripperJointName);
        
        // Debug: Log actual position received
        if (showDebugLogs && UnityEngine.Time.frameCount % 60 == 0) // Log once per second
        {
            Debug.Log($"xdrivegripper: Received joint position - {gripperJointName}: {realRobotPosition:F4}");
        }

        // Clamp to valid range (use joint limits from URDF)
        realRobotPosition = Mathf.Clamp(realRobotPosition, gripperMinPosition, gripperMaxPosition);

        // Update target position for Unity visualization
        targetGripperPosition = realRobotPosition;

        // Instant sync - Unity visualization immediately matches real robot position
        // Only update if position actually changed (to avoid unnecessary updates)
        float oldPosition = currentGripperPosition;
        if (Mathf.Abs(currentGripperPosition - targetGripperPosition) > 0.001f)
        {
            currentGripperPosition = targetGripperPosition;
            
            if (showDebugLogs)
            {
                Debug.Log($"xdrivegripper: Position changed - Old: {oldPosition:F4}, New: {currentGripperPosition:F4}");
            }
        }

        // Update Unity visualization based on mode
        if (useArticulationBody)
        {
            // Always update ArticulationBody every frame for best synchronization
            UpdateArticulationDrive();
        }
        else
        {
            // Update Transform positions for finger visualization
            UpdateFingerVisualization();
        }

        if (showDebugLogs && UnityEngine.Time.frameCount % 60 == 0) // Log once per second
        {
            Debug.Log($"xdrivegripper: Synced - Real Robot: {realRobotPosition:F3} | Unity: {currentGripperPosition:F3} | Command: {commandPosition:F3}");
        }
    }

    /// <summary>
    /// Auto-find ArticulationBody component by GameObject name
    /// </summary>
    void FindGripperArticulation()
    {
        if (gripperArticulation == null)
        {
            // Try to find by link name (link_gripper_finger_left)
            GameObject gripperObj = GameObject.Find("link_gripper_finger_left");
            if (gripperObj != null)
            {
                gripperArticulation = gripperObj.GetComponent<ArticulationBody>();
                if (gripperArticulation != null && showDebugLogs)
                {
                    Debug.Log($"xdrivegripper: Auto-found gripperArticulation on {gripperObj.name}");
                }
            }
        }
    }

    /// <summary>
    /// Validate that ArticulationBody is found and is a revolute joint
    /// </summary>
    void ValidateArticulation()
    {
        gripperFound = gripperArticulation != null && gripperArticulation.jointType == ArticulationJointType.RevoluteJoint;

        if (!gripperFound)
        {
            Debug.LogError("xdrivegripper: Missing or invalid ArticulationBody component!");
            Debug.LogError("xdrivegripper: Assign gripper ArticulationBody in Inspector or enable auto-find!");
            if (gripperArticulation != null)
            {
                Debug.LogError($"xdrivegripper: Found ArticulationBody but wrong type: {gripperArticulation.jointType} (expected RevoluteJoint)");
            }
        }
    }

    /// <summary>
    /// Initialize ArticulationBody drive parameters
    /// </summary>
    void InitializeDrive()
    {
        if (gripperArticulation == null) return;

        ArticulationDrive drive = gripperArticulation.xDrive;
        drive.stiffness = stiffness;
        drive.damping = damping;
        drive.forceLimit = forceLimit;
        gripperArticulation.xDrive = drive;

        if (showDebugLogs)
        {
            Debug.Log($"xdrivegripper: Initialized drive - Stiffness: {stiffness}, Damping: {damping}, ForceLimit: {forceLimit}");
        }
    }

    /// <summary>
    /// Load current position from ArticulationBody
    /// </summary>
    void LoadCurrentPosition()
    {
        if (gripperArticulation == null) return;

        // For revolute joints, Unity returns jointPosition[0] in radians
        // Gripper position range is -0.6 to 0.6 radians (from URDF)
        float rawPosition = gripperArticulation.jointPosition[0];
        
        // Use the raw position (already in radians)
        currentGripperPosition = rawPosition;
        
        // Clamp to valid range (use joint limits from URDF)
        currentGripperPosition = Mathf.Clamp(currentGripperPosition, gripperMinPosition, gripperMaxPosition);
        
        targetGripperPosition = currentGripperPosition;
        commandPosition = currentGripperPosition;
        lastPublishedPosition = currentGripperPosition;

        if (showDebugLogs)
        {
            Debug.Log($"xdrivegripper: Loaded current position: {currentGripperPosition:F3} (raw: {rawPosition:F3})");
        }
    }

    /// <summary>
    /// Update ArticulationBody X Drive target (Unity visualization)
    /// IMPORTANT: For revolute joints, Unity expects target in DEGREES, not radians!
    /// Gripper position is in radians (-0.6 to 0.6 rad = -34.4° to 34.4°)
    /// </summary>
    void UpdateArticulationDrive()
    {
        if (gripperArticulation == null) return;

        ArticulationDrive drive = gripperArticulation.xDrive;
        
        // For revolute joints, Unity's xDrive.target expects degrees
        // Convert from radians to degrees
        float targetInDegrees = currentGripperPosition * Mathf.Rad2Deg;
        
        drive.target = targetInDegrees;
        gripperArticulation.xDrive = drive;
    }

    /// <summary>
    /// Get the movement direction vector based on movementAxis setting
    /// </summary>
    Vector3 GetMovementDirection(Transform transform)
    {
        if (movementAxis == -1)
        {
            // Use transform's local right direction (respects GameObject rotation)
            if (transform.parent != null)
            {
                return transform.parent.InverseTransformDirection(transform.right).normalized;
            }
            else
            {
                return transform.right.normalized;
            }
        }
        else if (movementAxis == 0)
            return Vector3.right; // X axis (left/right)
        else if (movementAxis == 1)
            return Vector3.up; // Y axis (up/down)
        else // movementAxis == 2
            return Vector3.forward; // Z axis (forward/back)
    }

    /// <summary>
    /// Update Unity visualization for both gripper fingers using Transform manipulation
    /// Moves fingers symmetrically based on current gripper position
    /// Similar to grippercontrolviz.cs
    /// </summary>
    void UpdateFingerVisualization()
    {
        // Check if both fingers are assigned
        bool hasBothFingers = (GripperFingerLeft != null && GripperFingerRight != null);
        
        if (!hasBothFingers)
            return;

        // Map gripper position (0.0 = closed, 0.6 = open) to finger separation
        // Position 0.0 -> separation 0.0 (fingers together)
        // Position 0.6 -> separation maxFingerSeparation (fingers fully open)
        float normalizedPosition = Mathf.InverseLerp(gripperClosedPosition, gripperOpenPosition, currentGripperPosition);
        float fingerSeparation = normalizedPosition * maxFingerSeparation;
        
        // Each finger moves half the separation distance
        // Left finger moves in negative direction, right finger moves in positive direction
        float halfSeparation = fingerSeparation * 0.5f;

        // Get movement direction for left finger
        Vector3 leftDirection = GetMovementDirection(GripperFingerLeft);
        // Right finger moves in opposite direction
        Vector3 rightDirection = -leftDirection;

        // Update left finger position (moves left/negative direction)
        GripperFingerLeft.localPosition = baseLocalPositionLeft + leftDirection * halfSeparation;
        
        // Update right finger position (moves right/positive direction)
        GripperFingerRight.localPosition = baseLocalPositionRight + rightDirection * halfSeparation;
    }

    void InitializeROS2()
    {
        try
        {
            // Create ROS2 node
            ros2Node = ros2Unity.CreateNode("xdrivegripper_node");

            // Create publisher for gripper commands
            gripperPublisher = ros2Node.CreatePublisher<JointTrajectory>(gripperCommandTopic);

            if (showDebugLogs)
            {
                Debug.Log($"xdrivegripper: ROS2 initialized successfully!");
                Debug.Log($"xdrivegripper: Publisher created - {gripperCommandTopic}");
            }

            isInitialized = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"xdrivegripper: Failed to initialize ROS2: {e.Message}");
            Debug.LogError($"Stack trace: {e.StackTrace}");
        }
    }

    /// <summary>
    /// Send command matching the exact format of the working manual command
    /// This uses zero timestamp and empty arrays like XsyncLIft.cs and grippercontrolviz.cs
    /// </summary>
    void SendCommand(float position)
    {
        if (gripperPublisher == null || !isInitialized)
            return;

        // Clamp position to valid range (use joint limits from URDF)
        position = Mathf.Clamp(position, gripperMinPosition, gripperMaxPosition);

        // Rate limiting - don't publish too frequently
        float timeSinceLastPublish = UnityEngine.Time.time - lastPublishTime;
        if (timeSinceLastPublish < publishInterval)
        {
            if (showDebugLogs)
            {
                Debug.Log($"xdrivegripper: Rate limiting - waiting {publishInterval - timeSinceLastPublish:F2}s");
            }
            return;
        }

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

        // Set joint name (only gripper joint) - matching manual command
        trajectory.Joint_names = new string[] { gripperJointName };

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
        gripperPublisher.Publish(trajectory);
        lastPublishedPosition = position;
        lastPublishTime = UnityEngine.Time.time;

        if (showDebugLogs)
        {
            Debug.Log($"xdrivegripper: Sent command - {gripperJointName}={position:F3}, duration={duration:F1}s");
        }
    }

    void OnDestroy()
    {
        if (gripperPublisher != null)
        {
            gripperPublisher.Dispose();
        }
    }
}
