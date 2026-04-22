using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine.XR;
using System.Collections.Generic;

using RosMessageTypes.Trajectory;  // For JointTrajectoryMsg
using RosMessageTypes.BuiltinInterfaces;
using RosMessageTypes.Std;

public class HeadController : MonoBehaviour
{
    // --- ROS Connections ---
    private ROSConnection ros;

    // --- ROS Action/Topic Names ---
    private readonly string headControllerTopic = "/stretch_controller/follow_joint_trajectory/goal";

    // Joint names
    public string panJointName = "joint_head_pan";
    public string tiltJointName = "joint_head_tilt";

    // --- XR Head Tracking ---
    private InputDevice headDevice;
    private bool headDeviceFound = false;

    // --- XR Right Controller (for calibration button) ---
    [Header("XR Right Controller Settings")]
    public XRNode controllerNode = XRNode.RightHand;
    private InputDevice rightController;
    private bool controllerFound = false;
    private bool wasCalibrationButtonPressed = false;

    // --- Camera Control Settings ---
    [Header("Camera Control Settings")]
    [Tooltip("Enable/disable head tracking camera control")]
    public bool headTrackingEnabled = true;

    [Tooltip("Sensitivity multiplier for head movements")]
    [Range(0.1f, 3.0f)]
    public float sensitivity = 1.0f;

    [Tooltip("Smoothing factor (0 = no smoothing, 0.9 = maximum smoothing)")]
    [Range(0f, 0.9f)]
    public float smoothing = 0.3f;

    [Tooltip("Update rate in Hz (how often to send commands to robot)")]
    [Range(0.1f, 30f)]
    public float updateRate = 10f;

    [Tooltip("Time for trajectory execution (seconds)")]
    [Range(0.1f, 2.0f)]
    public float trajectoryDuration = 0.5f;

    // --- Joint Limits (in radians) ---
    [Header("Joint Limits")]
    [Tooltip("Minimum head pan angle (radians) - leftmost")]
    public float panMin = -3.8f;

    [Tooltip("Maximum head pan angle (radians) - rightmost")]
    public float panMax = 1.50f;

    [Tooltip("Minimum head tilt angle (radians) - looking down")]
    public float tiltMin = -0.1f;

    [Tooltip("Maximum head tilt angle (radians) - looking up")]
    public float tiltMax = 0.52f;

    // --- Calibration ---
    [Header("Calibration")]
    [Tooltip("Enable calibration feature")]
    public bool enableCalibration = true;

    [Tooltip("Press this button on controller to calibrate (default: X/A button)")]
    public InputFeatureUsage<bool> calibrationButton = CommonUsages.primaryButton;

    // --- Dead Zone ---
    [Header("Dead Zone")]
    [Tooltip("Dead zone angle in degrees to prevent jittery movements near center")]
    [Range(0f, 10f)]
    public float deadZoneDegrees = 2f;

    // --- Velocity Control ---
    [Header("Velocity Control")]
    [Tooltip("Maximum rotational velocity (radians/sec)")]
    [Range(0.1f, 2.0f)]
    public float maxVelocity = 0.5f;

    // --- Internal State ---
    private Quaternion calibrationOffset = Quaternion.identity;
    private float currentPan = 0f;
    private float currentTilt = 0f;
    private float targetPan = 0f;
    private float targetTilt = 0f;
    private float lastUpdateTime = 0f;
    private bool isCalibrated = false;

    // --- Debug ---
    [Header("Debug")]
    public bool showDebugInfo = true;

    void Start()
    {
        // --- Initialize ROS ---
        ros = ROSConnection.GetOrCreateInstance();

        // --- Register publisher for trajectory goal ---
        ros.RegisterPublisher<JointTrajectoryMsg>(headControllerTopic);

        Debug.Log("Stretch Camera VR Action Controller initialized. Searching for devices...");

        // --- Initialize head tracking and controller ---
        InitializeHeadTracking();

        // --- Retry initialization if devices not found ---
        if (!headDeviceFound || !controllerFound)
        {
            Invoke("InitializeHeadTracking", 2f);
        }

        // --- Perform initial calibration ---
        if (enableCalibration)
        {
            Invoke("CalibrateHeadTracking", 1f);
        }
    }

