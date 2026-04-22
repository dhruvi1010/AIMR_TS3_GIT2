using builtin_interfaces.msg;
using ROS2;
using System.Collections;
using System.Collections.Generic;
using trajectory_msgs.msg;
using UnityEngine;


public class ros2LiftByJoy : MonoBehaviour
{
    // Start is called before the first frame update
    [Header("Info: working_ros2for_unity_Lift_joystcik_controller..but joy is not there..only key")]
    [Header("ROS2 Settings")]


    public string LiftControllerTopicName = "/stretch_controller/follow_joint_trajectory/goal";

    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2unityNode;
    private IPublisher<trajectory_msgs.msg.JointTrajectory> LiftControllerPublisher;


    [Header("Debug")]
    public bool showDebugLogs = true;

    public float liftIncrement = 0.05f;
    public float liftSpeed = 0.1f; // m/s

    //[Range(0.5f, 1.0f)]
    public float liftPositionMin = 0.5f;
    public float liftPositionMax = 1.0f;

    // --- Internal State ---  to-do

    private float currentLiftPosition = 0.5f;
    public float maxVelocity = 0.5f;

    public GameObject Joint_Lift;
    void Start()
    {
        ros2Unity = GetComponent<ROS2UnityComponent>();

        if (ros2Unity == null)
        {
            Debug.LogError("ros2baseController: ROS2UnityComponent not found! Please add ROS2UnityComponent to this GameObject.");
        }
        if (showDebugLogs)
        {
            Debug.Log("ros2LIFT Controller script initialized.");
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (ros2Unity.Ok())
        {
            if (ros2unityNode == null)
            {
                ros2unityNode = ros2Unity.CreateNode("ros2Lift_Controller_node");
                LiftControllerPublisher = ros2unityNode.CreatePublisher<trajectory_msgs.msg.JointTrajectory>(LiftControllerTopicName);
                Debug.Log($"ros2baseController: baseControllerPublisher created. topic name: {LiftControllerTopicName}");
            }
        }

        if (Input.GetKeyDown(KeyCode.UpArrow))

        {
            MoveLift(liftIncrement);

            Debug.Log($"Lift by..: {liftIncrement}");
            Debug.Log($"Lift UP - New position: {currentLiftPosition:F3}m");

        }
        else if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            MoveLift(-liftIncrement);
            Debug.Log($"Lift DOWN - New position: {currentLiftPosition:F3}m");
        }
    }

    void MoveLift(float deltaPosition)
    {
        // Update and clamp position
        float previousPosition = currentLiftPosition;
        currentLiftPosition += deltaPosition;
        Debug.Log($"Lift position before clamp: {currentLiftPosition}");
        currentLiftPosition = Mathf.Clamp(currentLiftPosition, liftPositionMin, liftPositionMax); // Stretch3 lift range: 0-1.1m



        // --- Create trajectory message ---
        JointTrajectory trajectory = new trajectory_msgs.msg.JointTrajectory();

        // --- Set header ---
        trajectory.Header = new std_msgs.msg.Header
        {
            Stamp = GetCurrentROSTime(),
            Frame_id = "base_link"
        };

        // --- Set joint names ---
        trajectory.Joint_names = new string[] { "joint_lift" };

        // --- Create trajectory point ---
        JointTrajectoryPoint point = new JointTrajectoryPoint();
        //trajectory.joint_names[0] = liftName;

        // --- Calculate trajectory duration based on distance and speed ---

        {
            float trajectoryDuration = Mathf.Abs(deltaPosition) / liftSpeed;

            point.Positions = new double[] { currentLiftPosition };
            Debug.Log($"cuurent lift Position: {currentLiftPosition}");
            point.Velocities = new double[] { maxVelocity };
            point.Accelerations = new double[] { };
            point.Effort = new double[] { };
            point.Time_from_start = new Duration
            {
                Sec = (int)trajectoryDuration,
                Nanosec = (uint)((trajectoryDuration - (int)trajectoryDuration) * 1e9)
            };



        };
            
        trajectory.Points = new JointTrajectoryPoint[] { point };

        // to move the lift in unity

        Joint_Lift.transform.localPosition = new Vector3(Joint_Lift.transform.localPosition.x, currentLiftPosition, Joint_Lift.transform.localPosition.z);
       


        // --- Publish trajectory ---
        LiftControllerPublisher.Publish(trajectory);
        Debug.Log($"Lift by..: {currentLiftPosition}");
    }

    builtin_interfaces.msg.Time GetCurrentROSTime()
    {

        double currentTime = UnityEngine.Time.timeAsDouble;   // 'Time' is an ambiguous reference between 'UnityEngine.Time' and 'builtin_interfaces.msg.Time'
        return new builtin_interfaces.msg.Time
        {
            Sec = (int)currentTime,
            Nanosec = (uint)((currentTime - (int)currentTime) * 1e9)
        };
        //throw new NotImplementedException();
    }
}

