using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;

public class VRCameraFeedDisplay : MonoBehaviour
{
    [Header("ROS Settings")]
    [SerializeField] private string topicName = "/camera/camera/color/image_raw";

    [Header("VR Display Settings")]
    [SerializeField] private Transform headTransform; // Your Quest 3 camera/head
    [SerializeField] private float distanceFromHead = 2f; // Distance of screen from user
    [SerializeField] private Vector2 screenSize = new Vector2(1.6f, 0.9f); // 16:9 aspect ratio
    [SerializeField] private bool followHead = false; // Screen follows your head movement
    [SerializeField] private Vector3 offset = new Vector3(0, 0, 0); // Offset from head

    private Texture2D cameraTexture;
    private ROSConnection ros;
    private GameObject displayQuad;
    private Material displayMaterial;

    void Start()
    {
        // Create display quad
        CreateDisplayQuad();

        // Get ROS connection
        ros = ROSConnection.GetOrCreateInstance();

        // Subscribe to camera topic
        ros.Subscribe<ImageMsg>(topicName, UpdateCameraImage);

        Debug.Log($"Subscribed to {topicName}");
    }

    void CreateDisplayQuad()
    {
        // Create a quad to display the camera feed
        displayQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        displayQuad.name = "D435i Camera Feed";
        displayQuad.transform.localScale = new Vector3(screenSize.x, screenSize.y, 1f);

        // Create material with unlit shader for better visibility
        displayMaterial = new Material(Shader.Find("Unlit/Texture"));
        displayQuad.GetComponent<Renderer>().material = displayMaterial;

        // Position in front of user
        if (headTransform != null)
        {
            displayQuad.transform.position = headTransform.position + headTransform.forward * distanceFromHead + offset;
            displayQuad.transform.rotation = Quaternion.LookRotation(displayQuad.transform.position - headTransform.position);
        }
        else
        {
            displayQuad.transform.position = new Vector3(0, 1.5f, 2f);
        }
    }

    void Update()
    {
        // Optionally make screen follow head
        if (followHead && headTransform != null && displayQuad != null)
        {
            Vector3 targetPosition = headTransform.position + headTransform.forward * distanceFromHead + offset;
            displayQuad.transform.position = Vector3.Lerp(displayQuad.transform.position, targetPosition, Time.deltaTime * 5f);
            displayQuad.transform.rotation = Quaternion.LookRotation(displayQuad.transform.position - headTransform.position);
        }
    }

    void UpdateCameraImage(ImageMsg imageMsg)
    {
        int width = (int)imageMsg.width;
        int height = (int)imageMsg.height;

        // Create or resize texture
        if (cameraTexture == null || cameraTexture.width != width || cameraTexture.height != height)
        {
            cameraTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
            cameraTexture.filterMode = FilterMode.Bilinear;
        }

        // Convert image data
        byte[] imageData = imageMsg.data;

        if (imageMsg.encoding == "rgb8")
        {
            cameraTexture.LoadRawTextureData(imageData);
        }
        else if (imageMsg.encoding == "bgr8")
        {
            // BGR to RGB conversion
            byte[] rgbData = new byte[imageData.Length];
            for (int i = 0; i < imageData.Length; i += 3)
            {
                rgbData[i] = imageData[i + 2];
                rgbData[i + 1] = imageData[i + 1];
                rgbData[i + 2] = imageData[i];
            }
            cameraTexture.LoadRawTextureData(rgbData);
        }

        // Flip vertically
        FlipTextureVertically(cameraTexture);
        cameraTexture.Apply();

        // Update material
        if (displayMaterial != null)
        {
            displayMaterial.mainTexture = cameraTexture;
        }
    }

    void FlipTextureVertically(Texture2D texture)
    {
        Color[] pixels = texture.GetPixels();
        Color[] flipped = new Color[pixels.Length];
        int w = texture.width;
        int h = texture.height;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                flipped[x + y * w] = pixels[x + (h - 1 - y) * w];
            }
        }

        texture.SetPixels(flipped);
    }

    void OnDestroy()
    {
        if (ros != null)
        {
            ros.Unsubscribe(topicName);
        }

        if (displayQuad != null)
        {
            Destroy(displayQuad);
        }
    }
}