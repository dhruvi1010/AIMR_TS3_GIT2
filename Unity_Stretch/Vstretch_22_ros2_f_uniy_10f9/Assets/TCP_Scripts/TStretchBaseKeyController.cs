using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry; // This is for the Twist message

public class TStretchBaseKeyController : MonoBehaviour
{
    // ROS Connector
    private ROSConnection ros;

    // ROS-related variables
    public string topicName = "/stretch/cmd_vel"; // The topic Stretch listens to for velocity commands

    // Movement Speeds
    public float linearSpeed = 0.2f; // meters per second for ros & unity
    public float angularSpeed = 0.5f; // radians per second for ros
    // public float rotationSpeed = 15.0f; //  for unity
    // private float rotationSpeed = angularSpeed*30; //  for unity
    //public GameObject robot;
    private float posX = 0;
    private float posZ = 0;
    private float rotYaw = 0;
    public GameObject baseLink;

    void Start()
    {
        // Get the ROS connection instance
        ros = ROSConnection.GetOrCreateInstance();

        // Register the publisher for the cmd_vel topic
        ros.RegisterPublisher<TwistMsg>(topicName);

        //to move within uniy
        posX = baseLink.transform.position.x;  
        posZ = baseLink.transform.position.z;
        baseLink.transform.rotation = Quaternion.Euler(0, 0, 0);
    }

    void Update()
    {
        // Get keyboard input
        float linearInput = Input.GetAxis("Vertical"); // W/S or Up/Down arrows
        float angularInput = Input.GetAxis("Horizontal"); // A/D or Left/Right arrows (negated for standard robot turning)
        //float rotateLeft = System.Convert.ToSingle(Input.GetKey(KeyCode.E));
        //float rotateRight = System.Convert.ToSingle(Input.GetKey(KeyCode.Q));

        // Create a new Twist message
        TwistMsg cmdVelMessage = new TwistMsg();
        cmdVelMessage.linear.x = linearInput * linearSpeed;
        cmdVelMessage.angular.z = -angularInput * angularSpeed;

        //to move within uniy

        posX += linearInput * linearSpeed * Time.deltaTime;
        //posZ += angularInput * angularSpeed * Time.deltaTime;
       
        //rotYaw += angularInput * rotationSpeed * Time.deltaTime; //angular speed for rotating in uniy is too slow
        rotYaw += angularInput * angularSpeed * 30 * Time.deltaTime; //angular speed for rotating in uniy is too slow

        //rotYaw += rotateLeft * rotationSpeed * Time.deltaTime;
        //rotYaw -= rotateRight * rotationSpeed * Time.deltaTime;

        //Debug.Log("Rotation Yaw: " + rotYaw);

        //Debug.Log("Position X: " + posX + " Position Z: " + posZ);

        //robot.transform.Translate(posX,0, posZ);

        //baseLink.transform.position = new Vector3(posX, baseLink.transform.position.y, posZ);
        //baseLink.transform.position = new Vector3(posX, baseLink.transform.position.y, baseLink.transform.position.z);
        baseLink.transform.position = new Vector3(baseLink.transform.position.x, baseLink.transform.position.y, posX); //moving along Z axis in Unity corresponds to moving along X axis in ROS
        baseLink.transform.rotation = Quaternion.Euler(0, rotYaw,0); // Rotate around Y axis in Unity corresponds to rotation around Z axis in ROS


        // Publish the message to ROS
        ros.Publish(topicName, cmdVelMessage);
    }
}
