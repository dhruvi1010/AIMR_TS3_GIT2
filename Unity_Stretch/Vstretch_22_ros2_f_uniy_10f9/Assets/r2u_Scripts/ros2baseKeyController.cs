using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ROS2 packages using

using geometry_msgs.msg;

namespace ROS2
{
    public class ros2baseKeyController : MonoBehaviour
    {
        // Start is called before the first frame update
        [Header("Info: working_ros2for_unity_keyboard_controller")]
        
        [Header("ROS2 Settings")]
        
        public string Public_baseControllerTopicName = "/stretch/cmd_vel";  // just for display in inspector
        private string baseControllerTopicName = "/stretch/cmd_vel";  // alwayas ensure correct topic name in ROS2 side

        private ROS2UnityComponent ros2Unity;
        private ROS2Node ros2unityNode;
        private IPublisher<geometry_msgs.msg.Twist> baseControllerPublisher;
        [Header("Debug")]
        public bool showDebugLogs = true;

        // Movement Speeds
        public float linearSpeed = 0.2f; // meters per second for ros & unity
        public float angularSpeed = 0.5f; // radians per second for ros

        private float posX = 0;
        private float posZ = 0;
        private float rotYaw = 0;
        public GameObject baseLink;

        public UnityEngine.Transform mobile_base;  //  'Transform' is an ambiguous reference between 'UnityEngine.Transform' and 'geometry_msgs.msg.Transform'

        void Start()
        {
            ros2Unity = GetComponent<ROS2UnityComponent>();

            if (ros2Unity == null)
            {
                Debug.LogError("ros2baseController: ROS2UnityComponent not found! Please add ROS2UnityComponent to this GameObject.");
            }
            if (showDebugLogs)
            {
                Debug.Log("ros2baseController script initialized.");
            }
        }

    // Update is called once per frame
        void Update()
        {
            if (ros2Unity.Ok())
            {
                if (ros2unityNode == null)
                {
                    ros2unityNode = ros2Unity.CreateNode("ros2baseController");
                    baseControllerPublisher = ros2unityNode.CreatePublisher<geometry_msgs.msg.Twist>(baseControllerTopicName);
                    Debug.Log($"ros2baseController: baseControllerPublisher created. topic name: {baseControllerTopicName}");
                }

                /* //to move within uniy
                posX = baseLink.transform.position.x;  
                posZ = baseLink.transform.position.z;
                baseLink.transform.rotation = geometry_msgs.msg.Quaternion.Euler(0, 0, 0); */

                // Get keyboard input for base movement
                float linearInput = Input.GetAxis("Vertical"); // W/S or Up/Down arrows
                float angularInput = Input.GetAxis("Horizontal"); // A/D or Left/Right arrows 


                // Create a new Twist message
                // geometry_msgs.msg.Twist baseCmd_vel = new geometry_msgs.msg.Twist();
                Twist baseCmd_vel = new Twist();
                baseCmd_vel.Linear.X = linearInput * linearSpeed;
                baseCmd_vel.Angular.Z = -angularInput * angularSpeed;

                // to move within uniy
                posX += linearInput * linearSpeed * Time.deltaTime;
                rotYaw += angularInput * angularSpeed * 30 * Time.deltaTime;   // multiplied by 30 to make rotation more noticeable
                /* 
                                 baseLink.transform.position = new Vector3(baseLink.transform.position.x, baseLink.transform.position.y, posX); //moving along Z axis in Unity corresponds to moving along X axis in ROS
                                 baseLink.transform.rotation = Quaternion.Euler(0, rotYaw,0); // Rotate around Y axis in Unity corresponds to rotation around Z axis in ROS
                 */
                baseLink.transform.position = new UnityEngine.Vector3(baseLink.transform.position.x, baseLink.transform.position.y, posX); //moving along Z axis in Unity corresponds to moving along X axis in ROS
                baseLink.transform.rotation = UnityEngine.Quaternion.Euler(0, rotYaw,0); // Rotate around Y axis in Unity corresponds to rotation around Z axis in ROS

                baseControllerPublisher.Publish(baseCmd_vel);
                Debug.Log("ros2baseController: Published baseCmd_vel message: " + baseCmd_vel.Linear.X + ", " + baseCmd_vel.Angular.Z);
                Debug.Log("ros2baseController: Published baseCmd_vel message: " + baseCmd_vel.Linear.Y + ", " + baseCmd_vel.Angular.Z);
                Debug.Log("ros2baseController: Published baseCmd_vel message: " + baseCmd_vel.Linear.Z + ", " + baseCmd_vel.Angular.Z);


            }
        }
    }
}
