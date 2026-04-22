using ROS2;

using System.Collections;
using System.Collections.Generic;
using UnityEditor.Presets;
using UnityEngine;
using UnityEngine.XR;

public class rightAbuttonTest : MonoBehaviour
{
    [Header("Info: working_ros2for_unity_Head_controller")]
    [Header("ROS2 Settings")]
    public string HeadControllerTopicName = "/stretch_controller/follow_joint_trajectory/goal";

    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2unityNode;
    private IPublisher<trajectory_msgs.msg.JointTrajectory> HeadControllerPublisher;

    [Header("XR Controller Settings")]
    public XRNode controllerNode = XRNode.LeftHand; // Changed to LeftHand to test Y button

    [Header("Button State Tracking")]
    private bool aButtonWasPressed = false;
    private bool bButtonWasPressed = false;

    [Header("Debug")]
    public bool showDebugLogs = true;
    public bool showContinuousDebug = false;

    private InputDevice leftController; // Changed to leftController
    private bool controllerFound = false;

    void Start()
    {
        Debug.Log("=== RIGHT ABUTTON TEST SCRIPT STARTED (XR Direct Input) ===");

        ros2Unity = GetComponent<ROS2UnityComponent>();

        if (ros2Unity == null)
        {
            Debug.LogError("ros2headController: ROS2UnityComponent not found!");
        }

        if (showDebugLogs)
        {
            Debug.Log("ros2 head Controller script initialized.");
        }

        StartCoroutine(FindController());
    }

    IEnumerator FindController()
    {
        Debug.Log("Searching for XR controller...");

        // Try to find controller for up to 5 seconds
        float timeout = 10f;
        float elapsed = 0f;

        while (!controllerFound && elapsed < timeout)
        {
            leftController = InputDevices.GetDeviceAtXRNode(controllerNode);
            Debug.Log(leftController.characteristics);

            if (leftController.isValid)
                if (leftController.characteristics.HasFlag(InputDeviceCharacteristics.Controller) &&
                    leftController.characteristics.HasFlag(InputDeviceCharacteristics.Left))
            {
                controllerFound = true;
                Debug.Log($" Left Controller found!");
                Debug.Log($"  Name: {leftController.name}");
                Debug.Log($"  Manufacturer: {leftController.manufacturer}");
                Debug.Log($"  Characteristics: {leftController.characteristics}");

                // List all available features
                ListControllerFeatures();
                break;
            }

            elapsed += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }

        if (!controllerFound)
        {
            Debug.LogWarning(" Controller not found after timeout. Make sure VR headset is connected and controllers are on.");
        }
    }

    // optional : need to remove this method if not used
    void ListControllerFeatures()
    {
        Debug.Log("=== Available Left Controller Features ===");

        var usages = new List<InputFeatureUsage>();
        leftController.TryGetFeatureUsages(usages);

        foreach (var usage in usages)
        {
            Debug.Log($"  Feature: {usage.name} (Type: {usage.type})");
        }

        Debug.Log("=== End of Features ===");
    }

    void Update()
    {
        // Initialize ROS2 node
        if (ros2Unity != null && ros2Unity.Ok())
        {
            if (ros2unityNode == null)
            {
                ros2unityNode = ros2Unity.CreateNode("ros2Head_Controller_node");
                HeadControllerPublisher = ros2unityNode.CreatePublisher<trajectory_msgs.msg.JointTrajectory>(HeadControllerTopicName);
                Debug.Log($"ros2headController: Publisher created. Topic: {HeadControllerTopicName}");
            }
        }

        // Handle XR input
        HandleXRInput();
    }

    void HandleXRInput()
    {
        // Re-find controller if lost
        if (!controllerFound || !leftController.isValid)
        {
            leftController = InputDevices.GetDeviceAtXRNode(controllerNode);
            if (leftController.isValid)
            {
                controllerFound = true;
                Debug.Log("Left Controller reconnected!");
            }
            return;
        }
    

        // Check X Button (Primary Button on Left Controller)
        if (leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool xButtonPressed))
        {
            // Button just pressed (rising edge)
            if (xButtonPressed && !aButtonWasPressed)
            {
                Debug.Log("[XR INPUT]  X Button (Left Primary) PRESSED!");
                OnAButtonPressed();
            }
            // Button just released (falling edge)
            else if (!xButtonPressed && aButtonWasPressed)
            {
                Debug.Log("[XR INPUT] X Button (Left Primary) RELEASED!");
                OnAButtonReleased();
            }

            // Continuous debug
            if (showContinuousDebug && xButtonPressed)
            {
                Debug.Log("[XR INPUT] X Button held down");
            }

            aButtonWasPressed = xButtonPressed;
        }

