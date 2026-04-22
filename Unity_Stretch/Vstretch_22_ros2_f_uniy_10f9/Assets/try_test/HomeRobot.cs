using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ROS2;
using std_msgs.msg;

/// <summary>
/// HomeRobot - Sends home command to Stretch 3 robot via ROS2 topic
/// 
/// This script provides a Unity interface to home the physical Stretch 3 robot.
/// It uses ROS2 topic communication following the pattern from robothome.txt:
/// - Unity sends intent ("home the robot") via topic
/// - Unity subscribes to status topic for feedback
/// - Unity updates state only from feedback
/// 
/// This follows the same pattern as unity_lift.py (topic-based commands)
/// 
/// REQUIREMENTS:
/// 1. stretch_home_topic.py must be running on the Stretch 3 robot
///    OR stretch_home_service.py for service-based approach
/// 2. ROS2 network must be configured (ROS_DOMAIN_ID, etc.)
/// 3. ROS2UnityComponent must be attached to this GameObject or parent
/// 
/// USAGE:
/// - Call Home() method to trigger homing
/// - Subscribe to OnHomingComplete/OnHomingFailed events for feedback
/// - Check isHoming flag to prevent multiple simultaneous requests
/// </summary>
public class HomeRobot : MonoBehaviour
{
    [Header("ROS2 Configuration")]
    [Tooltip("Topic to publish home command (must match topic on robot)")]
    public string homeCommandTopic = "/stretch/home_command";
    
    [Tooltip("Topic to subscribe for home status feedback")]
    public string homeStatusTopic = "/stretch/home_status";
    
    [Tooltip("Timeout for homing operation (seconds)")]
    public float homingTimeout = 60.0f; // Homing takes ~30 seconds, allow extra time
    
    [Header("UI Feedback")]
    [Tooltip("Show debug logs")]
    public bool showDebugLogs = true;
    
    [Header("State")]
    [Tooltip("Current homing state")]
    public bool isHoming = false;
    
    [Tooltip("Last homing result")]
    public bool lastHomingSuccess = false;
    
    [Tooltip("Last homing message")]
    public string lastHomingMessage = "";
    
    // ROS2 components
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<String> commandPublisher;
    private ISubscription<String> statusSubscription;
    
    // Events for external scripts to subscribe
    public System.Action<bool, string> OnHomingComplete;
    public System.Action<string> OnHomingFailed;
    
    // State tracking
    private float homingStartTime = 0f;
    private Coroutine timeoutCoroutine;
    
    // Thread-safe message queue (ROS2 callbacks run on background thread)
    private Queue<String> statusMessageQueue = new Queue<String>();
    private object queueLock = new object();
    
    void Start()
    {
        ros2Unity = GetComponent<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            ros2Unity = FindObjectOfType<ROS2UnityComponent>();
        }
        
