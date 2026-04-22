using UnityEngine;
using UnityEngine.InputSystem;
using ROS2;
using trajectory_msgs.msg;
using builtin_interfaces.msg;

/// <summary>
/// Control gripper open/close using right hand gripper button
/// Publishes JointTrajectory to real Stretch3 robot via ROS2
/// Similar to syncliftbyjoy.cs and fixmovelift.cs but for gripper control
/// </summary>
public class grippercontrolviz : MonoBehaviour
{
    [Header("ROS2 Settings")]
    [Tooltip("Topic to publish gripper commands (action goal topic)")]
    public string gripperCommandTopic = "/stretch_controller/follow_joint_trajectory/goal";

    [Header("Joystick Input")]
    [Tooltip("Right hand gripper button InputActionReference for gripper control")]
    public InputActionReference gripperButton;

    [Header("Gripper Settings")]
    [Tooltip("Gripper joint name to use (joint_gripper_finger_left or gripper_aperture)")]
    public string gripperJointName = "joint_gripper_finger_left";

    [Header("Gripper Limits (Working Values)")]
    [Tooltip("Gripper position when open (for joint_gripper_finger_left: 0.5 = fully open)")]
    public float gripperOpenPosition = 0.6f;

    [Tooltip("Gripper position when closed (for joint_gripper_finger_left: 0.0 = fully closed)")]
    public float gripperClosedPosition = 0.0f;

    [Header("GameObject References - Gripper Fingers")]
    [Tooltip("Left gripper finger Transform - REQUIRED for synchronized visualization")]
    public Transform GripperFingerLeft;
    
    [Tooltip("Right gripper finger Transform - REQUIRED for synchronized visualization")]
    public Transform GripperFingerRight;

    [Header("Unity Visualization Settings")]
    [Tooltip("Movement axis in Unity: 0=X, 1=Y, 2=Z. Or use -1 for transform.right direction ")]
    public int movementAxis = 2; // 0=X (left/right), -1 = use transform.right

    [Tooltip("Maximum finger separation distance (meters) - how far fingers move apart when fully open")]
    public float maxFingerSeparation = 0.1f; // 10cm separation when fully open

    [Header("Trajectory Settings")]
    [Tooltip("Trajectory duration (seconds) - fixed 2s like working manual command")]
    public float duration = 2.0f;

    [Header("Publishing Settings")]
    [Tooltip("Minimum time between publishes (seconds) - prevents overwhelming robot")]
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
    private float currentGripperPosition = 0.0f; // Current gripper position (0.0 = closed, 0.5 = open)
    private bool lastButtonState = false; // Last button state to detect changes
    private float lastPublishTime = 0.0f;
    
    // Base positions for gripper fingers (when fully closed)
    private Vector3 baseLocalPositionLeft = Vector3.zero;
    private Vector3 baseLocalPositionRight = Vector3.zero;

