using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using ROS2;
using trajectory_msgs.msg;
using builtin_interfaces.msg;

/// <summary>
/// Synchronized wrist control: Joystick input controls real robot, Unity visualization syncs with real robot position
/// Uses ArticulationBody X Drive method for accurate physics-based visualization
/// Controls all three wrist joints: yaw, pitch, and roll (Dexterous Wrist)
/// - Joystick input → Sends commands to real robot via ROS2
/// - Real robot position from StretchJointStateSub → Syncs Unity visualization
/// This ensures Unity always shows what the robot is actually doing (perfect sync)
/// </summary>
public class xdrivewrist : MonoBehaviour
{
    [Header("ROS2 Settings")]
    [Tooltip("Topic to publish wrist commands (action goal topic)")]
    public string wristCommandTopic = "/stretch_controller/follow_joint_trajectory/goal";

    [Header("StretchJointStateSub Reference")]
    [Tooltip("StretchJointStateSub component that subscribes to /stretch/joint_states - REQUIRED")]
    public StretchJointStateSub jointStateSubscriber;

    [Header("ArticulationBody References")]
    [Tooltip("ArticulationBody for link_wrist_yaw - REQUIRED for wrist yaw visualization")]
    public ArticulationBody wristYawArticulation;

    [Tooltip("ArticulationBody for link_wrist_pitch - REQUIRED for wrist pitch visualization")]
    public ArticulationBody wristPitchArticulation;

    [Tooltip("ArticulationBody for link_wrist_roll - REQUIRED for wrist roll visualization")]
    public ArticulationBody wristRollArticulation;

    [Header("Auto-Find Settings")]
    [Tooltip("Automatically find StretchJointStateSub component in scene")]
    public bool autoFindJointStateSub = true;

    [Tooltip("Automatically find ArticulationBody components by GameObject name")]
    public bool autoFindArticulations = true;

    [Header("Joystick Input")]
    [Tooltip("Left hand joystick InputActionReference for wrist pitch/roll control")]
    public InputActionReference leftHandJoystick;

    [Tooltip("Left hand trigger InputActionReference for wrist yaw positive")]
    public InputActionReference leftTrigger;

    [Tooltip("Right hand trigger InputActionReference for wrist yaw negative")]
    public InputActionReference rightTrigger;

    [Header("Mode Toggle")]
    [Tooltip("Current control mode: true = Wrist Mode, false = Base Mode")]
    public bool wristModeActive = false;

    [Header("ArticulationBody Drive Settings")]
    [Tooltip("Stiffness for wrist joints (higher = stiffer, more responsive) - Typical range: 100-1000 for revolute joints")]
    public float stiffness = 500f;

    [Tooltip("Damping for wrist joints (higher = more damped, smoother) - Typical range: 10-100 for revolute joints")]
    public float damping = 50f;

    [Tooltip("Force limit for wrist joints (torque limit in N⋅m) - Typical range: 10-50 for revolute joints")]
    public float forceLimit = 50f;

    [Header("Movement Settings")]
    [Tooltip("Wrist yaw rotation speed (radians per second) - Higher = faster robot responsiveness")]
    public float wristYawSpeed = 0.5f; // rad/s

    [Tooltip("Wrist pitch rotation speed (radians per second)")]
    public float wristPitchSpeed = 0.3f; // rad/s

    [Tooltip("Wrist roll rotation speed (radians per second)")]
    public float wristRollSpeed = 0.5f; // rad/s

    [Header("Trajectory Settings")]
    [Tooltip("Trajectory duration (seconds) - Lower = faster robot movement")]
    public float duration = 0.5f;

    [Header("Publishing Settings")]
    [Tooltip("Minimum time between publishes (seconds) - Match gamepad teleop: 0.05s = 20 Hz")]
    public float publishInterval = 0.05f;

    [Tooltip("Minimum angle change to trigger publish (radians)")]
    public float minPublishAngleChange = 0.01f; // ~0.57 degrees

