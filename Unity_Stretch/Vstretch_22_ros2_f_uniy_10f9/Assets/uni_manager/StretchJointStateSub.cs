using UnityEngine;
using ROS2;
using sensor_msgs.msg;
using System.Collections.Generic;

/// <summary>
/// Subscribe to Stretch3 robot's /stretch/joint_states topic to get all joint data in real time
/// Message type: sensor_msgs/JointState
/// Topic: /stretch/joint_states
/// </summary>
public class StretchJointStateSub : MonoBehaviour
{
    [Header("ROS2 Settings")]
    [Tooltip("Topic to subscribe for joint states")]
    public string jointStateTopic = "/stretch/joint_states";

    [Tooltip("QoS Profile: DEFAULT (reliable) or SENSOR_DATA (best-effort)")]
    public bool useSensorDataQoS = false;

    [Header("Logging Settings")]
    [Tooltip("Enable logging of all joint state data")]
    public bool enableLogging = true;

    [Tooltip("Log frequency (frames) - logs every N frames to reduce spam")]
    public int logFrequency = 60; // Log once per second at 60fps

    // ROS2 components
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private ISubscription<JointState> jointStateSubscriber;
    private bool isInitialized = false;

    // Thread-safe joint state storage
    private Dictionary<string, float> jointPositions = new Dictionary<string, float>();
    private Dictionary<string, float> jointVelocities = new Dictionary<string, float>();
    private Dictionary<string, float> jointEfforts = new Dictionary<string, float>();
    private bool hasReceivedMessage = false;
    private int messageCount = 0;

    // Thread-safe flags for main thread logging
    private bool newMessageReceived = false;
    private bool isFirstMessage = false;

    void Start()
    {
        ros2Unity = GetComponent<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            ros2Unity = FindObjectOfType<ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                Debug.LogError("StretchJointStateSub: ROS2UnityComponent not found!");
                return;
            }
        }
    }

    void Update()
    {
        if (!isInitialized && ros2Unity != null && ros2Unity.Ok())
        {
            InitializeROS2();
        }

        // Process new messages on main thread (logging)
        if (newMessageReceived && enableLogging)
        {
            newMessageReceived = false;

            // Log first message immediately
            if (isFirstMessage)
            {
                LogAllJointData("FIRST MESSAGE");
                isFirstMessage = false;
            }
            // Log subsequent messages (throttled)
            else if (UnityEngine.Time.frameCount % logFrequency == 0)
            {
                LogAllJointData($"Message #{messageCount}");
            }
        }
    }

    void InitializeROS2()
    {
        try
        {
            ros2Node = ros2Unity.CreateNode("stretch_joint_state_sub_node");

            QualityOfServiceProfile qos = useSensorDataQoS
                ? new QualityOfServiceProfile(QosPresetProfile.SENSOR_DATA)
                : new QualityOfServiceProfile(QosPresetProfile.DEFAULT);

            jointStateSubscriber = ros2Node.CreateSubscription<JointState>(
                jointStateTopic,
                OnJointStateReceived,
                qos
            );

            isInitialized = true;
            Debug.Log($"StretchJointStateSub: Subscribed to {jointStateTopic}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"StretchJointStateSub: Failed to initialize: {e.Message}");
        }
    }

    void OnJointStateReceived(JointState msg)
    {
        hasReceivedMessage = true;
        messageCount++;

        // Clear and update all joint data
        jointPositions.Clear();
        jointVelocities.Clear();
        jointEfforts.Clear();

        for (int i = 0; i < msg.Name.Length; i++)
        {
            string jointName = msg.Name[i];

            if (i < msg.Position.Length)
                jointPositions[jointName] = (float)msg.Position[i];

            if (i < msg.Velocity.Length)
                jointVelocities[jointName] = (float)msg.Velocity[i];

            if (i < msg.Effort.Length)
                jointEfforts[jointName] = (float)msg.Effort[i];
        }

        // Set flags for main thread processing
        if (messageCount == 1)
        {
            isFirstMessage = true;
        }
        newMessageReceived = true;
    }

    void LogAllJointData(string header)
    {
        Debug.Log($"=== StretchJointStateSub: {header} ===");
        Debug.Log($"Total Joints: {jointPositions.Count}");
        Debug.Log($"Message Count: {messageCount}");

        foreach (var jointName in jointPositions.Keys)
        {
            string logLine = $"  {jointName}:";
            
            if (jointPositions.ContainsKey(jointName))
                logLine += $" Pos={jointPositions[jointName]:F4}";
            
            if (jointVelocities.ContainsKey(jointName))
                logLine += $" Vel={jointVelocities[jointName]:F4}";
            
            if (jointEfforts.ContainsKey(jointName))
                logLine += $" Eff={jointEfforts[jointName]:F4}";
            
            Debug.Log(logLine);
        }
        
        Debug.Log("=== End Joint State Data ===");
    }

    /// <summary>
    /// Get position of a specific joint by name
    /// </summary>
    public float GetJointPosition(string jointName)
    {
        return jointPositions.TryGetValue(jointName, out float position) ? position : 0.0f;
    }

    /// <summary>
    /// Get velocity of a specific joint by name
    /// </summary>
    public float GetJointVelocity(string jointName)
    {
        return jointVelocities.TryGetValue(jointName, out float velocity) ? velocity : 0.0f;
    }

    /// <summary>
    /// Get effort of a specific joint by name
    /// </summary>
    public float GetJointEffort(string jointName)
    {
        return jointEfforts.TryGetValue(jointName, out float effort) ? effort : 0.0f;
    }

    /// <summary>
    /// Get all joint positions as a dictionary
    /// </summary>
    public Dictionary<string, float> GetAllJointPositions()
    {
        return new Dictionary<string, float>(jointPositions);
    }

    /// <summary>
    /// Get all joint velocities as a dictionary
    /// </summary>
    public Dictionary<string, float> GetAllJointVelocities()
    {
        return new Dictionary<string, float>(jointVelocities);
    }

    /// <summary>
    /// Get all joint efforts as a dictionary
    /// </summary>
    public Dictionary<string, float> GetAllJointEfforts()
    {
        return new Dictionary<string, float>(jointEfforts);
    }

    /// <summary>
    /// Get list of all joint names
    /// </summary>
    public List<string> GetAllJointNames()
    {
        return new List<string>(jointPositions.Keys);
    }

    /// <summary>
    /// Check if a specific joint exists
    /// </summary>
    public bool HasJoint(string jointName)
    {
        return jointPositions.ContainsKey(jointName);
    }

    /// <summary>
    /// Check if messages have been received
    /// </summary>
    public bool HasReceivedMessage()
    {
        return hasReceivedMessage;
    }

    /// <summary>
    /// Get total message count
    /// </summary>
    public int GetMessageCount()
    {
        return messageCount;
    }

    void OnDestroy()
    {
        if (jointStateSubscriber != null)
        {
            jointStateSubscriber.Dispose();
        }
    }
}