    void InitializeHeadTracking()
    {
        List<InputDevice> devices = new List<InputDevice>();

        // --- Find and store HEAD device ---
        InputDevices.GetDevicesAtXRNode(XRNode.Head, devices);

        if (devices.Count > 0)
        {
            headDevice = devices[0];
            headDeviceFound = true;
            Debug.Log($"Head tracking device found: {headDevice.name}");
        }
        else
        {
            Debug.LogWarning("No head tracking device found! Make sure Quest 3 is connected.");
        }

        // --- Find and store RIGHT CONTROLLER ---
        devices.Clear(); // Clear list before reusing
        InputDevices.GetDevicesAtXRNode(XRNode.RightHand, devices);

        if (devices.Count > 0)
        {
            rightController = devices[0];
            controllerFound = true;
            Debug.Log($"Right controller found: {rightController.name}");
        }
        else
        {
            Debug.LogWarning("Right controller not found! Calibration button will not work.");
        }
    }

    void Update()
    {
        // --- Check if HEAD device is still valid ---
        if (!headDeviceFound || !headDevice.isValid)
        {
            InitializeHeadTracking();
            return;
        }

        // --- Check if CONTROLLER is still valid ---
        if (!controllerFound || !rightController.isValid)
        {
            // Try to reconnect controller
            rightController = InputDevices.GetDeviceAtXRNode(controllerNode);
            if (rightController.isValid)
            {
                controllerFound = true;
                Debug.Log("Controller reconnected!");
            }
            // Don't return - head tracking can still work without controller
        }

        // --- Handle calibration button (check every frame for instant response) ---
        if (enableCalibration && controllerFound && rightController.isValid)
        {
            bool isButtonPressed = false;
            if (rightController.TryGetFeatureValue(calibrationButton, out isButtonPressed))
            {
                // Detect rising edge (button just pressed, not held)
                if (isButtonPressed && !wasCalibrationButtonPressed)
                {
                    Debug.Log("Calibration button pressed!");
                    CalibrateHeadTracking();
                }
                wasCalibrationButtonPressed = isButtonPressed;
            }
        }

        // --- Only update trajectory at specified rate ---
        if (Time.time - lastUpdateTime < 1f / updateRate)
        {
            return;
        }
        lastUpdateTime = Time.time;

        // --- Get head rotation and update camera ---
        if (headTrackingEnabled && headDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion headRotation))
        {
            UpdateCameraFromHeadPose(headRotation);
        }
    }

    void CalibrateHeadTracking()
    {
        if (!headDeviceFound || !headDevice.isValid)
        {
            Debug.LogWarning("Cannot calibrate: Head device not found!");
            return;
        }

        if (headDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion currentRotation))
        {
            calibrationOffset = Quaternion.Inverse(currentRotation);
            Debug.Log($"Calibration offset set to: {calibrationOffset.eulerAngles}");
            isCalibrated = true;
            currentPan = 0f;
            currentTilt = 0f;
            Debug.Log("Head tracking calibrated! Current head position set as center (0, 0)");
        }
    }

    void UpdateCameraFromHeadPose(Quaternion rawHeadRotation)
    {
        if (!isCalibrated)
        {
            return;
        }

        // --- Apply calibration offset ---
        Quaternion calibratedRotation = calibrationOffset * rawHeadRotation;

        // --- Convert to Euler angles ---
        Vector3 eulerAngles = calibratedRotation.eulerAngles;

        // --- Convert Unity's angles to robot-friendly values ---
        float yaw = NormalizeAngle(eulerAngles.y);
        float pitch = NormalizeAngle(eulerAngles.x);

        float maxAllowedYaw = 75f; // Don't allow more than 75° rotation
        float maxAllowedPitch = 45f;


        yaw = Mathf.Clamp(yaw, -maxAllowedYaw, maxAllowedYaw);
        pitch = Mathf.Clamp(pitch, -maxAllowedPitch, maxAllowedPitch);

        // --- Apply dead zone ---
        if (Mathf.Abs(yaw) < deadZoneDegrees)
        {
            yaw = 0f;
        }
        if (Mathf.Abs(pitch) < deadZoneDegrees)
        {
            pitch = 0f;
        }

        // --- Convert to radians and apply sensitivity ---
        float yawRad = yaw * Mathf.Deg2Rad * sensitivity;
        float pitchRad = pitch * Mathf.Deg2Rad * sensitivity;

        // --- Map to robot's coordinate system ---
        targetPan = Mathf.Clamp(yawRad, panMin, panMax);
        targetTilt = Mathf.Clamp(-pitchRad, tiltMin, tiltMax);

        // --- Apply smoothing ---
        if (smoothing > 0f)
        {
            currentPan = Mathf.Lerp(currentPan, targetPan, 1f - smoothing);
            currentTilt = Mathf.Lerp(currentTilt, targetTilt, 1f - smoothing);
        }
        else
        {
            currentPan = targetPan;
            currentTilt = targetTilt;
        }

        // --- Send trajectory command ---
        SendTrajectoryCommand();
    }

    float NormalizeAngle(float angle)
    {
        if (angle > 180f)
        {
            angle -= 360f;
        }
        return angle;
    }

    void SendTrajectoryCommand()
    {
        // --- Create trajectory message ---
        JointTrajectoryMsg trajectory = new JointTrajectoryMsg();

        // --- Set header ---
        trajectory.header = new HeaderMsg
        {
            stamp = new TimeMsg
            {
                sec = (int)Time.time,
                nanosec = (uint)((Time.time - (int)Time.time) * 1e9)
            },
            frame_id = "base_link"
        };

        // --- Set joint names ---
        trajectory.joint_names = new string[] { panJointName, tiltJointName };

        // --- Create trajectory point ---
        JointTrajectoryPointMsg point = new JointTrajectoryPointMsg
        {
            positions = new double[] { currentPan, currentTilt },
            velocities = new double[] { maxVelocity, maxVelocity },
            accelerations = new double[] { },
            effort = new double[] { },
            time_from_start = new DurationMsg
            {
                sec = (int)trajectoryDuration,
                nanosec = (uint)((trajectoryDuration - (int)trajectoryDuration) * 1e9)
            }
        };

        trajectory.points = new JointTrajectoryPointMsg[] { point };

        // --- Publish trajectory ---
        ros.Publish(headControllerTopic, trajectory);

        if (showDebugInfo && Time.frameCount % 30 == 0)
        {
            Debug.Log($"Camera Pan: {currentPan * Mathf.Rad2Deg:F1}? | Tilt: {currentTilt * Mathf.Rad2Deg:F1}?");
        }
    }

    // --- Public Methods for External Control ---
    public void ToggleHeadTracking()
    {
        headTrackingEnabled = !headTrackingEnabled;
        Debug.Log($"Head tracking {(headTrackingEnabled ? "enabled" : "disabled")}");
    }

    public void RecalibrateCamera()
    {
        CalibrateHeadTracking();
    }

    public void ResetCameraToCenter()
    {
        targetPan = 0f;
        targetTilt = 0f;
        currentPan = 0f;
        currentTilt = 0f;
        SendTrajectoryCommand();
        Debug.Log("Camera reset to center position");
    }

    // --- Debug GUI ---
    void OnGUI()
    {
        if (!showDebugInfo || !Application.isEditor)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 400, 220));
        GUILayout.Box("=== Stretch Camera Action Controller ===");
        GUILayout.Label($"Head Device: {(headDeviceFound ? headDevice.name : "Not Found")}");
        GUILayout.Label($"Controller: {(controllerFound ? rightController.name : "Not Found")}");
        GUILayout.Label($"Calibrated: {(isCalibrated ? "Yes" : "No")}");
        GUILayout.Label($"Tracking Enabled: {(headTrackingEnabled ? "Yes" : "No")}");
        GUILayout.Label($"Pan: {currentPan * Mathf.Rad2Deg:F1}? (Target: {targetPan * Mathf.Rad2Deg:F1}?)");
        GUILayout.Label($"Tilt: {currentTilt * Mathf.Rad2Deg:F1}? (Target: {targetTilt * Mathf.Rad2Deg:F1}?)");
        GUILayout.Label($"Control Mode: FollowJointTrajectory Action");
        GUILayout.Label($"Press '{calibrationButton}' button to calibrate");
        GUILayout.EndArea();
    }

    void OnDestroy()
    {
        if (ros != null)
        {
            ResetCameraToCenter();
        }
    }
}