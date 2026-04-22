using ROS2;
using sensor_msgs.msg;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Subscribes to sensor_msgs/PointCloud2, parses to positions (and optional colors),
/// and renders as a live 3D point cloud in VR using a ParticleSystem.
/// Use the same ROS2UnityComponent and topic namespace as your D435i image feed.
/// </summary>
public class PointCloud2VRViewer : MonoBehaviour
{
    [Header("ROS2 Settings")]
    [Tooltip("PointCloud2 topic (e.g. RealSense depth/color/points)")]
    public string topicName = "/camera/camera/depth/color/points";

    [Header("Frame / Transform")]
    [Tooltip("Apply this transform to put cloud in world space (e.g. same as camera feed). Leave null to use this GameObject.")]
    public Transform pointCloudRoot;

    [Header("Rendering")]
    [Tooltip("Max particles to render (ParticleSystem limit). Excess points are downsampled.")]
    public int maxPoints = 65535;
    [Tooltip("Downsample step: use every Nth point (1 = use all, 2 = half, 4 = quarter).")]
    [Min(1)]
    public int downsampleStep = 1;
    [Tooltip("Point size in world units")]
    public float pointSize = 0.05f;
    [Tooltip("Use colors from PointCloud2 if available (rgb field)")]
    public bool useColors = true;
    [Tooltip("Default color when no rgb in message")]
    public Color defaultColor = new Color(0.4f, 0.8f, 1f, 0.9f);

    [Header("Performance")]
    [Tooltip("Throttle: process at most this many messages per second")]
    [Range(5f, 30f)]
    public float updateRateHz = 15f;

    [Header("Debug")]
    public bool showDebugLogs = true;
    public int logFirstNMessages = 3;

    // ROS2
    private ROS2UnityComponent ros2Unity;
    private ROS2Node node;
    private ISubscription<PointCloud2> pointCloudSub;

    // Parsed data (written on ROS thread, read on main)
    private readonly object dataLock = new object();
    private List<Vector3> pendingPositions = new List<Vector3>();
    private List<Color32> pendingColors = new List<Color32>();
    private bool hasNewCloud = false;

    // ParticleSystem
    private ParticleSystem ps;
    private ParticleSystem.Particle[] particles;
    private int lastPointCount;

    // Throttle
    private float lastUpdateTime;
    private int messagesReceived;

    void Start()
    {
        if (pointCloudRoot == null)
            pointCloudRoot = transform;

        ros2Unity = GetComponent<ROS2UnityComponent>();
        if (ros2Unity == null)
            ros2Unity = FindObjectOfType<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            Debug.LogError("PointCloud2VRViewer: ROS2UnityComponent not found. Add it to this GameObject or in the scene.");
            return;
        }

