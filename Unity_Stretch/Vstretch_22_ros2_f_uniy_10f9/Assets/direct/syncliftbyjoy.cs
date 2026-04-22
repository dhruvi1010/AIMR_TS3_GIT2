using UnityEngine;
using UnityEngine.InputSystem;
using ROS2;
using trajectory_msgs.msg;
using sensor_msgs.msg;
using builtin_interfaces.msg;

/// <summary>
/// Control lift up/down using right hand Meta joystick
/// Publishes JointTrajectory to real Stretch3 robot via ROS2
/// Similar to ros2basejoystickcontroller but for lift control
/// </summary>
public class syncliftbyjoy : MonoBehaviour
{
    [Header("ROS2 Settings")]
    [Tooltip("Topic to publish lift commands (action goal topic)")]
    public string liftCommandTopic = "/stretch_controller/follow_joint_trajectory/goal";

    [Tooltip("Topic to subscribe for joint states feedback")]
    public string jointStateTopic = "/stretch/joint_states";

    [Tooltip("Manual initial position (meters) - use if joint_states not available. Set to robot's current position.")]
    public float manualInitialPosition = 0.0f; // Can be set manually if joint_states don't work

    [Header("GameObject Reference")]
    [Tooltip("The Transform to move up/down in Unity (assign link_lift Transform)")]
    public Transform LiftLink;

    [Header("Joystick Input")]
    [Tooltip("Right hand joystick InputActionReference for lift control")]
    public InputActionReference rightHandJoystick;

    [Header("Movement Settings")]
    [Tooltip("Movement speed (meters per second)")]
    public float liftSpeed = 0.1f; // m/s

    [Header("Lift Limits (meters)")]
    [Tooltip("Minimum lift position (meters) - from URDF: 0.0")]
    public float liftMinPosition = 0.5f;

    [Tooltip("Maximum lift position (meters) - from URDF: 1.1")]
    public float liftMaxPosition = 1.1f;

    [Tooltip("Maximum velocity for trajectory (m/s) - lower to prevent contact detection")]
    public float maxVelocity = 0.1f; // Reduced further to prevent contact detection

    [Header("Publishing Settings")]
    [Tooltip("Minimum time between publishes (seconds) - prevents overwhelming robot")]
    public float publishInterval = 0.15f; // Publish max 6-7 times per second

    [Tooltip("Minimum position change to trigger publish (meters) - prevents tiny updates")]
    public float minPositionChange = 0.005f; // 5mm minimum change (reduced to allow more frequent publishing)

    [Tooltip("Maximum position jump allowed (meters) - prevents large sudden movements")]
    public float maxPositionJump = 0.05f; // 5cm max jump per command

    [Tooltip("Timeout to allow publishing without sync (seconds) - fallback if joint_states not available")]
    public float syncTimeout = 5.0f; // Allow publishing after 5 seconds even without sync

    [Header("Debug")]
    public bool showDebugLogs = true;

    // ROS2 components
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<JointTrajectory> liftPublisher;
    private ISubscription<JointState> jointStateSubscriber;

    // Internal state
    private float targetLiftPosition = 0.0f; // Lift position in meters (for ROS2)
    private float actualLiftPosition = 0.0f; // Actual robot position from joint_states
    private float unityPosY = 0.5f; // Unity Y position for visualization
    private float lastPublishedPosition = 0.0f; // Last position sent to robot
    private float lastPublishTime = 0.0f; // Time of last publish
    private bool isInitialized = false;
    private bool hasReceivedJointState = false;
    private bool hasInitialSync = false; // Track if we've synced with robot position
    private float startTime = 0.0f; // Time when script started
    private bool timeoutLogged = false; // Track if timeout message was already logged

