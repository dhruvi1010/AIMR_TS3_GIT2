using ROS2;
using std_msgs.msg;
using trajectory_msgs.msg;
using UnityEngine;
using System;
using builtin_interfaces.msg;
//using control_msgs.action;

namespace ROS2
{

    public class ros2lift : MonoBehaviour
    {
        // Start is called before the first frame update

        [Header("Info: working_ros2for_unity_Lift_keyboard_controller")]
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

        [Header("Unity Visualization")]
        [Tooltip("Transform to move up/down (the lift part of your robot in Unity)")]
        public Transform liftTransform;

        [Tooltip("Movement axis (default: Y-axis for vertical movement)")]
        public Vector3 movementAxis = Vector3.up;

        [Tooltip("Scale factor from meters to Unity units (1.0 = 1m = 1 Unity unit)")]
        public float unityScale = 1.0f;

        [Tooltip("Smooth the visualization movement")]
        public bool smoothMovement = true;

        [Tooltip("Speed of smooth movement (higher = faster)")]
        public float smoothSpeed = 5.0f;

        private Vector3 initialLiftPosition;
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

            // Initialize visualization
            if (liftTransform != null)
            {
                initialLiftPosition = liftTransform.localPosition;
                // Set initial position
                UpdateLiftVisualization();
            }
            else
            {
                Debug.LogWarning("ros2lift: Lift Transform not assigned! Visualization will not work. Assign the lift Transform in the Inspector.");
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
                    Debug.Log($"ros2liftController: liftControllerPublisher created. topic name: {LiftControllerTopicName}");
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

            // Update visualization every frame
            UpdateLiftVisualization();
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
            ;

            {
                //point.positions = new double[] { currentLiftPosition };

                //point.time_from_start = new DurationMsg();
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



            }
            ;


            trajectory.Points = new JointTrajectoryPoint[] { point };


            // --- Publish trajectory ---

            LiftControllerPublisher.Publish(trajectory);

            Debug.Log($"Lift by..: {currentLiftPosition}");
        }

        void UpdateLiftVisualization()
        {
            if (liftTransform == null)
                return;

            // Calculate target Unity position based on current lift position
            Vector3 offset = movementAxis.normalized * (currentLiftPosition * unityScale);
            Vector3 targetPosition = initialLiftPosition + offset;

            // Apply movement (smooth or instant)
            if (smoothMovement)
            {
                liftTransform.localPosition = Vector3.Lerp(
                    liftTransform.localPosition,
                    targetPosition,
                    UnityEngine.Time.deltaTime * smoothSpeed
                );
            }
            else
            {
                liftTransform.localPosition = targetPosition;
            }
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

}