        if (ros2Unity == null)
        {
            Debug.LogError("HomeRobot: ROS2UnityComponent not found! Please add ROS2UnityComponent to this GameObject or scene.");
            return;
        }
    }
    
    
    /// <summary>
    /// Callback when home status is received from robot
    /// NOTE: This runs on ROS2 background thread - we queue messages for main thread processing
    /// </summary>
    void OnHomeStatusReceived(String msg)
    {
        // Queue message for processing on main thread
        lock (queueLock)
        {
            statusMessageQueue.Enqueue(msg);
        }
    }
    
    /// <summary>
    /// Process queued status messages on main thread (called from Update)
    /// </summary>
    void ProcessStatusMessages()
    {
        // Process all queued messages
        while (true)
        {
            String msg = null;
            lock (queueLock)
            {
                if (statusMessageQueue.Count > 0)
                {
                    msg = statusMessageQueue.Dequeue();
                }
            }
            
            if (msg == null)
            {
                break; // No more messages
            }
            
            // Now we're on main thread - safe to use Unity APIs
            ProcessStatusMessage(msg);
        }
    }
    
    /// <summary>
    /// Process a single status message (runs on main thread)
    /// </summary>
    void ProcessStatusMessage(String msg)
    {
        // Debug: Log that we received ANY message
        if (showDebugLogs)
        {
            Debug.Log($"HomeRobot: Raw message received - msg is null: {msg == null}");
            if (msg != null)
            {
                Debug.Log($"HomeRobot: Raw message Data: '{msg.Data}' (length: {msg.Data?.Length ?? 0})");
            }
        }
        
        if (msg == null || string.IsNullOrEmpty(msg.Data))
        {
            if (showDebugLogs)
            {
                Debug.LogWarning("HomeRobot: Received null or empty status message");
            }
            return;
        }
        
        // Remove quotes if present (sometimes ROS2 adds quotes)
        string status = msg.Data.Trim().Trim('"').Trim('\'');
        
        if (showDebugLogs)
        {
            Debug.Log($"HomeRobot: Status received (cleaned): '{status}'");
        }
        
        if (status == "homing_started")
        {
            // Reset isHoming flag if it was stuck
            if (!isHoming)
            {
                if (showDebugLogs)
                {
                    Debug.Log("HomeRobot: Received 'homing_started' but wasn't expecting it - resetting state");
                }
                isHoming = true;
                homingStartTime = Time.realtimeSinceStartup;
            }
            
            if (showDebugLogs)
            {
                Debug.Log("HomeRobot: Robot confirmed homing started");
            }
        }
        else if (status == "homing_complete")
        {
            HandleHomingComplete();
        }
        else if (status.StartsWith("homing_failed:"))
        {
            string errorMsg = status.Substring("homing_failed:".Length).Trim();
            HandleHomingFailed(errorMsg);
        }
        else
        {
            // Unknown status - log it for debugging
            if (showDebugLogs)
            {
                Debug.LogWarning($"HomeRobot: Unknown status message: '{status}'");
            }
        }
    }
    
    /// <summary>
    /// Public method to trigger robot homing
    /// Returns true if command was sent, false if already homing
    /// </summary>
    public bool Home()
    {
        // Check if homing is stuck (been too long without status update)
        if (isHoming && homingStartTime > 0f)
        {
            float elapsed = Time.realtimeSinceStartup - homingStartTime;
            if (elapsed > homingTimeout)
            {
                if (showDebugLogs)
                {
                    Debug.LogWarning($"HomeRobot: Homing flag was stuck (no status for {elapsed:F1}s). Resetting and allowing new command.");
                }
                // Reset stuck state
                isHoming = false;
                homingStartTime = 0f;
            }
        }
        
        if (isHoming)
        {
            if (showDebugLogs)
            {
                float elapsed = homingStartTime > 0f ? (Time.realtimeSinceStartup - homingStartTime) : 0f;
                Debug.LogWarning($"HomeRobot: Homing already in progress (for {elapsed:F1}s), ignoring request. Press 'R' to force reset.");
            }
            return false;
        }
        
        if (commandPublisher == null)
        {
            Debug.LogError("HomeRobot: Command publisher not initialized. Is ROS2 connected?");
            OnHomingFailed?.Invoke("ROS2 not connected");
            return false;
        }
        
        // Send home command
        String command = new String();
        command.Data = "home"; // Any message triggers homing
        
        commandPublisher.Publish(command);
        
        // Set homing state
        isHoming = true;
        homingStartTime = Time.realtimeSinceStartup;
        lastHomingSuccess = false;
        lastHomingMessage = "Homing in progress...";
        
        if (showDebugLogs)
        {
            Debug.Log("HomeRobot: Home command sent to robot");
        }
        
        // Start timeout coroutine
        if (timeoutCoroutine != null)
        {
            StopCoroutine(timeoutCoroutine);
        }
        timeoutCoroutine = StartCoroutine(HomingTimeoutCoroutine());
        
        return true;
    }
    
    /// <summary>
    /// Handle successful homing completion
    /// </summary>
    void HandleHomingComplete()
    {
        if (!isHoming)
        {
            return; // Already handled or not expected
        }
        
        isHoming = false;
        homingStartTime = 0f;
        lastHomingSuccess = true;
        lastHomingMessage = "Homing completed successfully";
        
        if (timeoutCoroutine != null)
        {
            StopCoroutine(timeoutCoroutine);
            timeoutCoroutine = null;
        }
        
        if (showDebugLogs)
        {
            Debug.Log($"HomeRobot: {lastHomingMessage}");
        }
        
        OnHomingComplete?.Invoke(true, lastHomingMessage);
    }
    
    /// <summary>
    /// Handle homing failure
    /// </summary>
    void HandleHomingFailed(string errorMessage)
    {
        if (!isHoming && homingStartTime == 0f)
        {
            return; // Already handled
        }
        
        isHoming = false;
        homingStartTime = 0f;
        lastHomingSuccess = false;
        lastHomingMessage = errorMessage;
        
        if (timeoutCoroutine != null)
        {
            StopCoroutine(timeoutCoroutine);
            timeoutCoroutine = null;
        }
        
        if (showDebugLogs)
        {
            Debug.LogError($"HomeRobot: {lastHomingMessage}");
        }
        
        OnHomingFailed?.Invoke(lastHomingMessage);
    }
    
    /// <summary>
    /// Timeout coroutine - handles timeout if no status received
    /// </summary>
    IEnumerator HomingTimeoutCoroutine()
    {
        yield return new WaitForSecondsRealtime(homingTimeout);
        
        if (isHoming)
        {
            HandleHomingFailed($"Timeout: No status received after {homingTimeout} seconds");
        }
    }
    
    /// <summary>
    /// Check if homing is available (for UI state)
    /// </summary>
    public bool IsHomingAvailable()
    {
        return commandPublisher != null && !isHoming;
    }
    
    /// <summary>
    /// Force reset homing state (useful if stuck)
    /// </summary>
    public void ResetHomingState()
    {
        if (showDebugLogs)
        {
            Debug.Log("HomeRobot: Manually resetting homing state");
        }
        isHoming = false;
        homingStartTime = 0f;
        if (timeoutCoroutine != null)
        {
            StopCoroutine(timeoutCoroutine);
            timeoutCoroutine = null;
        }
    }
    
    void Update()
    {
        // Create ROS2 node and subscriptions when ROS2 is ready
        if (ros2Node == null && ros2Unity != null && ros2Unity.Ok())
        {
            ros2Node = ros2Unity.CreateNode("UnityHomeRobot");
            
            // Create publisher for home command
            commandPublisher = ros2Node.CreatePublisher<String>(homeCommandTopic);
            
            // Create subscription for home status
            QualityOfServiceProfile sensorQos = new QualityOfServiceProfile(QosPresetProfile.SENSOR_DATA);
            statusSubscription = ros2Node.CreateSubscription<String>(
                homeStatusTopic, 
                OnHomeStatusReceived,
                sensorQos
            );
            
            if (showDebugLogs)
            {
                Debug.Log($"HomeRobot: Initialized");
                Debug.Log($"  Command topic: {homeCommandTopic}");
                Debug.Log($"  Status topic: {homeStatusTopic}");
                Debug.Log($"  Subscription created: {statusSubscription != null}");
            }
        }
        
        // Process queued status messages on main thread
        ProcessStatusMessages();
        
        // Allow manual reset with 'R' key for debugging
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (isHoming)
            {
                ResetHomingState();
                Debug.Log("HomeRobot: State reset (pressed R key)");
            }
        }
        
        // Check for timeout
        if (isHoming && homingStartTime > 0f)
        {
            if (Time.realtimeSinceStartup - homingStartTime > homingTimeout)
            {
                Debug.LogError($"HomeRobot: Homing timed out after {homingTimeout} seconds");
                HandleHomingFailed("Timeout: No status received from robot");
            }
        }
    }
    
    void OnDestroy()
    {
        if (timeoutCoroutine != null)
        {
            StopCoroutine(timeoutCoroutine);
        }
    }
}
