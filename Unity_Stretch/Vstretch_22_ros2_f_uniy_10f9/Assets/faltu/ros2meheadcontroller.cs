using System.Collections.Generic;
using UnityEngine;
using ROS2;
using trajectory_msgs.msg;
using UnityEngine.XR;
using Unity.XR.OpenVR;
using UnityEngine.XR.Management;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.InputSystem;


public class ros2headcontroller : MonoBehaviour
{
    // Start is called before the first frame update
    [Header("Info: working_ros2for_unity_Head_controller")]
    [Header("ROS2 Settings")]

    // --- ROS Action's/Topic Names ---
    public string HeadControllerTopicName = "/stretch_controller/follow_joint_trajectory/goal";
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2unityNode;    
    private IPublisher<trajectory_msgs.msg.JointTrajectory> HeadControllerPublisher;

    // ---  Input Action References ---
    public InputActionReference buttonA;

    // --- XR Head Tracking ---
    //private InputDevice headDevice;
    private bool headDeviceFound = false;


    // --- Calibration ---
    [Header("Calibration")]
    [Tooltip("Enable calibration feature")]
    public bool enableCalibration = true;

    [Header("Debug")]
    public bool showDebugLogs = true;
    void Start()
    {
        ros2Unity = GetComponent<ROS2UnityComponent>();

        if (ros2Unity == null)
        {
            Debug.LogError("ros2headController: ROS2UnityComponent not found! Please add ROS2UnityComponent to this GameObject.");
        }
        if (showDebugLogs)
        {
            Debug.Log("ros2 head Controller script initialized.");
        }

        buttonA.action.Enable();
       
        Debug.Log("Button A action enabled.");
    }

    // Update is called once per frame
    void Update()
    {
        if (ros2Unity.Ok())
        {
            if (ros2unityNode == null)
            {
                ros2unityNode = ros2Unity.CreateNode("ros2Lift_Controller_node");
                HeadControllerPublisher = ros2unityNode.CreatePublisher<trajectory_msgs.msg.JointTrajectory>(HeadControllerTopicName);
                Debug.Log($"ros2baseController: baseControllerPublisher created. topic name: {HeadControllerTopicName}");
            }

        }

        var rightHandController = new System.Collections.Generic.List<UnityEngine.InputSystem.InputDevice>();
        Debug.Log(rightHandController);

        //UnityEngine.InputSystem.InputSystem.FindDevices("XRController","RightHand Controller");
        //UnityEngine.InputSystem.InputSystem.GetDevices(rightHandController);

        // Get button input       

        //Vector2 buttonInput = buttonA.action.ReadValue<Vector2>();
        bool buttonInputA = buttonA.action.ReadValue<bool>();
        float buttonInputB = buttonA.action.ReadValue<float>();

        Debug.Log($"Button A input value: {buttonInputA}");
        Debug.Log("rightAbutttonTest enabled");
        Debug.Log($"InputActionReference assigned: {buttonInputA != null}");

    }
    // --- Perform initial calibration ---
    /*if (enableCalibration)
    {
        Invoke("CalibrateHeadTracking", 1f);
        //CalibrateHeadTracking();


    }


    //void CalibrateHeadTracking();

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
            Debug.Log(" Head tracking calibrated! Current head position set as center (0, 0)");
        }
         List<InputDevice> devices = new List<InputDevice>();

        Debug.Log($"Found device: {devices}");
        Open

        InputDevice.GetDevicesAtXRNode(XRNode.Head, devices);
    }


        */




}  // End of ros2headcontroller class
