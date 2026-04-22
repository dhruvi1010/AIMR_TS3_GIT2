using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using ROS2;
using trajectory_msgs.msg;
using builtin_interfaces.msg;

/// <summary>
/// Control wrist yaw, pitch, and roll using Quest 3 left controller
/// Implements Option 3: Hybrid control with mode toggle
/// Publishes JointTrajectory to real Stretch3 robot via ROS2
/// Similar to LinkArm.cs and grippercontrolviz.cs but for wrist control
/// </summary>
public class wristButton : MonoBehaviour
{
    [Header("ROS2 Settings")]
    [Tooltip("Topic to publish wrist commands (action goal topic)")]
    public string wristCommandTopic = "/stretch_controller/follow_joint_trajectory/goal";

    [Header("Left Controller Inputs")]
    [Tooltip("Left hand joystick InputActionReference for wrist pitch/roll control (when in wrist mode)")]
    public InputActionReference leftHandJoystick;
    
    [Tooltip("Left hand trigger (analog) InputActionReference for wrist yaw positive")]
    public InputActionReference leftTrigger;
    
    [Tooltip("Right hand trigger (analog) InputActionReference for wrist yaw negative")]
    public InputActionReference rightTrigger;
    
    [Tooltip("Left hand Y button InputActionReference for toggling Wrist/Base mode")]
    public InputActionReference leftYButton;
    
    [Tooltip("Left hand X button InputActionReference for resetting wrist to center")]
    public InputActionReference leftXButton;

    [Header("Wrist Joint Limits (radians)")]
    [Tooltip("Wrist Yaw minimum angle (radians) - from URDF: -2.967 rad = -170°")]
    public float wristYawMin = -2.967f;
    
    [Tooltip("Wrist Yaw maximum angle (radians) - from URDF: 2.967 rad = 170°")]
    public float wristYawMax = 2.967f;
    
    [Tooltip("Wrist Pitch minimum angle (radians) - from URDF: -0.873 rad = -50°")]
    public float wristPitchMin = -0.873f;
    
    [Tooltip("Wrist Pitch maximum angle (radians) - from URDF: 0.873 rad = 50°")]
    public float wristPitchMax = 0.873f;
    
    [Tooltip("Wrist Roll minimum angle (radians) - from URDF: -2.967 rad = -170°")]
    public float wristRollMin = -2.967f;
    
    [Tooltip("Wrist Roll maximum angle (radians) - from URDF: 2.967 rad = 170°")]
    public float wristRollMax = 2.967f;

    [Header("Movement Settings")]
    [Tooltip("Wrist yaw rotation speed (radians per second) - Higher = faster response")]
    public float wristYawSpeed = 0.5f; // rad/s
    
    [Tooltip("Wrist pitch rotation speed (radians per second) - Higher = faster response")]
    public float wristPitchSpeed = 0.3f; // rad/s
    
    [Tooltip("Wrist roll rotation speed (radians per second) - Higher = faster response")]
    public float wristRollSpeed = 0.5f; // rad/s

    [Header("Trajectory Settings")]
    [Tooltip("Trajectory duration (seconds) - Lower = faster robot movement. Minimum recommended: 0.3s")]
    public float duration = 0.5f;

    [Header("Publishing Settings")]
    [Tooltip("Minimum time between publishes (seconds) - Match gamepad teleop: 0.05s = 20 Hz")]
    public float publishInterval = 0.05f;
    
    [Tooltip("Minimum angle change to trigger publish (radians) - Lower = more sensitive")]
    public float minAngleChange = 0.01f; // ~0.57 degrees
    
    [Tooltip("Dead zone for analog inputs (0.0 to 1.0) - Inputs below this are ignored")]
    public float analogDeadZone = 0.1f;
    
    [Tooltip("Button press threshold (0.0 to 1.0) - button value above this is considered pressed")]
    public float buttonPressThreshold = 0.5f;

    [Header("Mode Toggle")]
    [Tooltip("Current control mode: true = Wrist Mode, false = Base Mode")]
    public bool wristModeActive = false;

    [Header("GameObject References (Optional - for Unity Visualization)")]
    [Tooltip("Wrist yaw Transform for visualization (optional)")]
    public Transform WristYawTransform;
    
    [Tooltip("Wrist pitch Transform for visualization (optional)")]
    public Transform WristPitchTransform;
    