        SetupParticleSystem();
    }

    void Update()
    {
        if (ros2Unity == null || !ros2Unity.Ok())
            return;

        if (node == null)
        {
            node = ros2Unity.CreateNode("unity_pointcloud_viewer_node");
            var qos = new QualityOfServiceProfile(QosPresetProfile.SENSOR_DATA);
            pointCloudSub = node.CreateSubscription<PointCloud2>(topicName, OnPointCloud2Received, qos);
            if (showDebugLogs)
                Debug.Log($"[PointCloud2VRViewer] Subscribed to {topicName}");
        }

        float interval = 1f / updateRateHz;
        if (hasNewCloud && (Time.realtimeSinceStartup - lastUpdateTime >= interval))
        {
            lastUpdateTime = Time.realtimeSinceStartup;
            ApplyPendingCloudOnMainThread();
        }
    }

    void OnPointCloud2Received(PointCloud2 msg)
    {
        if (msg.Data == null || msg.Data.Length == 0)
            return;

        int width = (int)msg.Width;
        int height = (int)msg.Height;
        uint pointStep = msg.Point_step;
        uint rowStep = msg.Row_step;
        byte[] data = msg.Data;
        bool isBigEndian = msg.Is_bigendian;

        if (width <= 0 || height <= 0 || pointStep == 0)
            return;

        int total = width * height;

        int offX = -1, offY = -1, offZ = -1, offRgb = -1;
        foreach (PointField f in msg.Fields)
        {
            string name = f.Name.ToLower();
            if (name == "x") offX = (int)f.Offset;
            else if (name == "y") offY = (int)f.Offset;
            else if (name == "z") offZ = (int)f.Offset;
            else if (name == "rgb" || name == "rgba") offRgb = (int)f.Offset;
        }

        if (offX < 0 || offY < 0 || offZ < 0)
            return;

        bool hasRgb = offRgb >= 0 && useColors;
        var positions = new List<Vector3>();
        var colors = new List<Color32>();

        for (int i = 0; i < total; i += downsampleStep)
        {
            int row = i / width;
            int col = i % width;
            int byteOffset = (int)(row * rowStep + col * pointStep);

            float x = ReadFloat(data, byteOffset + offX, isBigEndian);
            float y = ReadFloat(data, byteOffset + offY, isBigEndian);
            float z = ReadFloat(data, byteOffset + offZ, isBigEndian);

            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y) || float.IsNaN(z) || float.IsInfinity(z))
                continue;
            if (z <= 0)
                continue;

            // ROS optical frame: X right, Y down, Z forward → Unity (same as camera): X right, Y up, Z forward
            positions.Add(new Vector3(x, -y, z));

            if (hasRgb)
            {
                uint packed = ReadUInt32(data, byteOffset + offRgb, isBigEndian);
                byte r = (byte)((packed >> 16) & 0xFF);
                byte g = (byte)((packed >> 8) & 0xFF);
                byte b = (byte)(packed & 0xFF);
                colors.Add(new Color32(r, g, b, 255));
            }
            else
            {
                colors.Add(new Color32(
                    (byte)(defaultColor.r * 255),
                    (byte)(defaultColor.g * 255),
                    (byte)(defaultColor.b * 255),
                    (byte)(defaultColor.a * 255)));
            }
        }

        lock (dataLock)
        {
            pendingPositions.Clear();
            pendingColors.Clear();
            pendingPositions.AddRange(positions);
            pendingColors.AddRange(colors);
            hasNewCloud = true;
        }

        messagesReceived++;
        if (showDebugLogs && messagesReceived <= logFirstNMessages)
            Debug.Log($"[PointCloud2VRViewer] Parsed {positions.Count} points (downsample step {downsampleStep}), hasRgb={hasRgb}");
    }

    private static float ReadFloat(byte[] data, int offset, bool bigEndian)
    {
        if (offset + 4 > data.Length) return 0f;
        int i = bigEndian
            ? (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]
            : (data[offset + 3] << 24) | (data[offset + 2] << 16) | (data[offset + 1] << 8) | data[offset];
        return System.BitConverter.ToSingle(System.BitConverter.GetBytes(i), 0);
    }

    private static uint ReadUInt32(byte[] data, int offset, bool bigEndian)
    {
        if (offset + 4 > data.Length) return 0;
        if (bigEndian)
            return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        return (uint)((data[offset + 3] << 24) | (data[offset + 2] << 16) | (data[offset + 1] << 8) | data[offset]);
    }

    private void ApplyPendingCloudOnMainThread()
    {
        List<Vector3> pos;
        List<Color32> col;
        lock (dataLock)
        {
            if (!hasNewCloud || pendingPositions.Count == 0)
                return;
            pos = new List<Vector3>(pendingPositions);
            col = new List<Color32>(pendingColors);
            hasNewCloud = false;
        }

        int n = Mathf.Min(pos.Count, maxPoints);
        if (particles == null || particles.Length < maxPoints)
            particles = new ParticleSystem.Particle[maxPoints];

        Transform root = pointCloudRoot != null ? pointCloudRoot : transform;
        for (int i = 0; i < n; i++)
        {
            particles[i].position = root.TransformPoint(pos[i]);
            particles[i].startColor = i < col.Count ? col[i] : defaultColor;
            particles[i].startSize = pointSize;
            particles[i].remainingLifetime = 1f;
            particles[i].startLifetime = 1f;
        }

        ps.SetParticles(particles, n);
        lastPointCount = n;
    }

    private void SetupParticleSystem()
    {
        ps = GetComponent<ParticleSystem>();
        if (ps == null)
            ps = gameObject.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.maxParticles = maxPoints;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = 1f;
        main.startSpeed = 0f;
        main.startSize = pointSize;
        main.loop = false;
        main.playOnAwake = false;

        var emit = ps.emission;
        emit.enabled = false;

        var shape = ps.shape;
        shape.enabled = false;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = renderer.material; // ensure material exists
            if (renderer.material.shader.name.Contains("Particles"))
                renderer.material.SetColor("_Color", defaultColor);
        }

        particles = new ParticleSystem.Particle[maxPoints];
    }

    void OnDestroy()
    {
        pointCloudSub = null;
        node = null;
    }

    /// <summary>Currently displayed point count (read-only).</summary>
    public int DisplayedPointCount => lastPointCount;
}