    [Header("Wrist Joint Limits (radians) - From URDF")]
    [Tooltip("Wrist Yaw minimum angle (radians) - From URDF: -1.75 rad = -100.3°")]
    public float wristYawMin = -1.75f;

    [Tooltip("Wrist Yaw maximum angle (radians) - From URDF: 4.0 rad = 229.2°")]
    public float wristYawMax = 4.0f;

    [Tooltip("Wrist Pitch minimum angle (radians) - From URDF: -1.57 rad = -90°")]
    public float wristPitchMin = -1.57f;

    [Tooltip("Wrist Pitch maximum angle (radians) - From URDF: 0.56 rad = 32.1°")]
    public float wristPitchMax = 0.56f;

    [Tooltip("Wrist Roll minimum angle (radians) - From URDF: -3.14 rad = -180°")]
    public float wristRollMin = -3.14f;

    [Tooltip("Wrist Roll maximum angle (radians) - From URDF: 3.14 rad = 180°")]
    public float wristRollMax = 3.14f;

    [Header("Debug")]
    public bool showDebugLogs = true;

    // ROS2 components
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<JointTrajectory> wristPublisher;
    private bool isInitialized = false;

    // Internal state
    private float targetWristYaw = 0.0f;      // Target position from real robot (for Unity visualization)
    private float targetWristPitch = 0.0f;
    private float targetWristRoll = 0.0f;
    private float currentWristYaw = 0.0f;      // Current Unity visualization position
    private float currentWristPitch = 0.0f;
    private float currentWristRoll = 0.0f;
    private float commandWristYaw = 0.0f;      // Position to send to robot (from joystick)
    private float commandWristPitch = 0.0f;
    private float commandWristRoll = 0.0f;
    private float lastPublishedYaw = 0.0f;
    private float lastPublishedPitch = 0.0f;
    private float lastPublishedRoll = 0.0f;
    private float lastPublishTime = 0.0f;
    private bool wristYawFound = false;
    private bool wristPitchFound = false;
    private bool wristRollFound = false;
    private bool jointStateSubFound = false;

    // Mode toggle state
    private bool lastYButtonState = false;
    private UnityEngine.XR.InputDevice leftControllerDevice;

    // Joint names from URDF
    private const string JOINT_WRIST_YAW = "joint_wrist_yaw";
    private const string JOINT_WRIST_PITCH = "joint_wrist_pitch";
    private const string JOINT_WRIST_ROLL = "joint_wrist_roll";

