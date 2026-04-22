using UnityEngine;
using ROS2;

/// <summary>
/// Moves Stretch lift in Unity using ArticulationBody (for URDF-imported robots)
/// Works with joint_states subscription from /joint_states topic
/// </summary>
public class StretchLiftArticulation : MonoBehaviour
{
    // ROS2 components
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<trajectory_msgs.msg.JointTrajectory> liftPublisher;
    private ISubscription<sensor_msgs.msg.JointState> jointStateSubscriber;

    [Header("ROS2 Configuration")]
    [Tooltip("Topic to publish lift commands")]
    public string liftCommandTopic = "/stretch_controller/follow_joint_trajectory/goal";

    [Tooltip("Topic to subscribe for joint states - USE /joint_states (not /stretch/joint_states)")]
    public string jointStateTopic = "/joint_states";

    [Tooltip("Joint name - USE joint_lift (not link_lift!)")]
    public string liftJointName = "joint_lift";

    [Header("Unity Joint Configuration")]
    [Tooltip("The ArticulationBody for the lift joint (usually on link_lift GameObject)")]
    public ArticulationBody liftArticulation;

    [Tooltip("If ArticulationBody not assigned, search by name")]
    public string liftLinkName = "link_lift";

    [Tooltip("Automatically find ArticulationBody on Start")]
    public bool autoFindArticulation = true;

    [Header("Control Settings")]
    [Tooltip("Amount to move per key press (meters)")]
    public float liftIncrement = 0.05f;

    [Tooltip("Speed of movement (m/s)")]
    public float liftSpeed = 0.1f;

    [Tooltip("Minimum lift position (meters)")]
    public float liftPositionMin = 0.0f;

    [Tooltip("Maximum lift position (meters) - Stretch 3 range")]
    public float liftPositionMax = 1.1f;

    [Tooltip("Max velocity for trajectory")]
    public float maxVelocity = 0.5f;

    [Header("Visualization Mode")]
    [Tooltip("Use ROS2 joint_states feedback (accurate) or local simulation (fast)")]
    public bool useROSFeedback = true;

    [Tooltip("Smooth the joint movement")]
    public bool smoothMovement = true;

    [Header("Debug")]
    public bool showDebugInfo = true;

    // State
    private float targetLiftPosition = 0.0f;
    private float actualRobotPosition = 0.0f;
    private bool isInitialized = false;
    private bool hasReceivedJointState = false;
    private bool articulationFound = false;

    void Start()
    {
        // Find the ArticulationBody
        if (autoFindArticulation)
        {
            FindLiftArticulation();
        }

        // Initialize ROS2
        InitializeROS2();
    }

    void FindLiftArticulation()
    {
        if (liftArticulation == null)
        {
            // Search for ArticulationBody by GameObject name
            GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();

            foreach (GameObject obj in allObjects)
            {
                if (obj.name.Contains(liftLinkName) || obj.name.Contains("lift"))
                {
                    ArticulationBody ab = obj.GetComponent<ArticulationBody>();
                    if (ab != null)
                    {
                        // Check if this is a prismatic joint (sliding)
                        if (ab.jointType == ArticulationJointType.PrismaticJoint)
                        {
                            liftArticulation = ab;
                            Debug.Log($"? Found lift ArticulationBody on: {obj.name}");
                            Debug.Log($"  Joint Type: {ab.jointType}");
                            Debug.Log($"  Anchored: {ab.isRoot}");
                            articulationFound = true;
                            break;
                        }
                    }
                }
            }

            if (liftArticulation == null)
            {
                Debug.LogError("? Could not find lift ArticulationBody! Please assign manually.");
                Debug.LogError("   Looking for prismatic joint on GameObject containing: " + liftLinkName);
            }
        }
        else
        {
            articulationFound = true;
            Debug.Log($"? Lift ArticulationBody assigned: {liftArticulation.name}");
        }
    }