    [Tooltip("Wrist roll Transform for visualization (optional)")]
    public Transform WristRollTransform;

    [Header("Debug")]
    public bool showDebugLogs = true;

    // ROS2 components
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<JointTrajectory> wristPublisher;
    private bool isInitialized = false;

    // Internal state
    private float currentWristYaw = 0.0f;    // Current wrist yaw angle (radians)
    private float currentWristPitch = 0.0f;   // Current wrist pitch angle (radians)
    private float currentWristRoll = 0.0f;    // Current wrist roll angle (radians)
    
    private float lastPublishedYaw = 0.0f;
    private float lastPublishedPitch = 0.0f;
    private float lastPublishedRoll = 0.0f;
    private float lastPublishTime = 0.0f;
    
    // Button state tracking
    private bool lastYButtonState = false;
    private bool lastXButtonState = false;
    
    // Direct XR input (proven to work - like rightAbutttonTest.cs)
    private UnityEngine.XR.InputDevice leftControllerDevice;
    
    // Base rotation states for visualization (when wrist transforms are assigned)
    private Quaternion baseYawRotation = Quaternion.identity;
    private Quaternion basePitchRotation = Quaternion.identity;
    private Quaternion baseRollRotation = Quaternion.identity;

    void Start()
    {
        // Validate required inputs
        if (leftHandJoystick == null)
        {
            Debug.LogWarning("wristButton: Left hand joystick InputActionReference not assigned! Wrist pitch/roll control will not work.");
        }
        
        if (leftTrigger == null)
        {
            Debug.LogWarning("wristButton: Left trigger InputActionReference not assigned! Wrist yaw positive control will not work.");
        }
        
        if (rightTrigger == null)
        {
            Debug.LogWarning("wristButton: Right trigger InputActionReference not assigned! Wrist yaw negative control will not work.");
        }
        
        if (leftYButton == null)
        {
            Debug.LogWarning("wristButton: Left Y button InputActionReference not assigned! Mode toggle will not work.");
        }
        else
        {
            // Enable the action if not already enabled
            if (!leftYButton.action.enabled)
            {
                leftYButton.action.Enable();
                if (showDebugLogs)
                {
                    Debug.Log("wristButton: Left Y button action enabled.");
                }
            }
        }
        
        if (leftXButton == null)
        {
            Debug.LogWarning("wristButton: Left X button InputActionReference not assigned! Reset wrist function will not work.");
        }

        // Initialize Unity visualization rotations
        if (WristYawTransform != null)
        {
            baseYawRotation = WristYawTransform.localRotation;
        }
        if (WristPitchTransform != null)
        {
            basePitchRotation = WristPitchTransform.localRotation;
        }
        if (WristRollTransform != null)
        {
            baseRollRotation = WristRollTransform.localRotation;
        }

        // Initialize wrist positions to center (0 radians)
        currentWristYaw = 0.0f;
        currentWristPitch = 0.0f;
        currentWristRoll = 0.0f;
        lastPublishedYaw = currentWristYaw;
        lastPublishedPitch = currentWristPitch;
        lastPublishedRoll = currentWristRoll;

        // Sync initial mode with ControlModeManager
        ControlModeManager.SetMode(wristModeActive);

        // Initialize ROS2
        ros2Unity = GetComponent<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            ros2Unity = FindObjectOfType<ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                Debug.LogError("wristButton: ROS2UnityComponent not found! Add ROS2UnityCore to scene.");
                return;
            }
        }

        if (showDebugLogs)
        {
            Debug.Log("wristButton: Initialized");
            Debug.Log($"wristButton: Wrist Yaw limits: [{wristYawMin:F3}, {wristYawMax:F3}] rad ({wristYawMin * Mathf.Rad2Deg:F1}° to {wristYawMax * Mathf.Rad2Deg:F1}°)");
            Debug.Log($"wristButton: Wrist Pitch limits: [{wristPitchMin:F3}, {wristPitchMax:F3}] rad ({wristPitchMin * Mathf.Rad2Deg:F1}° to {wristPitchMax * Mathf.Rad2Deg:F1}°)");
            Debug.Log($"wristButton: Wrist Roll limits: [{wristRollMin:F3}, {wristRollMax:F3}] rad ({wristRollMin * Mathf.Rad2Deg:F1}° to {wristRollMax * Mathf.Rad2Deg:F1}°)");
            Debug.Log($"wristButton: Using working trajectory format (zero timestamp, empty arrays)");
            Debug.Log($"wristButton: Initial mode: {(wristModeActive ? "Wrist Mode" : "Base Mode")}");
            Debug.Log($"wristButton: Press Y button to toggle between Wrist Mode and Base Mode");
            Debug.Log($"wristButton: Press X button to reset wrist to center position");
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

        if (!isInitialized || wristPublisher == null)
            return;

        // Handle mode toggle (Y button) - Using direct XR input (proven working method)
        HandleYButtonInput();

        // Handle reset to center (X button) - Using direct XR input (same method as Y button)
        HandleXButtonInput();

        // Only process wrist controls when in wrist mode
        if (wristModeActive)
        {
            // Handle wrist yaw (triggers)
            HandleWristYawInput();
            
            // Handle wrist pitch and roll (left joystick)
            HandleWristPitchRollInput();
            
            // Update Unity visualization
            UpdateWristVisualization();
            
            // Publish commands with throttling
            PublishWristCommand();
        }
    }

    /// <summary>
    /// Handle Y button input using direct XR input (proven working method)
    /// </summary>
    void HandleYButtonInput()
    {
        // Get left controller directly (assumed to be always available)
        leftControllerDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

        // Read Y button directly using XR CommonUsages (SecondaryButton on left = Y button)
        // This is the proven working method from rightAbutttonTest.cs
        if (leftControllerDevice.isValid && 
            leftControllerDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool yButtonPressed))
        {
            // Debug: Log button state periodically
            if (showDebugLogs && UnityEngine.Time.frameCount % 60 == 0) // Log once per second
            {
                Debug.Log($"wristButton: Y Button (Direct XR) pressed: {yButtonPressed}, last state: {lastYButtonState}");
            }

            // Button just pressed (rising edge detection)
            if (yButtonPressed && !lastYButtonState)
            {
                // Button just pressed - toggle mode
                if (showDebugLogs)
                {
                    Debug.Log("wristButton: Y Button detected! Toggling mode...");
                }
                ToggleWristMode();
            }
            lastYButtonState = yButtonPressed;
        }
    }

    /// <summary>
    /// Handle X button input using direct XR input (same method as Y button)
    /// </summary>
    void HandleXButtonInput()
    {
        // Get left controller directly (assumed to be always available)
        leftControllerDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

        // Read X button directly using XR CommonUsages (PrimaryButton on left = X button)
        // This is the same proven working method as Y button
        if (leftControllerDevice.isValid && 
            leftControllerDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool xButtonPressed))
        {
            // Debug: Log button state periodically
            if (showDebugLogs && UnityEngine.Time.frameCount % 60 == 0) // Log once per second
            {
                Debug.Log($"wristButton: X Button (Direct XR) pressed: {xButtonPressed}, last state: {lastXButtonState}");
            }

            // Button just pressed (rising edge detection)
            if (xButtonPressed && !lastXButtonState)
            {
                // Button just pressed - reset wrist to center
                if (showDebugLogs)
                {
                    Debug.Log("wristButton: X Button detected! Resetting wrist to center...");
                }
                ResetWristToCenter();
            }
            lastXButtonState = xButtonPressed;
        }
    }

    void InitializeROS2()
    {
        try
        {
            // Create ROS2 node
            ros2Node = ros2Unity.CreateNode("wristcontrol_node");

            // Create publisher for wrist commands
            wristPublisher = ros2Node.CreatePublisher<JointTrajectory>(wristCommandTopic);

            if (showDebugLogs)
            {
                Debug.Log($"wristButton: ROS2 initialized successfully!");
                Debug.Log($"wristButton: Publisher created - {wristCommandTopic}");
            }

            isInitialized = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"wristButton: Failed to initialize ROS2: {e.Message}");
            Debug.LogError($"Stack trace: {e.StackTrace}");
        }
    }

    /// <summary>
    /// Toggle between Wrist Mode and Base Mode
    /// Updates both local state and global ControlModeManager
    /// </summary>
    void ToggleWristMode()
    {
        wristModeActive = !wristModeActive;
        
        // Update global mode manager (used by BaseJoyContoller)
        ControlModeManager.SetMode(wristModeActive);
        
        if (showDebugLogs)
        {
            Debug.Log($"wristButton: Mode switched to {(wristModeActive ? "Wrist Mode" : "Base Mode")}");
            if (wristModeActive)
            {
                Debug.Log("wristButton: Left joystick now controls wrist pitch/roll");
                Debug.Log("wristButton: Left/Right triggers control wrist yaw");
            }
            else
            {
                Debug.Log("wristButton: Left joystick now controls mobile base");
            }
        }
    }

    /// <summary>
    /// Reset all wrist joints to center position (0 radians)
    /// </summary>
    void ResetWristToCenter()
    {
        currentWristYaw = 0.0f;
        currentWristPitch = 0.0f;
        currentWristRoll = 0.0f;
        
        // Immediately send command to robot
        SendWristCommand(currentWristYaw, currentWristPitch, currentWristRoll);
        lastPublishedYaw = currentWristYaw;
        lastPublishedPitch = currentWristPitch;
        lastPublishedRoll = currentWristRoll;
        
        // Update visualization
        UpdateWristVisualization();
        
        if (showDebugLogs)
        {
            Debug.Log("wristButton: Wrist reset to center position (0, 0, 0)");
        }
    }

    /// <summary>
    /// Handle wrist yaw input from triggers
    /// Left trigger = positive yaw, Right trigger = negative yaw
    /// </summary>
    void HandleWristYawInput()
    {
        float yawInput = 0.0f;
        
        // Left trigger for positive yaw
        if (leftTrigger != null)
        {
            float leftTriggerValue = leftTrigger.action.ReadValue<float>();
            if (leftTriggerValue > analogDeadZone)
            {
                yawInput += leftTriggerValue * wristYawSpeed * UnityEngine.Time.deltaTime;
            }
        }
        
        // Right trigger for negative yaw
        if (rightTrigger != null)
        {
            float rightTriggerValue = rightTrigger.action.ReadValue<float>();
            if (rightTriggerValue > analogDeadZone)
            {
                yawInput -= rightTriggerValue * wristYawSpeed * UnityEngine.Time.deltaTime;
            }
        }
        
        // Update yaw position
        if (Mathf.Abs(yawInput) > 0.001f)
        {
            currentWristYaw += yawInput;
            currentWristYaw = Mathf.Clamp(currentWristYaw, wristYawMin, wristYawMax);
        }
    }

    /// <summary>
    /// Handle wrist pitch and roll input from left joystick
    /// Y-axis = pitch, X-axis = roll
    /// </summary>
    void HandleWristPitchRollInput()
    {
        if (leftHandJoystick == null)
            return;
        
        Vector2 joystickInput = leftHandJoystick.action.ReadValue<Vector2>();
        
        // Apply dead zone
        if (Mathf.Abs(joystickInput.x) < analogDeadZone)
            joystickInput.x = 0.0f;
        if (Mathf.Abs(joystickInput.y) < analogDeadZone)
            joystickInput.y = 0.0f;
        
        // Update pitch (Y-axis: up = positive pitch, down = negative pitch)
        if (Mathf.Abs(joystickInput.y) > 0.001f)
        {
            currentWristPitch += joystickInput.y * wristPitchSpeed * UnityEngine.Time.deltaTime;
            currentWristPitch = Mathf.Clamp(currentWristPitch, wristPitchMin, wristPitchMax);
        }
        
        // Update roll (X-axis: right = positive roll, left = negative roll)
        if (Mathf.Abs(joystickInput.x) > 0.001f)
        {
            currentWristRoll += joystickInput.x * wristRollSpeed * UnityEngine.Time.deltaTime;
            currentWristRoll = Mathf.Clamp(currentWristRoll, wristRollMin, wristRollMax);
        }
    }

    /// <summary>
    /// Update Unity visualization for wrist joints
    /// Based on Stretch 3 Hardware Guide and confirmed testing:
    /// - Wrist Yaw: Rotates around vertical axis (left/right) - Range: +256°/-76°
    /// - Wrist Pitch: Tilts tool up/down - Range: +20°/-90° - CONFIRMED: Y-axis works
    /// - Wrist Roll: Twists around tool's forward axis - Range: +172.5°/-172.5°
    /// 
    /// Initial rotations are already applied (baseYawRotation, basePitchRotation, baseRollRotation)
    /// After initial rotations, local coordinate axes are transformed
    /// </summary>
    void UpdateWristVisualization()
    {
        if (WristYawTransform != null)
        {
            // Yaw: Rotates around vertical axis (world Y = up/down)
            // After initial Z:180° rotation, vertical axis maps to local X or Z
            // Based on URDF axis (0,0,-1) and initial rotation, likely X-axis
            // Test with vizwrist.cs to confirm: Try X, Y, Z axes
            WristYawTransform.localRotation = baseYawRotation * Quaternion.Euler(currentWristYaw * Mathf.Rad2Deg, 0, 0);
        }
        
        if (WristPitchTransform != null)
        {
            // Pitch: CONFIRMED - Rotates around Y-axis (tilts tool up/down)
            // After initial Y:180°, Z:-90° rotations, Y-axis still corresponds to pitch
            WristPitchTransform.localRotation = basePitchRotation * Quaternion.Euler(0, currentWristPitch * Mathf.Rad2Deg, 0);
        }
        
        if (WristRollTransform != null)
        {
            // Roll: Rotates around tool's forward axis (twisting the tool)
            // After all rotations, forward axis likely maps to local Z-axis
            // Test with vizwrist.cs to confirm: Try X, Y, Z axes
            WristRollTransform.localRotation = baseRollRotation * Quaternion.Euler(0, 0, currentWristRoll * Mathf.Rad2Deg);
        }
    }

    /// <summary>
    /// Publish wrist command with rate limiting and position change threshold
    /// </summary>
    void PublishWristCommand()
    {
        float timeSinceLastPublish = UnityEngine.Time.time - lastPublishTime;
        
        // Calculate total angle change
        float yawChange = Mathf.Abs(currentWristYaw - lastPublishedYaw);
        float pitchChange = Mathf.Abs(currentWristPitch - lastPublishedPitch);
        float rollChange = Mathf.Abs(currentWristRoll - lastPublishedRoll);
        float totalChange = yawChange + pitchChange + rollChange;
        
        // Only publish if enough time passed AND position changed significantly
        if (timeSinceLastPublish >= publishInterval && totalChange >= minAngleChange)
        {
            SendWristCommand(currentWristYaw, currentWristPitch, currentWristRoll);
            lastPublishedYaw = currentWristYaw;
            lastPublishedPitch = currentWristPitch;
            lastPublishedRoll = currentWristRoll;
            lastPublishTime = UnityEngine.Time.time;
        }
    }

    /// <summary>
    /// Send wrist command matching the exact format of working manual commands
    /// Uses zero timestamp and empty arrays like LinkArm.cs and grippercontrolviz.cs
    /// </summary>
    void SendWristCommand(float yaw, float pitch, float roll)
    {
        if (wristPublisher == null || !isInitialized)
            return;

        // Clamp to valid ranges
        yaw = Mathf.Clamp(yaw, wristYawMin, wristYawMax);
        pitch = Mathf.Clamp(pitch, wristPitchMin, wristPitchMax);
        roll = Mathf.Clamp(roll, wristRollMin, wristRollMax);

        var trajectory = new JointTrajectory();

        // Set header - matching manual command format (empty frame_id, zero stamp)
        // THIS IS THE KEY - uses zero timestamp and empty frame_id like LinkArm.cs
        trajectory.Header = new std_msgs.msg.Header
        {
            Stamp = new builtin_interfaces.msg.Time
            {
                Sec = 0,
                Nanosec = 0
            },
            Frame_id = "" // Empty frame_id like manual command (not "base_link")
        };

        // Set joint names - all three wrist joints
        trajectory.Joint_names = new string[] 
        { 
            "joint_wrist_yaw",
            "joint_wrist_pitch",
            "joint_wrist_roll"
        };

        // Create trajectory point - matching exact manual command format
        // Manual command shows: positions: [yaw, pitch, roll], time_from_start: sec: 2, nanosec: 0
        // velocities, accelerations, effort are empty arrays in manual command
        var point = new JointTrajectoryPoint
        {
            Positions = new double[] 
            { 
                yaw,
                pitch,
                roll
            },
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
        wristPublisher.Publish(trajectory);

        if (showDebugLogs)
        {
            Debug.Log($"wristButton: Sent command - Yaw: {yaw:F3}rad ({yaw * Mathf.Rad2Deg:F1}°), Pitch: {pitch:F3}rad ({pitch * Mathf.Rad2Deg:F1}°), Roll: {roll:F3}rad ({roll * Mathf.Rad2Deg:F1}°)");
            
        }
    }
}
