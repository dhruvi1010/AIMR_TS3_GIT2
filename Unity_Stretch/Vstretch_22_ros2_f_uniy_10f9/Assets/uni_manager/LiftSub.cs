using UnityEngine;
using ROS2;
using sensor_msgs.msg;

/// <summary>
/// Subscribe to Stretch3 robot's /stretch/joint_states topic to get real robot position
/// This subscribes to the joint_states published by stretch_driver in navigation mode
/// Message type: sensor_msgs/JointState
/// Topic: /stretch/joint_states (published by stretch_driver)
/// </summary>
public class LiftSub : MonoBehaviour
{
    [Header("ROS2 Settings")]
    [Tooltip("Topic to subscribe for joint states - Stretch driver publishes to /stretch/joint_states")]
    public string jointStateTopic = "/stretch/joint_states";

    [Tooltip("QoS Profile: DEFAULT (reliable - matches stretch_driver) or SENSOR_DATA (best-effort). Robot publishes with RELIABLE QoS.")]
    public bool useSensorDataQoS = false; // Changed to false - robot publishes with RELIABLE QoS

    [Header("Joint to Monitor")]
    [Tooltip("Joint name to extract from joint_states - use 'joint_lift' for lift")]
    public string jointNameToMonitor = "joint_lift";

    [Header("Logging Settings")]
    [Tooltip("Log frequency (frames) - logs every N frames to reduce spam")]
    public int logFrequency = 60; // Log once per second at 60fps

    [Tooltip("Show all joints in log (not just the monitored joint)")]
    public bool showAllJoints = false;

    [Tooltip("Enable debug logs")]
    public bool showDebugLogs = true;

    // ROS2 components
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private ISubscription<JointState> jointStateSubscriber;
    private bool isInitialized = false;

    // State tracking (thread-safe - simple assignments)
    private float currentJointPosition = 0.0f;
    private float currentJointVelocity = 0.0f; // Velocity from JointState message
    private bool hasReceivedMessage = false;
    private int messageCount = 0;
    private float startTime = 0.0f;
    private float lastMessageTime = 0.0f;
    private bool timeoutWarningLogged = false;
    
    // Thread-safe flags for main thread processing
    private bool newMessageReceived = false;
    private bool isFirstMessage = false;
    private int lastProcessedMessageCount = 0;

