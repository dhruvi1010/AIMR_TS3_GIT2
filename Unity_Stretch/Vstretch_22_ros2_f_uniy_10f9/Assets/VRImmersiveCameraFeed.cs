using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using UnityEngine.UI;

public class VRImmersiveCameraFeed : MonoBehaviour
{
    [Header("ROS Settings")]
    [SerializeField] private string topicName = "/camera/camera/color/image_raw";

    [Header("Display Settings")]
    [SerializeField] private RawImage leftEyeImage;
    [SerializeField] private RawImage rightEyeImage;
    [SerializeField] private bool useSingleImageForBothEyes = true;

    [Header("Performance")]
    [SerializeField] private int targetFrameRate = 30;

    private Texture2D cameraTexture;
    private ROSConnection ros;
    private float lastUpdateTime;
    private float updateInterval;

    void Start()
    {
        updateInterval = 1f / targetFrameRate;

        // Get ROS connection
        ros = ROSConnection.GetOrCreateInstance();

        // Subscribe to camera topic
        ros.Subscribe<ImageMsg>(topicName, UpdateCameraImage);

        Debug.Log($"Subscribed to {topicName} for immersive VR view");
    }

    void UpdateCameraImage(ImageMsg imageMsg)
    {
        // Throttle updates for performance
        if (Time.time - lastUpdateTime < updateInterval)
            return;

        lastUpdateTime = Time.time;

        int width = (int)imageMsg.width;
        int height = (int)imageMsg.height;

        // Create or resize texture
        if (cameraTexture == null || cameraTexture.width != width || cameraTexture.height != height)
        {
            cameraTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
            cameraTexture.filterMode = FilterMode.Bilinear;
            cameraTexture.wrapMode = TextureWrapMode.Clamp;
        }

        // Convert image data
        byte[] imageData = imageMsg.data;

        if (imageMsg.encoding == "rgb8")
        {
            cameraTexture.LoadRawTextureData(imageData);
        }
        else if (imageMsg.encoding == "bgr8")
        {
            byte[] rgbData = new byte[imageData.Length];
            for (int i = 0; i < imageData.Length; i += 3)
            {
                rgbData[i] = imageData[i + 2];
                rgbData[i + 1] = imageData[i + 1];
                rgbData[i + 2] = imageData[i];
            }
            cameraTexture.LoadRawTextureData(rgbData);
        }

        FlipTextureVertically(cameraTexture);
        cameraTexture.Apply();

        // Update UI images for both eyes
        if (leftEyeImage != null)
        {
            leftEyeImage.texture = cameraTexture;
        }

        if (rightEyeImage != null && useSingleImageForBothEyes)
        {
            rightEyeImage.texture = cameraTexture;
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
    }
}