    void InitializeROS2()
    {
        try
        {
            ros2Unity = GetComponent<ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                ros2Unity = FindObjectOfType<ROS2UnityComponent>();
                if (ros2Unity == null)
                {
                    Debug.LogError("ROS2UnityComponent not found!");
                    return;
                }
            }

            ros2Node = ros2Unity.CreateNode("stretch_lift_articulation_node");

            // Publisher for commands
            liftPublisher = ros2Node.CreatePublisher<trajectory_msgs.msg.JointTrajectory>(
                liftCommandTopic
            );

            // Subscriber for feedback
            if (useROSFeedback && !string.IsNullOrEmpty(jointStateTopic))
            {
                jointStateSubscriber = ros2Node.CreateSubscription<sensor_msgs.msg.JointState>(
                    jointStateTopic,
                    OnJointStateReceived
                );
                Debug.Log($"? Subscribed to: {jointStateTopic}");
                Debug.Log($"  Looking for joint: {liftJointName}");
            }

            isInitialized = true;
            Debug.Log($"? ROS2 Stretch Lift Controller initialized!");
            Debug.Log($"  Command topic: {liftCommandTopic}");
            Debug.Log($"  Feedback: {(useROSFeedback ? "ROS2" : "Local Simulation")}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to initialize ROS2: {e.Message}");
        }
    }

    void OnJointStateReceived(sensor_msgs.msg.JointState msg)
    {
        // Find joint_lift in the message
        for (int i = 0; i < msg.Name.Length; i++)
        {
            if (msg.Name[i] == liftJointName)
            {
                if (i < msg.Position.Length)
                {
                    actualRobotPosition = (float)msg.Position[i];
                    hasReceivedJointState = true;

                    if (showDebugInfo && Time.frameCount % 120 == 0)
                    {
                        Debug.Log($"?? Joint state: {liftJointName} = {actualRobotPosition:F3}m");
                    }
                }
                break;
            }
        }
    }

    void Update()
    {
        if (!isInitialized)
            return;

        // Keyboard control
        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            MoveLift(liftIncrement);
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            MoveLift(-liftIncrement);
        }

        // Update Unity visualization
        UpdateArticulationJoint();
    }

    void UpdateArticulationJoint()
    {
        if (!articulationFound || liftArticulation == null)
            return;

        float desiredPosition;

        if (useROSFeedback && hasReceivedJointState)
        {
            // Use actual robot feedback
            desiredPosition = actualRobotPosition;
        }
        else
        {
            // Use local simulation
            desiredPosition = targetLiftPosition;
        }

        // Apply to ArticulationBody
        SetArticulationTarget(desiredPosition);
    }

    void SetArticulationTarget(float positionMeters)
    {
        if (liftArticulation == null)
            return;

        // Get the ArticulationDrive for the prismatic joint
        ArticulationDrive drive = liftArticulation.xDrive;

        // Set target position
        if (smoothMovement)
        {
            // Smooth interpolation
            float currentPos = drive.target;
            float newTarget = Mathf.Lerp(currentPos, positionMeters, Time.deltaTime * 10f);
            drive.target = newTarget;
        }
        else
        {
            // Direct assignment
            drive.target = positionMeters;
        }

        // Apply the drive back
        liftArticulation.xDrive = drive;

        if (showDebugInfo && Time.frameCount % 60 == 0)
        {
            Debug.Log($" ArticulationBody target: {drive.target:F3}m");
        }
    }

    void MoveLift(float deltaPosition)
    {
        if (!isInitialized || liftPublisher == null)
        {
            Debug.LogWarning("ROS2 not initialized");
            return;
        }

        // Update target
        float previousPosition = targetLiftPosition;
        targetLiftPosition += deltaPosition;
        targetLiftPosition = Mathf.Clamp(targetLiftPosition, liftPositionMin, liftPositionMax);

        if (showDebugInfo)
        {
            Debug.Log($"?? Lift {(deltaPosition > 0 ? "UP" : "DOWN")}: Target = {targetLiftPosition:F3}m");
        }

        // Publish to robot
        var trajectory = CreateLiftTrajectory(targetLiftPosition, deltaPosition);
        liftPublisher.Publish(trajectory);

        // If not using ROS feedback, update visualization immediately
        if (!useROSFeedback)
        {
            SetArticulationTarget(targetLiftPosition);
        }
    }

    trajectory_msgs.msg.JointTrajectory CreateLiftTrajectory(float targetPosition, float deltaPosition)
    {
        var trajectory = new trajectory_msgs.msg.JointTrajectory();

        trajectory.Header = new std_msgs.msg.Header
        {
            Stamp = GetCurrentROSTime(),
            Frame_id = "base_link"
        };

        trajectory.Joint_names = new string[] { liftJointName };

        float actualDelta = Mathf.Abs(deltaPosition);
        float trajectoryDuration = Mathf.Max(actualDelta / liftSpeed, 0.01f);

        var point = new trajectory_msgs.msg.JointTrajectoryPoint
        {
            Positions = new double[] { targetPosition },
            Velocities = new double[] { maxVelocity },
            Accelerations = new double[] { },
            Effort = new double[] { },
            Time_from_start = new builtin_interfaces.msg.Duration
            {
                Sec = (int)trajectoryDuration,
                Nanosec = (uint)((trajectoryDuration - (int)trajectoryDuration) * 1e9)
            }
        };

        trajectory.Points = new trajectory_msgs.msg.JointTrajectoryPoint[] { point };

        return trajectory;
    }

    builtin_interfaces.msg.Time GetCurrentROSTime()
    {
        double currentTime = Time.timeAsDouble;
        return new builtin_interfaces.msg.Time
        {
            Sec = (int)currentTime,
            Nanosec = (uint)((currentTime - (int)currentTime) * 1e9)
        };
    }

    public void SetLiftPosition(float position)
    {
        position = Mathf.Clamp(position, liftPositionMin, liftPositionMax);
        float delta = position - targetLiftPosition;
        MoveLift(delta);
    }

    public void ResetLift()
    {
        SetLiftPosition(liftPositionMin);
    }

    void OnGUI()
    {
        if (!showDebugInfo)
            return;

        GUILayout.BeginArea(new Rect(10, 10, 400, 280));
        GUILayout.Box("=== STRETCH LIFT (ArticulationBody) ===");

        GUILayout.Label($"ROS2: {(isInitialized ? "? Connected" : "? Not Init")}");
        GUILayout.Label($"ArticulationBody: {(articulationFound ? "? Found" : "? Not Found")}");

        if (liftArticulation != null)
        {
            GUILayout.Label($"Joint: {liftArticulation.name}");
            GUILayout.Label($"Type: {liftArticulation.jointType}");

            ArticulationDrive drive = liftArticulation.xDrive;
            GUILayout.Label($"Current Target: {drive.target:F3}m");
        }

        GUILayout.Label("---");

        if (useROSFeedback)
        {
            GUILayout.Label($"Feedback: ROS2 ({(hasReceivedJointState ? "?" : "?")})");
            GUILayout.Label($"Robot Position: {actualRobotPosition:F3}m");
        }
        else
        {
            GUILayout.Label($"Feedback: Local Simulation");
        }

        GUILayout.Label($"Target Position: {targetLiftPosition:F3}m");
        GUILayout.Label($"Range: [{liftPositionMin:F1}, {liftPositionMax:F1}]m");

        GUILayout.Label("---");
        GUILayout.Label("Controls: ?/? Arrow Keys");
        GUILayout.Label($"Topic: {jointStateTopic}");
        GUILayout.Label($"Joint: {liftJointName}");

        GUILayout.EndArea();
    }

    void OnDestroy()
    {
        if (showDebugInfo)
        {
            Debug.Log("Stretch Lift Articulation destroyed");
        }
    }
}