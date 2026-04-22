using ROS2;
using geometry_msgs.msg;
using std_msgs.msg;
using UnityEngine;

namespace ROS2
{
    /// <summary>
    /// An example class provided for testing of basic ROS2 communication
    /// </summary>
    public class r2uTwistTester : MonoBehaviour
    {
        // Start is called before the first frame update
        private ROS2UnityComponent ros2Unity;
        private ROS2Node ros2unityNode;

        [Header("Debug")]
        public bool showDebugLogs = true;

        private IPublisher<geometry_msgs.msg.Twist> twist_pub;

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
                    twist_pub = ros2unityNode.CreatePublisher<geometry_msgs.msg.Twist>("cmd_vel");
                    Debug.Log($"r2uTwistTester:twist_pub created. topic name: cmd_vel");
                }

                i++;

                Twist msg = new Twist();
                msg.Linear.X = 0.3f ;
                twist_pub.Publish(msg);
                Debug.Log("r2uTester: Published message: " + msg.Linear);
            }
        }
    }

}  // namespace ROS2
