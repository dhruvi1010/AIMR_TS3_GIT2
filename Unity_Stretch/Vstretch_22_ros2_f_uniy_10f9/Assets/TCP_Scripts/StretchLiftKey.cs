using RosMessageTypes.BuiltinInterfaces;
using RosMessageTypes.Std;
using RosMessageTypes.Trajectory;
using Unity.Robotics.ROSTCPConnector;

using UnityEditor.ShaderGraph.Internal;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR;
using System.Collections.Generic;
using UnityEngine.Rendering;
using std_msgs.msg;

public class StretchLiftKey : MonoBehaviour
{
    private ROSConnection ros;
    private string LiftTopicName = "/stretch_controller/follow_joint_trajectory/goal";

    // Joint names
    public string liftName = "joint_lift";

    // 5cm per press
    [Header("Lift Control Settings")]

    [Tooltip("Minimum lift")]
    
    public float liftIncrement = 0.05f; 
    public float liftSpeed = 0.1f; // m/s

   //[Range(0.5f, 1.0f)]
    public float liftPositionMin = 0.5f;
    
    public float liftPositionMax = 1.0f;

    // --- Internal State ---  to-do

    private float currentLiftPosition = 0.5f;

    private float currentPan = 0f;
    private float currentTilt = 0f;
    private float targetPan = 0f;
    private float targetTilt = 0f;
    private float lastUpdateTime = 0f;
    private bool isCalibrated = false;

    public float maxVelocity = 0.5f;

    // --- Debug ---
    [Header("Debug")]
    public bool showDebugInfo = true;


    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<JointTrajectoryMsg>(LiftTopicName);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.UpArrow))
            
        {
            MoveLift(liftIncrement);
            
            Debug.Log($"Lift by..: {liftIncrement}");


        }
        else if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            MoveLift(-liftIncrement);
        }
    }

    void MoveLift(float deltaPosition)
    {
        currentLiftPosition += deltaPosition;
        Debug.Log($"Lift position before clamp: {currentLiftPosition}");
        currentLiftPosition = Mathf.Clamp(currentLiftPosition, liftPositionMin, liftPositionMax); // Stretch3 lift range: 0-1.1m

       

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
        trajectory.joint_names = new string[] { "joint_lift" };

        // --- Create trajectory point ---
        JointTrajectoryPointMsg point = new JointTrajectoryPointMsg();
        //trajectory.joint_names[0] = liftName;
        ;       

        {
            //point.positions = new double[] { currentLiftPosition };

            //point.time_from_start = new DurationMsg();
            float trajectoryDuration = Mathf.Abs(deltaPosition) / liftSpeed;

            point.positions = new double[] { currentLiftPosition };
            Debug.Log($"cuurent lift Position: {currentLiftPosition}");
            point.velocities = new double[] { maxVelocity };
            point.accelerations = new double[] { };
            point.effort = new double[] { };
            point.time_from_start = new DurationMsg
            {
                sec = (int)trajectoryDuration,
                nanosec = (uint)((trajectoryDuration - (int)trajectoryDuration) * 1e9)
            };



        };


        trajectory.points = new JointTrajectoryPointMsg[] { point };


        // --- Publish trajectory ---
        ros.Publish(LiftTopicName, trajectory);

        if (showDebugInfo && Time.frameCount % 30 == 0)
        {
            Debug.Log($"Lift by..: {currentLiftPosition}");
        }
    }
}

    
