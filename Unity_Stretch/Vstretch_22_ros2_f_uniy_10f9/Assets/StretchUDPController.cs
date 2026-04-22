using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;

// NOTE: This script uses Unity's built-in JSON utility (no external packages needed)
// For complex JSON, you might want Newtonsoft.Json, but this works for our data

public class StretchUDPController : MonoBehaviour
{
    // ============================================
    // CONFIGURATION - Inspector Editable
    // ============================================
    [Header("Network Settings")]
    [Tooltip("IP address of the machine running ROS 2 bridge")]
    public string rosIP = "172.31.1.83";  // stretch3: 3020

    [Tooltip("Port to receive robot state from ROS")]
    public int receivePort = 5005;

    [Tooltip("Port to send commands to ROS")]
    public int sendPort = 5006;

    [Header("Robot Transforms - Assign from Hierarchy")]
    [Tooltip("Base link of the robot (for odometry)")]
    public Transform baseLink;

    [Tooltip("Lift joint (moves vertically)")]
    public Transform jointLift;

    [Tooltip("Arm extension - can be wrist_extension or joint_arm_l0")]
    public Transform wristExtension;

    [Tooltip("Head pan joint")]
    public Transform jointHeadPan;

    [Tooltip("Head tilt joint")]
    public Transform jointHeadTilt;

    [Tooltip("Wrist yaw joint")]
    public Transform jointWristYaw;

    [Tooltip("Wrist pitch joint (if dex wrist)")]
    public Transform jointWristPitch;

    [Tooltip("Wrist roll joint (if dex wrist)")]
    public Transform jointWristRoll;

    [Tooltip("Left gripper finger")]
    public Transform jointGripperLeft;

    [Tooltip("Right gripper finger")]
    public Transform jointGripperRight;

    [Header("Control Settings")]
    [Tooltip("Linear velocity when moving forward/backward (m/s)")]
    public float linearSpeed = 0.2f;

    [Tooltip("Angular velocity when rotating (rad/s)")]
    public float angularSpeed = 0.5f;

    [Tooltip("Send commands at this rate (Hz)")]
    public float commandRate = 30f;

    [Header("Debug")]
    public bool showDebugLogs = false;

    // ============================================
    // PRIVATE VARIABLES
    // ============================================
    private UdpClient receiveClient;
    private UdpClient sendClient;
    private IPEndPoint sendEndPoint;
    private Thread receiveThread;
    private bool isRunning = true;

    // Thread-safe queue for received data
    private Queue<string> receivedDataQueue = new Queue<string>();
    private object queueLock = new object();

    // Joint state storage
    private Dictionary<string, float> jointPositions = new Dictionary<string, float>();

    // Command throttling
    private float commandTimer = 0f;

    // ============================================
    // UNITY LIFECYCLE
    // ============================================