    void Start()
    {
        if (gripperButton == null)
        {
            Debug.LogError("gripper: Gripper button InputActionReference not assigned!");
            return;
        }

        // Initialize Unity visualization position
        // Store base positions for both gripper fingers (when gripper is fully closed)
        bool hasBothFingers = (GripperFingerLeft != null && GripperFingerRight != null);
        
        if (hasBothFingers)
        {
            // Store base positions for both fingers (when gripper is closed)
            baseLocalPositionLeft = GripperFingerLeft.localPosition;
            baseLocalPositionRight = GripperFingerRight.localPosition;
            currentGripperPosition = gripperClosedPosition; // Start at fully closed
            
            if (showDebugLogs)
            {
                Debug.Log("gripper: Both gripper fingers assigned - synchronized visualization enabled");
            }
        }
        else
        {
            Debug.LogWarning("gripper: Gripper finger GameObjects not assigned! Assign both GripperFingerLeft and GripperFingerRight for visualization.");
        }

        // Initialize gripper position to closed
        currentGripperPosition = gripperClosedPosition;

        // Initialize ROS2
        ros2Unity = GetComponent<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            ros2Unity = FindObjectOfType<ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                Debug.LogError("gripper: ROS2UnityComponent not found! Add ROS2UnityCore to scene.");
                return;
            }
        }

        if (showDebugLogs)
        {
            Debug.Log("gripper: Initialized");
            Debug.Log($"gripper: Gripper positions - Open: {gripperOpenPosition:F3}, Closed: {gripperClosedPosition:F3}");
            Debug.Log($"gripper: Using joint name: {gripperJointName}");
            Debug.Log($"gripper: Using working trajectory format (zero timestamp, empty arrays)");
            Debug.Log($"gripper: Max finger separation: {maxFingerSeparation:F3}m");
            
            // Log movement axis setting
            if (movementAxis == -1)
                Debug.Log($"gripper: Movement axis: Transform.right (auto-detected from GameObject rotation)");
            else if (movementAxis == 0)
                Debug.Log($"gripper: Movement axis: X (left/right)");
            else if (movementAxis == 1)
                Debug.Log($"gripper: Movement axis: Y (up/down)");
            else
                Debug.Log($"gripper: Movement axis: Z (forward/back)");
            
            // Log which fingers are assigned
            bool hasBoth = (GripperFingerLeft != null && GripperFingerRight != null);
            if (hasBoth)
            {
                Debug.Log($"gripper: ✓ Both fingers assigned - synchronized visualization enabled");
                Debug.Log($"gripper:   Left: {GripperFingerLeft.name}, Right: {GripperFingerRight.name}");
            }
            else
            {
                Debug.LogWarning($"gripper: ⚠ Not both fingers assigned! Assign both for full visualization.");
                Debug.LogWarning($"gripper:   Left: {(GripperFingerLeft != null ? GripperFingerLeft.name : "NULL")}, Right: {(GripperFingerRight != null ? GripperFingerRight.name : "NULL")}");
            }
        }
    }

    void Update()
    {
        if (gripperButton == null)
            return;

        // Try to initialize ROS2 if not already initialized
        if (!isInitialized)
        {
            if (ros2Unity != null && ros2Unity.Ok())
            {
                InitializeROS2();
            }
        }

        if (!isInitialized || gripperPublisher == null)
            return;

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

        // Update Unity visualization - move both fingers symmetrically
        // Map gripper position (0.0 = closed, 0.5 = open) to finger separation
        UpdateFingerVisualization();

        if (showDebugLogs && buttonPressed)
        {
            Debug.Log($"gripper: Button pressed (value: {buttonValue:F2}) | Current position: {currentGripperPosition:F2}");
        }
    }

    void InitializeROS2()
    {
        try
        {
            // Create ROS2 node
            ros2Node = ros2Unity.CreateNode("gripper_node");

            // Create publisher for gripper commands
            gripperPublisher = ros2Node.CreatePublisher<JointTrajectory>(gripperCommandTopic);

            if (showDebugLogs)
            {
                Debug.Log($"gripper: ROS2 initialized successfully!");
                Debug.Log($"gripper: Publisher created - {gripperCommandTopic}");
            }

            isInitialized = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"gripper: Failed to initialize ROS2: {e.Message}");
            Debug.LogError($"Stack trace: {e.StackTrace}");
        }
    }

    /// <summary>
    /// Toggle gripper between open and closed positions
    /// </summary>
    void ToggleGripper()
    {
        // Toggle between open and closed
        // If current position is closer to closed, open it; otherwise close it
        float distanceToClosed = Mathf.Abs(currentGripperPosition - gripperClosedPosition);
        float distanceToOpen = Mathf.Abs(currentGripperPosition - gripperOpenPosition);

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
    /// Get the movement direction vector based on movementAxis setting
    /// </summary>
    Vector3 GetMovementDirection(Transform transform)
    {
        if (movementAxis == -1)
        {
            // Use transform's local right direction (respects GameObject rotation)
            // This is the most accurate - fingers move along their right direction
            if (transform.parent != null)
            {
                // Get right direction in parent's local space
                return transform.parent.InverseTransformDirection(transform.right).normalized;
            }
            else
            {
                // No parent, use world right converted to local
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
    /// Update Unity visualization for both gripper fingers
    /// Moves fingers symmetrically based on current gripper position
    /// </summary>
    void UpdateFingerVisualization()
    {
        // Check if both fingers are assigned
        bool hasBothFingers = (GripperFingerLeft != null && GripperFingerRight != null);
        
        if (!hasBothFingers)
            return;

        // Map gripper position (0.0 = closed, 0.5 = open) to finger separation
        // Position 0.0 -> separation 0.0 (fingers together)
        // Position 0.5 -> separation maxFingerSeparation (fingers fully open)
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

    /// <summary>
    /// Open the gripper
    /// </summary>
    public void OpenGripper()
    {
        // Set to open position
        currentGripperPosition = gripperOpenPosition;
        SendCommand(currentGripperPosition);
        
        // Update visualization immediately
        UpdateFingerVisualization();

        if (showDebugLogs)
        {
            Debug.Log($"gripper: Opening gripper to position: {currentGripperPosition:F3}");
        }
    }

    /// <summary>
    /// Close the gripper
    /// </summary>
    public void CloseGripper()
    {
        // Set to closed position
        currentGripperPosition = gripperClosedPosition;
        SendCommand(currentGripperPosition);
        
        // Update visualization immediately
        UpdateFingerVisualization();

        if (showDebugLogs)
        {
            Debug.Log($"gripper: Closing gripper to position: {currentGripperPosition:F3}");
        }
    }

    /// <summary>
    /// Send command matching the exact format of the working manual command
    /// This uses zero timestamp and empty arrays like fixmovelift.cs
    /// </summary>
    void SendCommand(float position)
    {
        if (gripperPublisher == null || !isInitialized)
            return;

        // Clamp position to valid range (0.0 to 0.5 for joint_gripper_finger_left)
        float minPos = Mathf.Min(gripperClosedPosition, gripperOpenPosition);
        float maxPos = Mathf.Max(gripperClosedPosition, gripperOpenPosition);
        position = Mathf.Clamp(position, minPos, maxPos);

        // Rate limiting - don't publish too frequently
        float timeSinceLastPublish = UnityEngine.Time.time - lastPublishTime;
        if (timeSinceLastPublish < publishInterval)
        {
            if (showDebugLogs)
            {
                Debug.Log($"gripper: Rate limiting - waiting {publishInterval - timeSinceLastPublish:F2}s");
            }
            return;
        }

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

        // Set joint name (only gripper joint) - matching manual command
        trajectory.Joint_names = new string[] { gripperJointName };
        // Create trajectory point - matching exact manual command format
        // Manual command shows: positions: [0.5] or [0.0], time_from_start: sec: 2, nanosec: 0
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
        gripperPublisher.Publish(trajectory);
        lastPublishTime = UnityEngine.Time.time;

        if (showDebugLogs)
        {
            Debug.Log($"gripper: Sent command - {gripperJointName}={position:F3}, duration={duration:F1}s");
        }
    }
}
