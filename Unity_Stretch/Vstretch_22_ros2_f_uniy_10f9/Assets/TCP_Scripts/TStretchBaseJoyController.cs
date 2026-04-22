using RosMessageTypes.Geometry;    // For TwistMsg
using RosMessageTypes.Std;         // For Float64Msg
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;
// We need these to access the VR controller inputs
using UnityEngine.InputSystem;
using UnityEngine.XR.OpenXR.Input;

public class TStretchBaseJoyController : MonoBehaviour
{
    // --- ROS Connections ---
    private ROSConnection ros;

    // --- ROS Topic Names ---
    private readonly string baseTopic = "/stretch/cmd_vel";
    private readonly string liftTopic = "/stretch/joint_lift/cmd";
    private readonly string wristYawTopic = "/stretch/joint_wrist_yaw/cmd";
    private readonly string gripperTopic = "/stretch/gripper_motor/cmd_gripper";

    // --- Input Action References ---
    // These will be linked to the controller inputs in the Unity Editor
    public InputActionReference baseMovement;
    public InputActionReference armMovement;
    public InputActionReference gripperAction;

    // --- Movement Speeds ---
    public float maxLinearSpeed = 0.2f;  // m/s
    public float maxAngularSpeed = 0.5f; // rad/s
    public float armSpeed = 0.1f;        // rate for lift/yaw

    private float posX = 0;
    private float rotYaw = 0;
    public GameObject baseLink;

    void Start()
    {
        // --- Initialize ROS ---
        ros = ROSConnection.GetOrCreateInstance();

        // --- Register all our publishers ---
        ros.RegisterPublisher<TwistMsg>(baseTopic);
        ros.RegisterPublisher<Float64Msg>(liftTopic);
        ros.RegisterPublisher<Float64Msg>(wristYawTopic);
        ros.RegisterPublisher<Float64Msg>(gripperTopic); // Stretch uses a Float64 for simple gripper commands

        posX = baseLink.transform.position.x;
        baseLink.transform.rotation = Quaternion.Euler(0, 0, 0);
    }

    void Update()
    {
        // --- 1. Handle Base Movement (Left Thumbstick) ---
        Vector2 baseInput = baseMovement.action.ReadValue<Vector2>();


        TwistMsg baseCmd = new TwistMsg();
        baseCmd.linear.x = baseInput.y * maxLinearSpeed;  // y-axis on thumbstick is forward/back
        baseCmd.angular.z = -baseInput.x * maxAngularSpeed; // x-axis is left/right
        ros.Publish(baseTopic, baseCmd);

        posX += baseInput.y * maxLinearSpeed * Time.deltaTime;
        rotYaw += baseInput.x * maxAngularSpeed * Time.deltaTime;

        baseLink.transform.position = new Vector3(baseLink.transform.position.x, baseLink.transform.position.y, posX);
        baseLink.transform.rotation = Quaternion.Euler(0, rotYaw, 0);

        // --- 2. Handle Arm and Wrist Movement (Right Thumbstick) ---
        Vector2 armInput = armMovement.action.ReadValue<Vector2>();
        // Lift (Up/Down)
        Float64Msg liftCmd = new Float64Msg(armInput.y * armSpeed);
        ros.Publish(liftTopic, liftCmd);
        // Wrist Yaw (Left/Right)
        Float64Msg wristYawCmd = new Float64Msg(-armInput.x * armSpeed);
        ros.Publish(wristYawTopic, wristYawCmd);

        // --- 3. Handle Gripper (Right Grip Button) ---
        float gripperInput = gripperAction.action.ReadValue<float>(); // 0 for released, 1 for pressed
        // Positive value closes, negative opens. We'll send a large value to close and a small to open.
        // NOTE: The Stretch gripper takes a value from -100 (fully open) to 100 (fully closed).
        Float64Msg gripperCmd = new Float64Msg(gripperInput > 0.5f ? 100.0 : -100.0);
        ros.Publish(gripperTopic, gripperCmd);
    }
}
