using ROS2;
using geometry_msgs.msg;
using std_msgs.msg;
using UnityEngine;

namespace ROS2
{
    /// <summary>
    /// An example class provided for testing of basic ROS2 communication
    /// </summary>
    public class r2uTester : MonoBehaviour
    {
        // Start is called before the first frame update
        private ROS2UnityComponent ros2Unity;
        private ROS2Node ros2unityNode;

        [Header("Debug")]
        public bool showDebugLogs = true;

        private IPublisher<std_msgs.msg.String> twist_pub;

        private int i;

        // ------------- Start is called before the first frame update-------------
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
        // ------------- Update is called once per frame-------------
        void Update()
        {
            if (ros2Unity.Ok())
            {
                if (ros2unityNode == null)
                {
                    ros2unityNode = ros2Unity.CreateNode("ROS2UnityTwist_pub_Node");
                    twist_pub = ros2unityNode.CreatePublisher<std_msgs.msg.String>("cmd_vel");
                    Debug.Log($"r2uTester:twist_pub created. topic name: cmd_vel");
                }

                i++;

                std_msgs.msg.String msg = new std_msgs.msg.String();    
                msg.Data = "Unity ROS2 sending: twist message " + i;
                twist_pub.Publish(msg);
                Debug.Log("r2uTester: Published message: " + msg.Data);
            }
        }
    }

}  // namespace ROS2