    void Start()
    {
        // Validate required inputs
        if (leftHandJoystick == null)
        {
            Debug.LogWarning("xdrivewrist: Left hand joystick InputActionReference not assigned!");
        }
        if (leftTrigger == null)
        {
            Debug.LogWarning("xdrivewrist: Left trigger InputActionReference not assigned!");
        }
        if (rightTrigger == null)
        {
            Debug.LogWarning("xdrivewrist: Right trigger InputActionReference not assigned!");
        }

        // Auto-find StretchJointStateSub component if enabled
        if (autoFindJointStateSub && jointStateSubscriber == null)
        {
            jointStateSubscriber = FindObjectOfType<StretchJointStateSub>();
            if (jointStateSubscriber != null && showDebugLogs)
            {
                Debug.Log("xdrivewrist: Auto-found StretchJointStateSub component");
            }
        }

        // Validate StretchJointStateSub is found
        if (jointStateSubscriber == null)
        {
            Debug.LogError("xdrivewrist: StretchJointStateSub component not found! Assign in Inspector or enable auto-find.");
        }
        else
        {
            jointStateSubFound = true;
        }

        // Auto-find ArticulationBody components if enabled
        if (autoFindArticulations)
        {
            FindWristArticulations();
        }

        // Validate articulations are found
        ValidateArticulations();

        // Initialize ArticulationBody drive parameters
        if (wristYawFound)
        {
            InitializeDrive(wristYawArticulation, "Yaw");
        }
        if (wristPitchFound)
        {
            InitializeDrive(wristPitchArticulation, "Pitch");
        }
        if (wristRollFound)
        {
            InitializeDrive(wristRollArticulation, "Roll");
        }

        // Load current positions from ArticulationBodies
        LoadCurrentPositions();

        // Sync initial mode with ControlModeManager
        ControlModeManager.SetMode(wristModeActive);

        // Initialize ROS2
        ros2Unity = GetComponent<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            ros2Unity = FindObjectOfType<ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                Debug.LogError("xdrivewrist: ROS2UnityComponent not found! Add ROS2UnityCore to scene.");
            }
        }

        if (showDebugLogs)
        {
            Debug.Log("xdrivewrist: Initialized");
            Debug.Log($"xdrivewrist: Wrist Yaw limits: [{wristYawMin:F3}, {wristYawMax:F3}] rad ({wristYawMin * Mathf.Rad2Deg:F1}° to {wristYawMax * Mathf.Rad2Deg:F1}°)");
            Debug.Log($"xdrivewrist: Wrist Pitch limits: [{wristPitchMin:F3}, {wristPitchMax:F3}] rad ({wristPitchMin * Mathf.Rad2Deg:F1}° to {wristPitchMax * Mathf.Rad2Deg:F1}°)");
            Debug.Log($"xdrivewrist: Wrist Roll limits: [{wristRollMin:F3}, {wristRollMax:F3}] rad ({wristRollMin * Mathf.Rad2Deg:F1}° to {wristRollMax * Mathf.Rad2Deg:F1}°)");
            Debug.Log($"xdrivewrist: Using ArticulationBody X Drive for synchronized control");
            if (jointStateSubFound)
            {
                Debug.Log($"xdrivewrist: StretchJointStateSub found - will sync Unity from /stretch/joint_states");
            }
            else
            {
                Debug.LogError("xdrivewrist: StretchJointStateSub not found!");
            }
            if (wristYawFound && wristPitchFound && wristRollFound)
            {
                Debug.Log($"xdrivewrist: All wrist ArticulationBodies found");
            }
            else
            {
                Debug.LogError($"xdrivewrist: Missing ArticulationBodies - Yaw: {wristYawFound}, Pitch: {wristPitchFound}, Roll: {wristRollFound}");
            }
            Debug.Log($"xdrivewrist: Initial mode: {(wristModeActive ? "Wrist Mode" : "Base Mode")}");
            Debug.Log($"xdrivewrist: Press Y button (left controller) to toggle between Wrist Mode and Base Mode");
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

        // Handle mode toggle (Y button) - Using direct XR input (proven working method)
        HandleYButtonInput();

        // Only process wrist controls when in wrist mode
        if (wristModeActive)
        {
            // Handle joystick input and send commands to robot
            if (isInitialized && wristPublisher != null)
            {
                HandleJoystickInput();
            }
        }

        // Always sync Unity visualization with real robot position (regardless of mode)
        if (jointStateSubFound && (wristYawFound || wristPitchFound || wristRollFound))
        {
            SyncUnityVisualization();
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
        // This is the proven working method from wristButton.cs
        if (leftControllerDevice.isValid && 
            leftControllerDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool yButtonPressed))
        {
            // Button just pressed (rising edge detection)
            if (yButtonPressed && !lastYButtonState)
            {
                // Button just pressed - toggle mode
                if (showDebugLogs)
                {
                    Debug.Log("xdrivewrist: Y Button detected! Toggling mode...");
                }
                ToggleWristMode();
            }
            lastYButtonState = yButtonPressed;
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
            Debug.Log($"xdrivewrist: Mode switched to {(wristModeActive ? "Wrist Mode" : "Base Mode")}");
            if (wristModeActive)
            {
                Debug.Log("xdrivewrist: Left joystick now controls wrist pitch/roll");
                Debug.Log("xdrivewrist: Left/Right triggers control wrist yaw");
            }
            else
            {
                Debug.Log("xdrivewrist: Left joystick now controls mobile base");
            }
        }
    }

    /// <summary>
    /// Handle joystick input and send commands to real robot
    /// </summary>
    void HandleJoystickInput()
    {
        bool hasInput = false;

        // Handle wrist yaw from triggers
        float yawInput = 0.0f;
        if (leftTrigger != null)
        {
            float leftTriggerValue = leftTrigger.action.ReadValue<float>();
            if (leftTriggerValue > 0.1f)
            {
                yawInput += leftTriggerValue * wristYawSpeed * UnityEngine.Time.deltaTime;
                hasInput = true;
            }
        }
        if (rightTrigger != null)
        {
            float rightTriggerValue = rightTrigger.action.ReadValue<float>();
            if (rightTriggerValue > 0.1f)
            {
                yawInput -= rightTriggerValue * wristYawSpeed * UnityEngine.Time.deltaTime;
                hasInput = true;
            }
        }

        if (Mathf.Abs(yawInput) > 0.001f)
        {
            commandWristYaw += yawInput;
            commandWristYaw = Mathf.Clamp(commandWristYaw, wristYawMin, wristYawMax);
        }

        // Handle wrist pitch and roll from left joystick
        if (leftHandJoystick != null)
        {
            Vector2 joystickInput = leftHandJoystick.action.ReadValue<Vector2>();
            
            // Apply dead zone
            if (Mathf.Abs(joystickInput.x) < 0.1f)
                joystickInput.x = 0.0f;
            if (Mathf.Abs(joystickInput.y) < 0.1f)
                joystickInput.y = 0.0f;

            // Pitch (Y-axis: up = positive pitch, down = negative pitch)
            if (Mathf.Abs(joystickInput.y) > 0.001f)
            {
                commandWristPitch += joystickInput.y * wristPitchSpeed * UnityEngine.Time.deltaTime;
                commandWristPitch = Mathf.Clamp(commandWristPitch, wristPitchMin, wristPitchMax);
                hasInput = true;
            }

            // Roll (X-axis: right = positive roll, left = negative roll)
            if (Mathf.Abs(joystickInput.x) > 0.001f)
            {
                commandWristRoll += joystickInput.x * wristRollSpeed * UnityEngine.Time.deltaTime;
                commandWristRoll = Mathf.Clamp(commandWristRoll, wristRollMin, wristRollMax);
                hasInput = true;
            }
        }

        // Publish to robot with throttling
        if (hasInput)
        {
            float timeSinceLastPublish = UnityEngine.Time.time - lastPublishTime;
            float yawChange = Mathf.Abs(commandWristYaw - lastPublishedYaw);
            float pitchChange = Mathf.Abs(commandWristPitch - lastPublishedPitch);
            float rollChange = Mathf.Abs(commandWristRoll - lastPublishedRoll);
            float totalChange = yawChange + pitchChange + rollChange;

            // Rate limiting: Only publish if enough time passed AND position changed significantly
            if (timeSinceLastPublish >= publishInterval && totalChange >= minPublishAngleChange)
            {
                SendCommand(commandWristYaw, commandWristPitch, commandWristRoll);
                lastPublishedYaw = commandWristYaw;
                lastPublishedPitch = commandWristPitch;
                lastPublishedRoll = commandWristRoll;
                lastPublishTime = UnityEngine.Time.time;

                if (showDebugLogs && UnityEngine.Time.frameCount % 60 == 0)
                {
                    Debug.Log($"xdrivewrist: Sent command - Yaw: {commandWristYaw:F3}rad, Pitch: {commandWristPitch:F3}rad, Roll: {commandWristRoll:F3}rad");
                }
            }
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
                Debug.LogWarning("xdrivewrist: Waiting for joint_states messages from robot...");
            }
            return;
        }

        // Check if joints exist in joint_states (first time only)
        if (showDebugLogs && UnityEngine.Time.frameCount % 300 == 0) // Log every 5 seconds
        {
            bool hasYaw = jointStateSubscriber.HasJoint(JOINT_WRIST_YAW);
            bool hasPitch = jointStateSubscriber.HasJoint(JOINT_WRIST_PITCH);
            bool hasRoll = jointStateSubscriber.HasJoint(JOINT_WRIST_ROLL);
            Debug.Log($"xdrivewrist: Joint availability - Yaw: {hasYaw}, Pitch: {hasPitch}, Roll: {hasRoll}");
            Debug.Log($"xdrivewrist: ArticulationBody status - Yaw: {wristYawFound}, Pitch: {wristPitchFound}, Roll: {wristRollFound}");
        }

        // Get real robot positions from StretchJointStateSub
        float realRobotYaw = jointStateSubscriber.GetJointPosition(JOINT_WRIST_YAW);
        float realRobotPitch = jointStateSubscriber.GetJointPosition(JOINT_WRIST_PITCH);
        float realRobotRoll = jointStateSubscriber.GetJointPosition(JOINT_WRIST_ROLL);

        // Clamp to valid ranges
        realRobotYaw = Mathf.Clamp(realRobotYaw, wristYawMin, wristYawMax);
        realRobotPitch = Mathf.Clamp(realRobotPitch, wristPitchMin, wristPitchMax);
        realRobotRoll = Mathf.Clamp(realRobotRoll, wristRollMin, wristRollMax);

        // Update target positions for Unity visualization
        targetWristYaw = realRobotYaw;
        targetWristPitch = realRobotPitch;
        targetWristRoll = realRobotRoll;

        // Instant sync - Unity visualization immediately matches real robot position
        currentWristYaw = targetWristYaw;
        currentWristPitch = targetWristPitch;
        currentWristRoll = targetWristRoll;

        // Always update ArticulationBody every frame for best synchronization
        UpdateArticulationDrives();

        if (showDebugLogs && UnityEngine.Time.frameCount % 60 == 0) // Log once per second
        {
            Debug.Log($"xdrivewrist: Synced - Real Robot Yaw: {realRobotYaw:F3}rad | Unity: {currentWristYaw:F3}rad | Command: {commandWristYaw:F3}rad");
            Debug.Log($"xdrivewrist: Synced - Real Robot Pitch: {realRobotPitch:F3}rad | Unity: {currentWristPitch:F3}rad | Command: {commandWristPitch:F3}rad");
            Debug.Log($"xdrivewrist: Synced - Real Robot Roll: {realRobotRoll:F3}rad | Unity: {currentWristRoll:F3}rad | Command: {commandWristRoll:F3}rad");
        }
    }

    /// <summary>
    /// Auto-find ArticulationBody components by GameObject name
    /// </summary>
    void FindWristArticulations()
    {
        if (wristYawArticulation == null)
        {
            GameObject yawObj = GameObject.Find("link_wrist_yaw");
            if (yawObj != null)
            {
                wristYawArticulation = yawObj.GetComponent<ArticulationBody>();
                if (wristYawArticulation != null && showDebugLogs)
                {
                    Debug.Log($"xdrivewrist: Auto-found wristYawArticulation on {yawObj.name}");
                }
            }
        }

        if (wristPitchArticulation == null)
        {
            GameObject pitchObj = GameObject.Find("link_wrist_pitch");
            if (pitchObj != null)
            {
                wristPitchArticulation = pitchObj.GetComponent<ArticulationBody>();
                if (wristPitchArticulation != null && showDebugLogs)
                {
                    Debug.Log($"xdrivewrist: Auto-found wristPitchArticulation on {pitchObj.name}");
                }
            }
        }

        if (wristRollArticulation == null)
        {
            GameObject rollObj = GameObject.Find("link_wrist_roll");
            if (rollObj != null)
            {
                wristRollArticulation = rollObj.GetComponent<ArticulationBody>();
                if (wristRollArticulation != null && showDebugLogs)
                {
                    Debug.Log($"xdrivewrist: Auto-found wristRollArticulation on {rollObj.name}");
                }
            }
        }
    }

    /// <summary>
    /// Validate that ArticulationBodies are found and are revolute joints
    /// </summary>
    void ValidateArticulations()
    {
        wristYawFound = wristYawArticulation != null && wristYawArticulation.jointType == ArticulationJointType.RevoluteJoint;
        wristPitchFound = wristPitchArticulation != null && wristPitchArticulation.jointType == ArticulationJointType.RevoluteJoint;
        wristRollFound = wristRollArticulation != null && wristRollArticulation.jointType == ArticulationJointType.RevoluteJoint;

        if (!wristYawFound)
        {
            Debug.LogError("xdrivewrist: Missing or invalid wrist yaw ArticulationBody component!");
            if (wristYawArticulation != null)
            {
                Debug.LogError($"xdrivewrist: Found ArticulationBody but wrong type: {wristYawArticulation.jointType} (expected RevoluteJoint)");
            }
        }
        if (!wristPitchFound)
        {
            Debug.LogError("xdrivewrist: Missing or invalid wrist pitch ArticulationBody component!");
            if (wristPitchArticulation != null)
            {
                Debug.LogError($"xdrivewrist: Found ArticulationBody but wrong type: {wristPitchArticulation.jointType} (expected RevoluteJoint)");
            }
        }
        if (!wristRollFound)
        {
            Debug.LogError("xdrivewrist: Missing or invalid wrist roll ArticulationBody component!");
            if (wristRollArticulation != null)
            {
                Debug.LogError($"xdrivewrist: Found ArticulationBody but wrong type: {wristRollArticulation.jointType} (expected RevoluteJoint)");
            }
        }
    }

    /// <summary>
    /// Initialize ArticulationBody drive parameters for a joint
    /// </summary>
    void InitializeDrive(ArticulationBody articulation, string jointName)
    {
        if (articulation == null) return;

        ArticulationDrive drive = articulation.xDrive;
        drive.stiffness = stiffness;
        drive.damping = damping;
        drive.forceLimit = forceLimit;
        articulation.xDrive = drive;

        if (showDebugLogs)
        {
            Debug.Log($"xdrivewrist: Initialized {jointName} drive - Stiffness: {stiffness}, Damping: {damping}, ForceLimit: {forceLimit}");
        }
    }

    /// <summary>
    /// Load current positions from ArticulationBodies
    /// </summary>
    void LoadCurrentPositions()
    {
        if (wristYawFound)
        {
            currentWristYaw = GetRevolutePosition(wristYawArticulation);
            currentWristYaw = Mathf.Clamp(currentWristYaw, wristYawMin, wristYawMax);
            targetWristYaw = currentWristYaw;
            commandWristYaw = currentWristYaw;
            lastPublishedYaw = currentWristYaw;
        }

        if (wristPitchFound)
        {
            currentWristPitch = GetRevolutePosition(wristPitchArticulation);
            currentWristPitch = Mathf.Clamp(currentWristPitch, wristPitchMin, wristPitchMax);
            targetWristPitch = currentWristPitch;
            commandWristPitch = currentWristPitch;
            lastPublishedPitch = currentWristPitch;
        }

        if (wristRollFound)
        {
            currentWristRoll = GetRevolutePosition(wristRollArticulation);
            currentWristRoll = Mathf.Clamp(currentWristRoll, wristRollMin, wristRollMax);
            targetWristRoll = currentWristRoll;
            commandWristRoll = currentWristRoll;
            lastPublishedRoll = currentWristRoll;
        }

        if (showDebugLogs)
        {
            Debug.Log($"xdrivewrist: Loaded current positions - Yaw: {currentWristYaw:F3}rad, Pitch: {currentWristPitch:F3}rad, Roll: {currentWristRoll:F3}rad");
        }
    }

    /// <summary>
    /// Get current position of a revolute joint from ArticulationBody (in radians)
    /// IMPORTANT: Unity returns jointPosition[0] in RADIANS for revolute joints
    /// </summary>
    float GetRevolutePosition(ArticulationBody joint)
    {
        if (joint == null) return 0.0f;
        // Unity's jointPosition[0] for revolute joints is already in radians
        return joint.jointPosition[0];
    }

    /// <summary>
    /// Update all ArticulationBody X Drive targets (Unity visualization)
    /// IMPORTANT: For revolute joints, Unity expects target in DEGREES, not radians!
    /// </summary>
    void UpdateArticulationDrives()
    {
        if (wristYawFound && wristYawArticulation != null)
        {
            ArticulationDrive drive = wristYawArticulation.xDrive;
            drive.target = currentWristYaw * Mathf.Rad2Deg; // Convert radians to degrees for revolute joints
            wristYawArticulation.xDrive = drive;
        }
        else if (showDebugLogs && UnityEngine.Time.frameCount % 300 == 0)
        {
            Debug.LogWarning($"xdrivewrist: Cannot update Yaw drive - Found: {wristYawFound}, Articulation: {(wristYawArticulation != null ? "exists" : "null")}");
        }

        if (wristPitchFound && wristPitchArticulation != null)
        {
            ArticulationDrive drive = wristPitchArticulation.xDrive;
            drive.target = currentWristPitch * Mathf.Rad2Deg; // Convert radians to degrees for revolute joints
            wristPitchArticulation.xDrive = drive;
        }
        else if (showDebugLogs && UnityEngine.Time.frameCount % 300 == 0)
        {
            Debug.LogWarning($"xdrivewrist: Cannot update Pitch drive - Found: {wristPitchFound}, Articulation: {(wristPitchArticulation != null ? "exists" : "null")}");
        }

        if (wristRollFound && wristRollArticulation != null)
        {
            ArticulationDrive drive = wristRollArticulation.xDrive;
            drive.target = currentWristRoll * Mathf.Rad2Deg; // Convert radians to degrees for revolute joints
            wristRollArticulation.xDrive = drive;
        }
        else if (showDebugLogs && UnityEngine.Time.frameCount % 300 == 0)
        {
            Debug.LogWarning($"xdrivewrist: Cannot update Roll drive - Found: {wristRollFound}, Articulation: {(wristRollArticulation != null ? "exists" : "null")}");
        }
    }

    void InitializeROS2()
    {
        try
        {
            // Create ROS2 node
            ros2Node = ros2Unity.CreateNode("xdrivewrist_node");

            // Create publisher for wrist commands
            wristPublisher = ros2Node.CreatePublisher<JointTrajectory>(wristCommandTopic);

            if (showDebugLogs)
            {
                Debug.Log($"xdrivewrist: ROS2 initialized successfully!");
                Debug.Log($"xdrivewrist: Publisher created - {wristCommandTopic}");
            }

            isInitialized = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"xdrivewrist: Failed to initialize ROS2: {e.Message}");
            Debug.LogError($"Stack trace: {e.StackTrace}");
        }
    }

    /// <summary>
    /// Send command matching the exact format of the working manual command
    /// This uses zero timestamp and empty arrays like XsyncLIft.cs
    /// </summary>
    void SendCommand(float yaw, float pitch, float roll)
    {
        if (wristPublisher == null || !isInitialized)
            return;

        // Clamp positions to valid ranges
        yaw = Mathf.Clamp(yaw, wristYawMin, wristYawMax);
        pitch = Mathf.Clamp(pitch, wristPitchMin, wristPitchMax);
        roll = Mathf.Clamp(roll, wristRollMin, wristRollMax);

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

        // Set joint names (all three wrist joints) - matching manual command
        trajectory.Joint_names = new string[] 
        { 
            JOINT_WRIST_YAW,
            JOINT_WRIST_PITCH,
            JOINT_WRIST_ROLL
        };

        // Create trajectory point - matching exact manual command format
        var point = new JointTrajectoryPoint
        {
            Positions = new double[] { yaw, pitch, roll },
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
    }

    void OnDestroy()
    {
        if (wristPublisher != null)
        {
            wristPublisher.Dispose();
        }
    }
}