        // Check Y Button (Secondary Button on Left Controller) - THIS IS WHAT WE NEED TO TEST
        if (leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool yButtonPressed))
        {
            // Button just pressed (rising edge)
            if (yButtonPressed && !bButtonWasPressed)
            {
                Debug.Log("[XR INPUT]  Y Button (Left Secondary) PRESSED!");
                OnBButtonPressed();
            }
            // Button just released (falling edge)
            else if (!yButtonPressed && bButtonWasPressed)
            {
                Debug.Log("[XR INPUT] Y Button (Left Secondary) RELEASED!");
                OnBButtonReleased();
            }

            // Continuous debug - ALWAYS show Y button state
            if (showDebugLogs && UnityEngine.Time.frameCount % 60 == 0) // Log once per second
            {
                Debug.Log($"[XR INPUT] Y Button state: {yButtonPressed} (was: {bButtonWasPressed})");
            }

            bButtonWasPressed = yButtonPressed;
        }
        else
        {
            // If we can't read the button, log it
            if (showDebugLogs && UnityEngine.Time.frameCount % 120 == 0)
            {
                Debug.LogWarning("[XR INPUT] Cannot read Y Button (SecondaryButton) from left controller!");
            }
        }

        // Optional: Check other buttons for debugging
        if (showContinuousDebug)
        {
            CheckOtherButtons();
        }
    }

    void CheckOtherButtons()
    {
        // Grip button
        if (leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool grip) && grip)
        {
            Debug.Log("[XR INPUT] Grip button pressed");
        }

        // Trigger button
        if (leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool trigger) && trigger)
        {
            Debug.Log("[XR INPUT] Trigger button pressed");
        }

        // Menu button
        if (leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.menuButton, out bool menu) && menu)
        {
            Debug.Log("[XR INPUT] Menu button pressed");
        }

        // Thumbstick click
        if (leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisClick, out bool thumbClick) && thumbClick)
        {
            Debug.Log("[XR INPUT] Thumbstick clicked");
        }

        // Thumbstick position
        if (leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 thumbPos))
        {
            if (thumbPos.magnitude > 0.1f)
            {
                Debug.Log($"[XR INPUT] Thumbstick: X={thumbPos.x:F2}, Y={thumbPos.y:F2}");
            }
        }

        // Trigger analog value
        if (leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float triggerValue))
        {
            if (triggerValue > 0.1f)
            {
                Debug.Log($"[XR INPUT] Trigger analog: {triggerValue:F2}");
            }
        }

        // Grip analog value
        if (leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out float gripValue))
        {
            if (gripValue > 0.1f)
            {
                Debug.Log($"[XR INPUT] Grip analog: {gripValue:F2}");
            }
        }
    }

    // ========== BUTTON X HANDLERS (Left Primary) ==========

    void OnAButtonPressed()
    {
        Debug.Log("[TEST] X Button (Left Primary) pressed - Head command would be sent");
        // PublishHeadCommand(1.0f, "X Button pressed");
    }

    void OnAButtonReleased()
    {
        Debug.Log("[TEST] X Button (Left Primary) released");
        // PublishHeadCommand(0.0f, "X Button released");
    }

    // ========== BUTTON Y HANDLERS (Left Secondary) ==========

    void OnBButtonPressed()
    {
        Debug.Log("[TEST] Y Button (Left Secondary) PRESSED! - This should toggle mode");
        // PublishHeadCommand(-1.0f, "Y Button pressed");
    }

    void OnBButtonReleased()
    {
        Debug.Log("[TEST] Y Button (Left Secondary) RELEASED!");
        // PublishHeadCommand(0.0f, "Y Button released");
    }

    // ========== ROS2 PUBLISHING ==========

    void PublishHeadCommand(float value, string debugMessage)
    {
        if (HeadControllerPublisher == null)
        {
            if (showDebugLogs)
            {
                Debug.LogWarning("Cannot publish: HeadControllerPublisher is null");
            }
            return;
        }

        // Create ROS2 message
        var msg = new trajectory_msgs.msg.JointTrajectory();

        // TODO: Populate message with your joint trajectory data
        // Example structure:
        /*
        msg.joint_names = new string[] { "joint_head_pan", "joint_head_tilt" };
        
        var point = new trajectory_msgs.msg.JointTrajectoryPoint();
        point.positions = new double[] { value, 0.0 };
        point.velocities = new double[] { 0.0, 0.0 };
        point.time_from_start = new builtin_interfaces.msg.Duration { 
            sec = 1, 
            nanosec = 0 
        };
        
        msg.points = new trajectory_msgs.msg.JointTrajectoryPoint[] { point };
        */

        HeadControllerPublisher.Publish(msg);

        if (showDebugLogs)
        {
            Debug.Log($"[ROS2]  Published head command: {value} ({debugMessage})");
        }
    }

    void OnDestroy()
    {
        //Debug.Log("=== RIGHT ABUTTON TEST SCRIPT DESTROYED ===");
    }

    // ========== PUBLIC UTILITIES ==========

    /// <summary>
    /// Call this method to test all available buttons on the controller
    /// Can be called from Inspector or another script
    /// </summary>
    public void TestAllButtons()
    {
        if (!controllerFound)
        {
            Debug.LogWarning("Left Controller not found! Cannot test buttons.");
            return;
        }

        Debug.Log("=== Testing All Left Controller Buttons ===");

        TestButton("Primary Button (X)", UnityEngine.XR.CommonUsages.primaryButton);
        TestButton("Secondary Button (Y)", UnityEngine.XR.CommonUsages.secondaryButton);
        TestButton("Grip Button", UnityEngine.XR.CommonUsages.gripButton);
        TestButton("Trigger Button", UnityEngine.XR.CommonUsages.triggerButton);
        TestButton("Menu Button", UnityEngine.XR.CommonUsages.menuButton);
        TestButton("Thumbstick Click", UnityEngine.XR.CommonUsages.primary2DAxisClick);

        // Analog values
        if (leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float triggerValue))
        {
            Debug.Log($"  Trigger Analog Value: {triggerValue:F3}");
        }

        if (leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out float gripValue))
        {
            Debug.Log($"  Grip Analog Value: {gripValue:F3}");
        }

        if (leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 thumbstick))
        {
            Debug.Log($"  Thumbstick Position: X={thumbstick.x:F2}, Y={thumbstick.y:F2}");
        }

        Debug.Log("=== End Button Test ===");
    }

    void TestButton(string buttonName, InputFeatureUsage<bool> usage)
    {
        if (leftController.TryGetFeatureValue(usage, out bool pressed))
        {
            string status = pressed ? " PRESSED" : "not pressed";
            Debug.Log($"  {buttonName}: {status}");
        }
        else
        {
            Debug.Log($"  {buttonName}:  Feature not available");
        }
    }
}