    void Start()
    {
        Debug.Log("=== Stretch UDP Controller Starting ===");

        // Setup UDP receiver for robot state
        try
        {
            receiveClient = new UdpClient(receivePort);
            receiveThread = new Thread(new ThreadStart(ReceiveData));
            receiveThread.IsBackground = true;
            receiveThread.Start();
            Debug.Log($"✓ Listening for robot state on port {receivePort}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"✗ Failed to start UDP receiver: {e.Message}");
            return;
        }

        // Setup UDP sender for commands
        try
        {
            sendClient = new UdpClient();
            sendEndPoint = new IPEndPoint(IPAddress.Parse(rosIP), sendPort);
            Debug.Log($"✓ Sending commands to {rosIP}:{sendPort}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"✗ Failed to setup UDP sender: {e.Message}");
            return;
        }

        Debug.Log("=== Stretch UDP Controller Ready ===");
        Debug.Log("Controls: W/S = Forward/Back, A/D = Rotate Left/Right");
    }

    void Update()
    {
        // Process received robot state (in main thread for Unity API access)
        ProcessReceivedData();

        // Handle keyboard input and send commands
        HandleInput();
    }

    void OnApplicationQuit()
    {
        Debug.Log("Shutting down UDP controller...");
        isRunning = false;

        if (receiveThread != null && receiveThread.IsAlive)
            receiveThread.Abort();

        if (receiveClient != null)
            receiveClient.Close();

        if (sendClient != null)
            sendClient.Close();
    }

    // ============================================
    // NETWORK RECEIVING (Background Thread)
    // ============================================

    void ReceiveData()
    {
        Debug.Log("Receive thread started");

        while (isRunning)
        {
            try
            {
                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, receivePort);
                byte[] data = receiveClient.Receive(ref remoteEP);
                string json = Encoding.UTF8.GetString(data);

                // Add to queue for main thread processing
                lock (queueLock)
                {
                    receivedDataQueue.Enqueue(json);
                }
            }
            catch (System.Exception e)
            {
                if (isRunning) // Only log if not shutting down
                {
                    Debug.LogError($"UDP Receive Error: {e.Message}");
                }
            }
        }
    }

    void ProcessReceivedData()
    {
        // Process all queued messages
        lock (queueLock)
        {
            while (receivedDataQueue.Count > 0)
            {
                string json = receivedDataQueue.Dequeue();
                ParseRobotData(json);
            }
        }
    }

    // ============================================
    // JSON PARSING & ROBOT UPDATE
    // ============================================

    void ParseRobotData(string json)
    {
        try
        {
            // Simple JSON parsing without external libraries
            if (json.Contains("\"type\":\"joint_states\""))
            {
                ParseJointStates(json);
            }
            else if (json.Contains("\"type\":\"odom\""))
            {
                ParseOdometry(json);
            }
        }
        catch (System.Exception e)
        {
            if (showDebugLogs)
                Debug.LogError($"JSON Parse Error: {e.Message}");
        }
    }

    void ParseJointStates(string json)
    {
        // Extract joint names and positions using simple string parsing
        // Format: {"type":"joint_states","names":["joint1","joint2"],"positions":[0.1,0.2]}

        int namesStart = json.IndexOf("\"names\":[") + 9;
        int namesEnd = json.IndexOf("]", namesStart);
        string namesStr = json.Substring(namesStart, namesEnd - namesStart);

        int posStart = json.IndexOf("\"positions\":[") + 13;
        int posEnd = json.IndexOf("]", posStart);
        string posStr = json.Substring(posStart, posEnd - posStart);

        // Split into arrays
        string[] names = namesStr.Replace("\"", "").Split(',');
        string[] positions = posStr.Split(',');

        // Update joint dictionary
        for (int i = 0; i < names.Length && i < positions.Length; i++)
        {
            string jointName = names[i].Trim();
            if (float.TryParse(positions[i].Trim(), out float position))
            {
                jointPositions[jointName] = position;
            }
        }

        // Apply to robot visuals
        UpdateRobotVisuals();
    }

    void ParseOdometry(string json)
    {
        if (baseLink == null) return;

        try
        {
            // Extract position
            float x = ExtractFloat(json, "\"x\":", ",", "\"position\"");
            float y = ExtractFloat(json, "\"y\":", ",", "\"position\"");
            float z = ExtractFloat(json, "\"z\":", "}", "\"position\"");

            // Extract orientation
            float qx = ExtractFloat(json, "\"x\":", ",", "\"orientation\"");
            float qy = ExtractFloat(json, "\"y\":", ",", "\"orientation\"");
            float qz = ExtractFloat(json, "\"z\":", ",", "\"orientation\"");
            float qw = ExtractFloat(json, "\"w\":", "}", "\"orientation\"");

            // ROS coordinate system (right-handed Z-up) to Unity (left-handed Y-up)
            baseLink.position = new Vector3(x, z, y);
            baseLink.rotation = new Quaternion(-qx, -qz, -qy, qw);

            if (showDebugLogs)
                Debug.Log($"Odom: pos=({x:F2},{y:F2},{z:F2})");
        }
        catch (System.Exception e)
        {
            if (showDebugLogs)
                Debug.LogError($"Odom parse error: {e.Message}");
        }
    }

    float ExtractFloat(string json, string key, string endChar, string section)
    {
        int sectionStart = json.IndexOf(section);
        int keyStart = json.IndexOf(key, sectionStart) + key.Length;
        int keyEnd = json.IndexOf(endChar, keyStart);
        string valueStr = json.Substring(keyStart, keyEnd - keyStart).Trim();
        float.TryParse(valueStr, out float value);
        return value;
    }

    // ============================================
    // ROBOT VISUAL UPDATE
    // ============================================

    void UpdateRobotVisuals()
    {
        // Lift (prismatic joint - translates along local Y axis)
        if (jointLift != null && jointPositions.ContainsKey("joint_lift"))
        {
            Vector3 pos = jointLift.localPosition;
            pos.y = jointPositions["joint_lift"];
            jointLift.localPosition = pos;
        }

        // Arm extension (prismatic - translates along local X axis)
        if (wristExtension != null && jointPositions.ContainsKey("wrist_extension"))
        {
            Vector3 pos = wristExtension.localPosition;
            pos.x = jointPositions["wrist_extension"];
            wristExtension.localPosition = pos;
        }

        // Head pan (revolute - rotates around Z axis)
        if (jointHeadPan != null && jointPositions.ContainsKey("joint_head_pan"))
        {
            float angle = jointPositions["joint_head_pan"] * Mathf.Rad2Deg;
            jointHeadPan.localRotation = Quaternion.Euler(0, 0, angle);
        }

        // Head tilt (revolute - rotates around Y axis)
        if (jointHeadTilt != null && jointPositions.ContainsKey("joint_head_tilt"))
        {
            float angle = jointPositions["joint_head_tilt"] * Mathf.Rad2Deg;
            jointHeadTilt.localRotation = Quaternion.Euler(0, angle, 0);
        }

        // Wrist yaw
        if (jointWristYaw != null && jointPositions.ContainsKey("joint_wrist_yaw"))
        {
            float angle = jointPositions["joint_wrist_yaw"] * Mathf.Rad2Deg;
            jointWristYaw.localRotation = Quaternion.Euler(0, 0, angle);
        }

        // Wrist pitch (if dex wrist)
        if (jointWristPitch != null && jointPositions.ContainsKey("joint_wrist_pitch"))
        {
            float angle = jointPositions["joint_wrist_pitch"] * Mathf.Rad2Deg;
            jointWristPitch.localRotation = Quaternion.Euler(0, angle, 0);
        }

        // Wrist roll (if dex wrist)
        if (jointWristRoll != null && jointPositions.ContainsKey("joint_wrist_roll"))
        {
            float angle = jointPositions["joint_wrist_roll"] * Mathf.Rad2Deg;
            jointWristRoll.localRotation = Quaternion.Euler(angle, 0, 0);
        }

        // Gripper fingers
        if (jointGripperLeft != null && jointPositions.ContainsKey("joint_gripper_finger_left"))
        {
            float angle = jointPositions["joint_gripper_finger_left"] * Mathf.Rad2Deg;
            jointGripperLeft.localRotation = Quaternion.Euler(0, angle, 0);
        }

        if (jointGripperRight != null && jointPositions.ContainsKey("joint_gripper_finger_right"))
        {
            float angle = jointPositions["joint_gripper_finger_right"] * Mathf.Rad2Deg;
            jointGripperRight.localRotation = Quaternion.Euler(0, angle, 0);
        }
    }

    // ============================================
    // INPUT HANDLING & COMMAND SENDING
    // ============================================

    void HandleInput()
    {
        commandTimer += Time.deltaTime;

        // Throttle command rate
        if (commandTimer < 1f / commandRate)
            return;

        commandTimer = 0f;

        // Get keyboard input
        float linear = 0f;
        float angular = 0f;

        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            linear = linearSpeed;
        else if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            linear = -linearSpeed;

        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            angular = angularSpeed;
        else if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            angular = -angularSpeed;

        // Only send if there's input
        if (linear != 0f || angular != 0f)
        {
            SendCmdVel(linear, angular);
        }
    }

    void SendCmdVel(float linear, float angular)
    {
        // Create simple JSON manually (no external library needed)
        string json = $"{{\"type\":\"cmd_vel\",\"linear\":{linear},\"angular\":{angular}}}";

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(json);
            sendClient.Send(data, data.Length, sendEndPoint);

            if (showDebugLogs)
                Debug.Log($"Sent cmd_vel: linear={linear:F2}, angular={angular:F2}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error sending cmd_vel: {e.Message}");
        }
    }

    // ============================================
    // PUBLIC API (for external scripts)
    // ============================================

    public void PublishCmdVel(float linear, float angular)
    {
        SendCmdVel(linear, angular);
    }

    public float GetJointPosition(string jointName)
    {
        if (jointPositions.ContainsKey(jointName))
            return jointPositions[jointName];
        return 0f;
    }
}