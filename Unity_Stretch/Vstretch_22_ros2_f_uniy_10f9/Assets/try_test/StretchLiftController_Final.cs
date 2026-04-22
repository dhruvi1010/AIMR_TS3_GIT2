using UnityEngine;
using ROS2;

/// <summary>
/// Complete Stretch Lift Controller for Unity
/// - Publishes commands to /stretch_controller/follow_joint_trajectory/goal
/// - Subscribes to /joint_states (reads joint_lift position)
/// - Handles ArticulationBody, Transform, and Rigidbody
/// </summary>
public class StretchLiftController_Final : MonoBehaviour
{
    // ROS2 components
    private ROS2UnityComponent ros2Unity;
    private ROS2Node ros2Node;
    private IPublisher<trajectory_msgs.msg.JointTrajectory> liftPublisher;
    private ISubscription<sensor_msgs.msg.JointState> jointStateSubscriber;

    [Header("ROS2 Configuration")]
    [Tooltip("Topic to publish lift commands")]
    public string liftCommandTopic = "/stretch_controller/follow_joint_trajectory/goal";

    [Tooltip("Topic to subscribe for joint states")]
    public string jointStateTopic = "/joint_states";

    [Tooltip("Name of the lift joint (NOT the link!)")]
    public string liftJointName = "joint_lift";

    [Header("Unity Visualization")]
    [Tooltip("The GameObject to move (assign link_lift or the visual part)")]
    public Transform liftTransform;

    [Tooltip("How to move the lift")]
    public ControlMethod controlMethod = ControlMethod.AutoDetect;

    [Tooltip("Movement axis (for Transform/Rigidbody methods)")]
    public Vector3 movementAxis = Vector3.up;

    [Tooltip("Scale from meters to Unity units")]
    public float unityScale = 1.0f;

    [Tooltip("Smooth the visualization")]
    public bool smoothMovement = true;

    [Tooltip("Smoothing speed")]
    public float smoothSpeed = 10.0f;

    [Header("Lift Control")]
    [Tooltip("Distance to move per key press (meters)")]
    public float liftIncrement = 0.05f;

    [Tooltip("Speed of lift movement (m/s)")]
    public float liftSpeed = 0.1f;

    [Tooltip("Minimum lift position (meters)")]
    public float liftPositionMin = 0.5f;

    [Tooltip("Maximum lift position (meters) - Stretch 3: 0-1.1m")]
    public float liftPositionMax = 1.0f;

    [Tooltip("Maximum velocity for trajectory")]
    public float maxVelocity = 0.5f;

    [Header("Debug")]
    public bool showDebugInfo = true;
    public bool logJointStates = false;

    [Header("Status (Read-Only)")]
    public bool isInitialized = false;
    public bool hasReceivedJointState = false;
    public ControlMethod activeMethod;
    public float currentLiftPosition = 0.0f;
    public float targetLiftPosition = 0.0f;

    // Component references
    private ArticulationBody articulationBody;
    private Rigidbody rigidBody;
    private Vector3 initialPosition;

    public enum ControlMethod
    {
        AutoDetect,
        ArticulationBody,
        Transform,
        Rigidbody
    }

    void Start()
    {
        DetectControlMethod();
        InitializeROS2();
    }

    void DetectControlMethod()
    {
        if (liftTransform == null)
        {
            Debug.LogError("? Lift Transform not assigned!");
            return;
        }

        // Store initial position
        initialPosition = liftTransform.localPosition;

        // Detect components
        articulationBody = liftTransform.GetComponent<ArticulationBody>();
        rigidBody = liftTransform.GetComponent<Rigidbody>();

        // Determine control method
        if (controlMethod == ControlMethod.AutoDetect)
        {
            if (articulationBody != null)
            {
                activeMethod = ControlMethod.ArticulationBody;
                SetupArticulationBody();
                Debug.Log($"? Auto-detected ArticulationBody on {liftTransform.name}");
            }
            else if (rigidBody != null)
            {
                activeMethod = ControlMethod.Rigidbody;
                Debug.Log($"? Auto-detected Rigidbody on {liftTransform.name}");
            }
            else
            {
                activeMethod = ControlMethod.Transform;
                Debug.Log($"? Using Transform control on {liftTransform.name}");
            }
        }
        else
        {
            activeMethod = controlMethod;
            if (activeMethod == ControlMethod.ArticulationBody)
            {
                SetupArticulationBody();
            }
        }

        Debug.Log($"Control Method: {activeMethod}");
    }