    void Start()
    {
        if (rightHandJoystick == null)
        {
            Debug.LogError("liftbyjoy: Right hand joystick InputActionReference not assigned!");
            return;
        }

        // Initialize Unity visualization position
        if (LiftLink != null)
        {
            unityPosY = LiftLink.position.y;
            unityPosY = Mathf.Clamp(unityPosY, liftMinPosition, liftMaxPosition);
        }

        // Initialize ROS2
        ros2Unity = GetComponent<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            ros2Unity = FindObjectOfType<ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                Debug.LogError("liftbyjoy: ROS2UnityComponent not found! Add ROS2UnityCore to scene.");
                return;
            }
        }

        // Initialize target position (will be synced from robot when joint_states arrive)
        // Use manual initial position if provided, otherwise assume 0.0m
        actualLiftPosition = manualInitialPosition; // Will be updated from joint_states
        targetLiftPosition = actualLiftPosition; // Start from robot's assumed position
        lastPublishedPosition = targetLiftPosition;
        lastPublishTime = 0.0f;
        
        // Sync Unity visualization to assumed robot position
        unityPosY = actualLiftPosition;
        if (LiftLink != null)
        {
            unityPosY = Mathf.Clamp(unityPosY, liftMinPosition, liftMaxPosition);
            LiftLink.position = new Vector3(
                LiftLink.position.x,
                unityPosY,
                LiftLink.position.z
            );
        }
        
        if (showDebugLogs && manualInitialPosition != 0.0f)
        {
            Debug.Log($"liftbyjoy: Using manual initial position: {manualInitialPosition:F3}m");
        }
        startTime = UnityEngine.Time.time; // Record start time for timeout

        if (showDebugLogs)
        {
            Debug.Log("liftbyjoy: Initialized");
            if (LiftLink != null)
            {
                Debug.Log($"liftbyjoy: Unity Transform: {LiftLink.name}");
            }
            Debug.Log($"liftbyjoy: Lift limits: [{liftMinPosition:F2}, {liftMaxPosition:F2}] meters");
            Debug.Log($"liftbyjoy: Publish interval: {publishInterval:F2}s | Min change: {minPositionChange:F3}m");
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

        // Get joystick input (y-axis for up/down movement)
        Vector2 joystickInput = rightHandJoystick.action.ReadValue<Vector2>();
        float verticalInput = joystickInput.y; // y-axis is up/down on joystick

        // Update target position based on input (for ROS2 robot)
        // Use actual position as base if we know it, otherwise use current target
        float basePosition = hasInitialSync ? actualLiftPosition : targetLiftPosition;
        targetLiftPosition = basePosition + (verticalInput * liftSpeed * UnityEngine.Time.deltaTime);
        targetLiftPosition = Mathf.Clamp(targetLiftPosition, liftMinPosition, liftMaxPosition);

        // Update Unity visualization position (same movement)
        unityPosY += verticalInput * liftSpeed * UnityEngine.Time.deltaTime;
        unityPosY = Mathf.Clamp(unityPosY, liftMinPosition, liftMaxPosition);

        // Update Unity Transform for visualization
        if (LiftLink != null)
        {
            LiftLink.position = new Vector3(
                LiftLink.position.x,
                unityPosY,
                LiftLink.position.z
            );
        }

        // Publish to robot with throttling and position change threshold
        // Allow publishing if synced OR after timeout (fallback if joint_states not available)
        float timeSinceStart = UnityEngine.Time.time - startTime;
        bool allowPublishing = hasInitialSync || (timeSinceStart > syncTimeout);
        
        // Log when publishing becomes enabled via timeout (only once)
        if (allowPublishing && !hasInitialSync && timeSinceStart > syncTimeout && !timeoutLogged && showDebugLogs)
        {
            Debug.Log($"liftbyjoy: Publishing enabled via timeout (no joint_states received). Starting to publish commands...");
            timeoutLogged = true;
        }
        
        if (liftPublisher != null && isInitialized && allowPublishing)
        {
            float timeSinceLastPublish = UnityEngine.Time.time - lastPublishTime;
            float positionChange = Mathf.Abs(targetLiftPosition - lastPublishedPosition);
            float distanceFromActual = Mathf.Abs(targetLiftPosition - actualLiftPosition);

            // Only publish if:
            // 1. Enough time has passed (rate limiting)
            // 2. Position changed significantly (prevents tiny updates)
            // 3. There's actual joystick input
            // 4. Target is not too far from actual position (prevents large jumps) - only check if synced
            bool positionCheck = hasInitialSync ? (distanceFromActual <= maxPositionJump) : true;
            
            if (timeSinceLastPublish >= publishInterval &&
                positionChange >= minPositionChange &&
                Mathf.Abs(verticalInput) > 0.01f &&
                positionCheck)
            {
                if (showDebugLogs)
                {
                    Debug.Log($"liftbyjoy: Publishing command - target: {targetLiftPosition:F3}m, actual: {actualLiftPosition:F3}m, synced: {hasInitialSync}");
                }
                PublishLiftTrajectory();
                lastPublishedPosition = targetLiftPosition;
                lastPublishTime = UnityEngine.Time.time;
                
                // Update actual position estimate after publishing (if not synced)
                // This helps track where we think the robot is, even without joint_states
                if (!hasInitialSync)
                {
                    // Estimate: robot should be moving toward target
                    // We'll update this more conservatively - only if we get feedback
                    // For now, keep using manual initial position
                }
            }
            else if (showDebugLogs && Mathf.Abs(verticalInput) > 0.01f && UnityEngine.Time.frameCount % 60 == 0)
            {
                // Log why we're not publishing
                if (timeSinceLastPublish < publishInterval)
                    Debug.Log($"liftbyjoy: Waiting for publish interval ({timeSinceLastPublish:F2}s < {publishInterval:F2}s)");
                else if (positionChange < minPositionChange)
                    Debug.Log($"liftbyjoy: Position change too small ({positionChange:F4}m < {minPositionChange:F3}m)");
                else if (!positionCheck)
                    Debug.Log($"liftbyjoy: Position jump too large ({distanceFromActual:F3}m > {maxPositionJump:F3}m)");
            }
            else if (distanceFromActual > maxPositionJump && showDebugLogs && hasInitialSync)
            {
                // Log when we skip publishing due to large jump
                if (UnityEngine.Time.frameCount % 60 == 0) // Log once per second
                {
                    Debug.LogWarning($"liftbyjoy: Skipping publish - target ({targetLiftPosition:F3}m) too far from actual ({actualLiftPosition:F3}m). Max jump: {maxPositionJump:F3}m");
                }
            }
        }
        else if (liftPublisher != null && isInitialized && !allowPublishing && showDebugLogs)
        {
            // Log when waiting for sync
            if (UnityEngine.Time.frameCount % 120 == 0) // Log every 2 seconds
            {
                Debug.LogWarning($"liftbyjoy: Waiting for joint_states sync... (hasInitialSync: {hasInitialSync}, time: {timeSinceStart:F1}s/{syncTimeout:F1}s)");
            }
        }

        if (showDebugLogs && Mathf.Abs(verticalInput) > 0.01f)
        {
            Debug.Log($"liftbyjoy: Joystick Input: {verticalInput:F2} | Unity Y: {unityPosY:F3} | ROS2 Position: {targetLiftPosition:F3}m");
        }
    }

    void InitializeROS2()
    {
        try
        {
            // Create ROS2 node
            ros2Node = ros2Unity.CreateNode("liftbyjoy_node");

            // Sanitize topic name
            liftCommandTopic = SanitizeTopicName(liftCommandTopic);

            // Create publisher for lift commands
            liftPublisher = ros2Node.CreatePublisher<JointTrajectory>(liftCommandTopic);

            // Create subscriber for joint states to know robot's actual position
            if (!string.IsNullOrEmpty(jointStateTopic))
            {
                jointStateTopic = SanitizeTopicName(jointStateTopic);
                jointStateSubscriber = ros2Node.CreateSubscription<JointState>(
                    jointStateTopic,
                    OnJointStateReceived
                );
                if (showDebugLogs)
                {
                    Debug.Log($"liftbyjoy: Subscribed to joint states - {jointStateTopic}");
                    Debug.Log($"liftbyjoy: Will allow publishing after {syncTimeout}s timeout if no joint_states received");
                }
            }
            else
            {
                if (showDebugLogs)
                {
                    Debug.LogWarning("liftbyjoy: No joint_states topic specified! Publishing will be enabled after timeout.");
                }
            }

            if (showDebugLogs)
            {
                Debug.Log($"liftbyjoy: ROS2 initialized successfully!");
                Debug.Log($"liftbyjoy: Publisher created - {liftCommandTopic}");
            }

            isInitialized = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"liftbyjoy: Failed to initialize ROS2: {e.Message}");
            Debug.LogError($"Stack trace: {e.StackTrace}");
        }
    }

    string SanitizeTopicName(string topicName)
    {
        if (string.IsNullOrEmpty(topicName))
            return topicName;

        string sanitized = topicName.Replace("//", "/");

        if (!string.IsNullOrEmpty(sanitized) && !sanitized.StartsWith("/"))
        {
            sanitized = "/" + sanitized;
        }

        return sanitized;
    }

    /// <summary>
    /// Callback when joint state message is received from robot
    /// Updates actual robot position to sync with real robot
    /// </summary>
    void OnJointStateReceived(JointState msg)
    {
        hasReceivedJointState = true;

        if (showDebugLogs && UnityEngine.Time.frameCount % 60 == 0) // Log once per second
        {
            Debug.Log($"liftbyjoy: ✓ Received joint_states message with {msg.Name.Length} joints");
        }

        // Parse joint_lift position from joint_states
        for (int i = 0; i < msg.Name.Length; i++)
        {
            string jointName = msg.Name[i];
            if (jointName == "joint_lift" && i < msg.Position.Length)
            {
                actualLiftPosition = (float)msg.Position[i];
                
                if (showDebugLogs && UnityEngine.Time.frameCount % 60 == 0) // Log once per second
                {
                    Debug.Log($"liftbyjoy: joint_lift position from robot: {actualLiftPosition:F3}m");
                }

                // On first message, sync target position to actual robot position
                if (!hasInitialSync)
                {
                    targetLiftPosition = actualLiftPosition;
                    lastPublishedPosition = actualLiftPosition;
                    unityPosY = actualLiftPosition; // Sync Unity visualization too

                    if (LiftLink != null)
                    {
                        LiftLink.position = new Vector3(
                            LiftLink.position.x,
                            unityPosY,
                            LiftLink.position.z
                        );
                    }

                    hasInitialSync = true;

                    if (showDebugLogs)
                    {
                        Debug.Log($"liftbyjoy: Initial sync with robot - lift position: {actualLiftPosition:F3}m");
                    }
                }

                break;
            }
        }
    }

    /// <summary>
    /// Publish lift trajectory to robot
    /// Creates JointTrajectory with only joint_lift for lift control
    /// </summary>
    void PublishLiftTrajectory()
    {
        if (liftPublisher == null || !isInitialized)
            return;

        var trajectory = new JointTrajectory();

        // Set header - USE WORKING FORMAT from fixmovelift.cs
        // KEY: Zero timestamp and empty frame_id (not GetCurrentROSTime() and "base_link")
        trajectory.Header = new std_msgs.msg.Header
        {
            Stamp = new builtin_interfaces.msg.Time
            {
                Sec = 0,
                Nanosec = 0
            },
            Frame_id = "" // Empty frame_id like working manual command
        };

        // Set joint name (only lift joint)
        trajectory.Joint_names = new string[] { "joint_lift" };

        // Use fixed 2-second duration like the working manual command
        float trajectoryDuration = 2.0f; // Fixed 2 seconds (matching working command exactly)

        // Create trajectory point - USE WORKING FORMAT from fixmovelift.cs
        // KEY: Empty arrays (not arrays with 0.0 values) and exact 0 nanosec
        var point = new JointTrajectoryPoint
        {
            Positions = new double[] { targetLiftPosition },
            Velocities = new double[0], // Empty array - KEY DIFFERENCE (not new double[] { 0.0 })
            Accelerations = new double[0], // Empty array - KEY DIFFERENCE
            Effort = new double[0], // Empty array - KEY DIFFERENCE
            Time_from_start = new builtin_interfaces.msg.Duration
            {
                Sec = (int)trajectoryDuration,
                Nanosec = 0 // Exactly 0 - KEY DIFFERENCE (not calculated)
            }
        };

        trajectory.Points = new JointTrajectoryPoint[] { point };

        // Publish trajectory
        liftPublisher.Publish(trajectory);

        if (showDebugLogs)
        {
            float positionDelta = Mathf.Abs(targetLiftPosition - actualLiftPosition);
            Debug.Log($"liftbyjoy: Published lift command - joint_lift={targetLiftPosition:F3}m (from {actualLiftPosition:F3}m, delta: {positionDelta:F3}m, duration: {trajectoryDuration:F2}s)");
        }
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

    public void ResetPosition()
    {
        targetLiftPosition = liftMinPosition;
        unityPosY = liftMinPosition;

        if (LiftLink != null)
        {
            LiftLink.position = new Vector3(
                LiftLink.position.x,
                unityPosY,
                LiftLink.position.z
            );
        }

        if (showDebugLogs)
        {
            Debug.Log($"liftbyjoy: Reset to minimum position: {targetLiftPosition:F3}m");
        }
    }
}
