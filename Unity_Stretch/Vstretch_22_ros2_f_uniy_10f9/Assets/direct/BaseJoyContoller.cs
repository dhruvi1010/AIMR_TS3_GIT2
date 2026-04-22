using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//  to access the VR controller inputs
using UnityEngine.InputSystem;
using UnityEngine.XR.OpenXR.Input;

// ROS2 packages using

using geometry_msgs.msg;

namespace ROS2
{
    public class BaseJoyContoller : MonoBehaviour
    {
        // Start is called before the first frame update
        [Header("ROS2 Settings")]
        public string baseJoystickControllerTopicName = "/stretch/cmd_vel";
        private ROS2UnityComponent ros2Unity;
        private ROS2Node ros2unityNode;
        private IPublisher<geometry_msgs.msg.Twist> baseJoystickControllerPublisher;
        [Header("Debug")]
        public bool showDebugLogs = true;

        // --- Joystick Input Action References ---
        public InputActionReference baseMovement;

        // --- Movement Speeds ---
        public float maxLinearSpeed = 0.2f;  // m/s
        public float maxAngularSpeed = 0.5f; // rad/s

        // Movement Speeds
        public float linearSpeed = 0.2f; // meters per second for ros & unity
        public float angularSpeed = 0.5f; // radians per second for ros

        private float posX = 0;
        private float posZ = 0;
        private float rotYaw = 0;
        public GameObject baseLink;



        void Start()
        {
            ros2Unity = GetComponent<ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                Debug.LogError("ros2basejoystickcontroller: ROS2UnityComponent not found! Please add ROS2UnityComponent to this GameObject.");
            }
            if (showDebugLogs)
            {
                Debug.Log("ros2basejoystickcontroller script initialized.");
            }
        }

        // Update is called once per frame
        void Update()
        {
            // Check if wrist mode is active - if so, skip base control
            if (ControlModeManager.IsWristMode)
            {
                return; // Wrist mode is active, don't process base movement
            }

            if (ros2Unity.Ok())
            {
                if (ros2unityNode == null)
                {
                    ros2unityNode = ros2Unity.CreateNode("ros2basejoystickcontroller");
                    baseJoystickControllerPublisher = ros2unityNode.CreatePublisher<geometry_msgs.msg.Twist>("/stretch/cmd_vel");
                    Debug.Log($"ros2basejoystickcontroller: baseJoystickControllerPublisher created. topic name: {baseJoystickControllerTopicName}");
                }
                /* 
                                posX = baseLink.transform.position.x;
                                posZ = baseLink.transform.position.z;
                                baseLink.transform.rotation = Quaternion.Euler(0, 0, 0); */

                // Get joystick input
                Vector2 baseInput = baseMovement.action.ReadValue<Vector2>();

                // Create a new Twist message
                Twist baseJoyCmd_vel = new Twist();
                baseJoyCmd_vel.Linear.X = baseInput.y * maxLinearSpeed;  // y-axis on thumbstick is forward/back
                baseJoyCmd_vel.Angular.Z = -baseInput.x * maxAngularSpeed; // x-axis is left/right

                posX += baseInput.y * maxLinearSpeed * Time.deltaTime;
                rotYaw += baseInput.x * maxAngularSpeed * 30 * Time.deltaTime; // multiplied by 30 to make rotation more noticeable


                /*
                baseLink.transform.position = new UnityEngine.Vector3(baseLink.transform.position.x, baseLink.transform.position.y, posX);
                baseLink.transform.rotation = UnityEngine.Quaternion.Euler(0, rotYaw, 0);
                */

                baseLink.transform.position = new UnityEngine.Vector3(baseLink.transform.position.x, baseLink.transform.position.y, posX);
                baseLink.transform.rotation = UnityEngine.Quaternion.Euler(0, rotYaw, 0);



                baseJoystickControllerPublisher.Publish(baseJoyCmd_vel);
                Debug.Log("ros2basejoystickcontroller: Published joystick command to " + baseJoyCmd_vel.Linear.X);
            }
        }
    }
}