    void SetupArticulationBody()
    {
        if (articulationBody == null)
        {
            Debug.LogError("? ArticulationBody not found but method set to ArticulationBody!");
            return;
        }

        if (articulationBody.jointType != ArticulationJointType.PrismaticJoint)
        {
            Debug.LogWarning($"?? ArticulationBody on {liftTransform.name} is {articulationBody.jointType}, not PrismaticJoint!");
            Debug.LogWarning("This might not be the lift joint. Check if you selected the correct GameObject.");
            return;
        }

        // Configure drive for better control
        var drive = articulationBody.xDrive;
        drive.stiffness = 10000f;
        drive.damping = 1000f;
        drive.forceLimit = float.MaxValue;
        articulationBody.xDrive = drive;

        Debug.Log($"? ArticulationBody configured:");
        Debug.Log($"  Joint Type: {articulationBody.jointType}");
        Debug.Log($"  Limits: [{drive.lowerLimit}, {drive.upperLimit}]");
    }

    void InitializeROS2()
    {
        try
        {
            // Find ROS2 Unity Component
            ros2Unity = GetComponent<ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                ros2Unity = FindObjectOfType<ROS2UnityComponent>();
            }

            if (ros2Unity == null)
            {
                Debug.LogError("? ROS2UnityComponent not found! Add ROS2UnityCore to scene.");
                return;
            }

            // Create node
            ros2Node = ros2Unity.CreateNode("stretch_lift_unity_controller");

            // Create publisher for commands
            liftPublisher = ros2Node.CreatePublisher<trajectory_msgs.msg.JointTrajectory>(liftCommandTopic);
            Debug.Log($"? Publisher created: {liftCommandTopic}");

            // Create subscriber for joint states
            jointStateSubscriber = ros2Node.CreateSubscription<sensor_msgs.msg.JointState>(
                jointStateTopic,
                OnJointStateReceived
            );
            Debug.Log($"? Subscriber created: {jointStateTopic}");
            Debug.Log($"  Listening for joint: {liftJointName}");

            isInitialized = true;
            Debug.Log("? ROS2 Stretch Lift Controller initialized!");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"? Failed to initialize ROS2: {e.Message}\n{e.StackTrace}");
        }
    }

    void OnJointStateReceived(sensor_msgs.msg.JointState msg)
    {
        // Find the lift joint in the message
        for (int i = 0; i < msg.Name.Length; i++)
        {
            if (msg.Name[i] == liftJointName)
            {
                if (i < msg.Position.Length)
                {
                    currentLiftPosition = (float)msg.Position[i];
                    hasReceivedJointState = true;

                    if (logJointStates && Time.frameCount % 60 == 0)
                    {
                        Debug.Log($"Joint State - {liftJointName}: {currentLiftPosition:F3}m");
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

        // Handle keyboard input
        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            MoveLiftUp();
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            MoveLiftDown();
        }

        // Update visualization based on received joint states
        UpdateVisualization();
    }

    void UpdateVisualization()
    {
        if (liftTransform == null || !hasReceivedJointState)
            return;

        // Use the actual robot position from joint_states
        float displayPosition = currentLiftPosition;

        // Apply based on control method
        switch (activeMethod)
        {
            case ControlMethod.ArticulationBody:
                UpdateArticulationBodyVisualization(displayPosition);
                break;

            case ControlMethod.Rigidbody:
                UpdateRigidbodyVisualization(displayPosition);
                break;

            case ControlMethod.Transform:
                UpdateTransformVisualization(displayPosition);
                break;
        }
    }

    void UpdateArticulationBodyVisualization(float position)
    {
        if (articulationBody == null)
            return;

        // For ArticulationBody, set the drive target
        var drive = articulationBody.xDrive;

        if (smoothMovement)
        {
            float currentTarget = drive.target;
            float smoothTarget = Mathf.Lerp(currentTarget, position, Time.deltaTime * smoothSpeed);
            drive.target = smoothTarget;
        }
        else
        {
            drive.target = position;
        }

        articulationBody.xDrive = drive;
    }

    void UpdateRigidbodyVisualization(float position)
    {
        if (rigidBody == null)
            return;

        Vector3 offset = movementAxis.normalized * (position * unityScale);
        Vector3 targetPos = initialPosition + offset;

        if (smoothMovement)
        {
            rigidBody.MovePosition(Vector3.Lerp(
                rigidBody.position,
                targetPos,
                Time.deltaTime * smoothSpeed
            ));
        }
        else
        {
            rigidBody.MovePosition(targetPos);
        }
    }

    void UpdateTransformVisualization(float position)
    {
        Vector3 offset = movementAxis.normalized * (position * unityScale);
        Vector3 targetPos = initialPosition + offset;

        if (smoothMovement)
        {
            liftTransform.localPosition = Vector3.Lerp(
                liftTransform.localPosition,
                targetPos,
                Time.deltaTime * smoothSpeed
            );
        }
        else
        {
            liftTransform.localPosition = targetPos;
        }
    }

    void MoveLiftUp()
    {
        MoveLift(liftIncrement);
    }

    void MoveLiftDown()
    {
        MoveLift(-liftIncrement);
    }

    void MoveLift(float deltaPosition)
    {
        if (!isInitialized || liftPublisher == null)
        {
            Debug.LogWarning("?? ROS2 not initialized");
            return;
        }

        // Calculate new target position
        targetLiftPosition = currentLiftPosition + deltaPosition;
        targetLiftPosition = Mathf.Clamp(targetLiftPosition, liftPositionMin, liftPositionMax);

        if (showDebugInfo)
        {
            Debug.Log($"Lift {(deltaPosition > 0 ? "UP ?" : "DOWN ?")}: {currentLiftPosition:F3}m ? {targetLiftPosition:F3}m");
        }

        // Create and publish trajectory
        var trajectory = CreateLiftTrajectory(targetLiftPosition);
        liftPublisher.Publish(trajectory);
    }

    trajectory_msgs.msg.JointTrajectory CreateLiftTrajectory(float targetPosition)
    {
        var trajectory = new trajectory_msgs.msg.JointTrajectory();

        // Header
        trajectory.Header = new std_msgs.msg.Header
        {
            Stamp = GetCurrentROSTime(),
            Frame_id = "base_link"
        };

        // Joint name
        trajectory.Joint_names = new string[] { liftJointName };

        // Calculate duration
        float distance = Mathf.Abs(targetPosition - currentLiftPosition);
        float duration = Mathf.Max(distance / liftSpeed, 0.1f);

        // Create trajectory point
        var point = new trajectory_msgs.msg.JointTrajectoryPoint
        {
            Positions = new double[] { targetPosition },
            Velocities = new double[] { maxVelocity },
            Accelerations = new double[] { },
            Effort = new double[] { },
            Time_from_start = new builtin_interfaces.msg.Duration
            {
                Sec = (int)duration,
                Nanosec = (uint)((duration - (int)duration) * 1e9)
            }
        };

        trajectory.Points = new trajectory_msgs.msg.JointTrajectoryPoint[] { point };
        return trajectory;
    }

    builtin_interfaces.msg.Time GetCurrentROSTime()
    {
        double time = Time.timeAsDouble;
        return new builtin_interfaces.msg.Time
        {
            Sec = (int)time,
            Nanosec = (uint)((time - (int)time) * 1e9)
        };
    }

    public void SetLiftPosition(float position)
    {
        float clampedPosition = Mathf.Clamp(position, liftPositionMin, liftPositionMax);
        float delta = clampedPosition - currentLiftPosition;
        MoveLift(delta);
    }

    void OnDestroy()
    {
        if (showDebugInfo)
        {
            Debug.Log("Stretch Lift Controller destroyed");
        }
    }

    void OnGUI()
    {
        if (!showDebugInfo)
            return;

        int width = 400;
        int height = 280;

        GUILayout.BeginArea(new Rect(10, 10, width, height));

        // Title
        GUI.backgroundColor = Color.black;
        GUILayout.Box("=== STRETCH LIFT CONTROLLER ===", GUILayout.Width(width - 10));
        GUI.backgroundColor = Color.white;

        // Status
        GUILayout.Label($"ROS2: {(isInitialized ? "? Connected" : "? Not Initialized")}");
        GUILayout.Label($"Control Method: {activeMethod}");

        // Joint state feedback
        string feedbackStatus = hasReceivedJointState ? "? Receiving" : "? No Data";
        GUILayout.Label($"Joint States: {feedbackStatus}");

        if (hasReceivedJointState)
        {
            GUILayout.Label($"Current Position: {currentLiftPosition:F3} m");
            GUILayout.Label($"Target Position: {targetLiftPosition:F3} m");
        }

        // Topics
        GUILayout.Label("????????????????????????");
        GUILayout.Label($"Publishing: {liftCommandTopic}");
        GUILayout.Label($"Subscribing: {jointStateTopic}");
        GUILayout.Label($"Joint: {liftJointName}");

        // Range
        GUILayout.Label("????????????????????????");
        GUILayout.Label($"Range: [{liftPositionMin:F2}, {liftPositionMax:F2}] m");
        GUILayout.Label($"Increment: {liftIncrement:F3} m");

        // Controls
        GUILayout.Label("????????????????????????");
        GUILayout.Label("Controls: ?/? Arrow Keys");

        GUILayout.EndArea();
    }

    void OnDrawGizmosSelected()
    {
        if (liftTransform == null || activeMethod == ControlMethod.ArticulationBody)
            return;

        // Draw movement range
        Vector3 start = liftTransform.position;
        Vector3 dir = movementAxis.normalized;

        // Min position
        Gizmos.color = Color.green;
        Vector3 minPos = start + dir * liftPositionMin * unityScale;
        Gizmos.DrawSphere(minPos, 0.02f);

        // Max position
        Gizmos.color = Color.red;
        Vector3 maxPos = start + dir * liftPositionMax * unityScale;
        Gizmos.DrawSphere(maxPos, 0.02f);

        // Range line
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(minPos, maxPos);

        // Current position
        if (hasReceivedJointState)
        {
            Gizmos.color = Color.cyan;
            Vector3 currentPos = start + dir * currentLiftPosition * unityScale;
            Gizmos.DrawWireSphere(currentPos, 0.03f);
        }
    }
}