    void Start()
    {
        // Initialize ROS2
        ros2Unity = GetComponent<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            ros2Unity = FindObjectOfType<ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                Debug.LogError("LiftSub: ROS2UnityComponent not found! Add ROS2UnityCore to scene.");
                return;
            }
        }

        startTime = UnityEngine.Time.time;

        if (showDebugLogs)
        {
            Debug.Log("LiftSub: Initialized - waiting for ROS2 to be ready...");
            Debug.Log($"LiftSub: Will subscribe to: {jointStateTopic}");
            Debug.Log($"LiftSub: Monitoring joint: {jointNameToMonitor}");
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

        // Process new messages on main thread (logging, time tracking)
        if (newMessageReceived)
        {
            lastMessageTime = UnityEngine.Time.time;
            newMessageReceived = false;

            // Log first message immediately
            if (isFirstMessage && showDebugLogs)
            {
                Debug.Log($"LiftSub: ✓✓✓ FIRST MESSAGE RECEIVED! Message #{messageCount} with joint position: {currentJointPosition:F3}m");
                Debug.Log($"LiftSub: Topic: {jointStateTopic} is working!");
                isFirstMessage = false;
            }

            // Log subsequent messages (throttled)
            if (showDebugLogs && messageCount > 1 && UnityEngine.Time.frameCount % logFrequency == 0)
            {
                Debug.Log($"LiftSub: ✓ Received joint_states message #{messageCount}");
                Debug.Log($"LiftSub: 📊 {jointNameToMonitor} position: {currentJointPosition:F3} meters");
            }
        }

        // Check if we're receiving messages (diagnostic)
        if (isInitialized && showDebugLogs)
        {
            float timeSinceStart = UnityEngine.Time.time - startTime;
            float timeSinceLastMessage = UnityEngine.Time.time - lastMessageTime;

            // Warn if no messages received (ROS2 discovery can take 10-30 seconds with FastRTPS)
            if (!hasReceivedMessage && timeSinceStart > 10.0f && !timeoutWarningLogged)
            {
                Debug.LogWarning($"LiftSub:  No messages received after {timeSinceStart:F1} seconds!");
                Debug.LogWarning($"LiftSub: Current QoS: {(useSensorDataQoS ? "SENSOR_DATA" : "DEFAULT")}");
                Debug.LogWarning($"LiftSub: TROUBLESHOOTING:");
                Debug.LogWarning($"LiftSub: 1. Try toggling 'useSensorDataQoS' in Inspector (false = DEFAULT QoS)");
                Debug.LogWarning($"LiftSub: 2. Check Windows Firewall - may block FastRTPS multicast");
                Debug.LogWarning($"LiftSub: 3. Verify topic exists: ros2 topic list | grep joint_states");
                Debug.LogWarning($"LiftSub: 4. Verify robot is publishing: ros2 topic echo /stretch/joint_states");
                timeoutWarningLogged = true;
            }
            // Second warning after 30 seconds
            else if (!hasReceivedMessage && timeSinceStart > 30.0f && UnityEngine.Time.frameCount % 300 == 0)
            {
                Debug.LogWarning($"LiftSub:  Still no messages after {timeSinceStart:F1} seconds!");
                Debug.LogWarning($"LiftSub: This suggests ROS2 discovery failed. Check network/firewall.");
            }
            // Warn if messages stopped coming
            else if (hasReceivedMessage && timeSinceLastMessage > 10.0f && UnityEngine.Time.frameCount % 300 == 0)
            {
                Debug.LogWarning($"LiftSub:  No messages received for {timeSinceLastMessage:F1} seconds (last message #{messageCount})");
            }
        }
    }

    void InitializeROS2()
    {
        try
        {
            // Create ROS2 node
            ros2Node = ros2Unity.CreateNode("liftsub_node");

            // Sanitize topic name
            jointStateTopic = SanitizeTopicName(jointStateTopic);

            // Create subscriber for joint states
            // Stretch driver publishes with SENSOR_DATA QoS (best-effort)
            if (!string.IsNullOrEmpty(jointStateTopic))
            {
                QualityOfServiceProfile qos;
                string qosName;

                if (useSensorDataQoS)
                {
                    // SENSOR_DATA QoS (best-effort) - not recommended, robot publishes with RELIABLE
                    qos = new QualityOfServiceProfile(QosPresetProfile.SENSOR_DATA);
                    qosName = "SENSOR_DATA";
                }
                else
                {
                    // DEFAULT QoS (reliable) - matches stretch_driver's RELIABLE QoS
                    qos = new QualityOfServiceProfile(QosPresetProfile.DEFAULT);
                    qosName = "DEFAULT (RELIABLE)";
                }

                jointStateSubscriber = ros2Node.CreateSubscription<JointState>(
                    jointStateTopic,
                    OnJointStateReceived,
                    qos
                );

                if (showDebugLogs)
                {
                    Debug.Log($"LiftSub: Subscribed to joint states - {jointStateTopic}");
                    Debug.Log($"LiftSub: Using {qosName} QoS profile");
                    Debug.Log($"LiftSub: Will log {jointNameToMonitor} position every {logFrequency} frames");
                    Debug.Log($"LiftSub: Subscription created: {(jointStateSubscriber != null ? "YES" : "NO")}");
                }
            }
            else
            {
                Debug.LogError("LiftSub: No joint_states topic specified!");
                return;
            }

            isInitialized = true;

            if (showDebugLogs)
            {
                Debug.Log("LiftSub: ROS2 initialized successfully!");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"LiftSub: Failed to initialize ROS2: {e.Message}");
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
    /// Extracts the monitored joint position from the message
    /// NOTE: This runs on a background thread - only do thread-safe operations here!
    /// All Unity API calls (Time, Debug.Log, etc.) must be done in Update() on main thread
    /// </summary>
    void OnJointStateReceived(JointState msg)
    {
        // Thread-safe operations only (simple assignments)
        hasReceivedMessage = true;
        messageCount++;
        
        // Find and extract the monitored joint position and velocity
        bool foundJoint = false;
        for (int i = 0; i < msg.Name.Length; i++)
        {
            string jointName = msg.Name[i];

            if (jointName == jointNameToMonitor)
            {
                if (i < msg.Position.Length)
                {
                    currentJointPosition = (float)msg.Position[i];
                    foundJoint = true;
                }
                // Extract velocity if available (more accurate than calculating from deltas)
                if (i < msg.Velocity.Length)
                {
                    currentJointVelocity = (float)msg.Velocity[i];
                }
                break;
            }
        }

        // Set flags for main thread processing
        if (messageCount == 1)
        {
            isFirstMessage = true;
        }
        newMessageReceived = true;

        // Warn if monitored joint not found (Debug.Log is thread-safe in Unity)
        if (!foundJoint && messageCount == 1)
        {
            Debug.LogWarning($"LiftSub:  {jointNameToMonitor} not found in joint_states message!");
            Debug.LogWarning($"LiftSub: Available joints: {string.Join(", ", msg.Name)}");
        }
    }

    /// <summary>
    /// Get current joint position (can be called from other scripts like XdriveLift.cs)
    /// </summary>
    public float GetCurrentJointPosition()
    {
        return currentJointPosition;
    }

    /// <summary>
    /// Get current joint velocity from JointState message (more accurate than calculating from deltas)
    /// </summary>
    public float GetCurrentJointVelocity()
    {
        return currentJointVelocity;
    }

    /// <summary>
    /// Check if we've received any messages from robot
    /// </summary>
    public bool HasReceivedMessage()
    {
        return hasReceivedMessage;
    }

    /// <summary>
    /// Get the message count (for diagnostics)
    /// </summary>
    public int GetMessageCount()
    {
        return messageCount;
    }
}
