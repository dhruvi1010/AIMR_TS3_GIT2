using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;

public class CameraFeedReceiver : MonoBehaviour
{
    [Header("ROS Settings")]
    [SerializeField] private string topicName = "/camera/camera/color/image_raw";

    [Header("Display Settings")]
    [SerializeField] private Material displayMaterial;
    private Texture2D cameraTexture;
    private ROSConnection ros;

    void Start()
    {
        // Get ROS connection
        ros = ROSConnection.GetOrCreateInstance();

        // Subscribe to the camera topic
        ros.Subscribe<RosMessageTypes.Sensor.ImageMsg>(topicName, UpdateCameraImage);

        Debug.Log($"Subscribed to {topicName}");
    }

    void UpdateCameraImage(RosMessageTypes.Sensor.ImageMsg imageMsg)
    {
        // Get image dimensions
        int width = (int)imageMsg.width;
        int height = (int)imageMsg.height;

        // Create texture if it doesn't exist or size changed
        if (cameraTexture == null || cameraTexture.width != width || cameraTexture.height != height)
        {
            cameraTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        }

        // Convert ROS image data to Unity texture
        byte[] imageData = imageMsg.data;

        // Handle different encodings
        if (imageMsg.encoding == "rgb8")
        {
            // RGB8: Direct copy
            cameraTexture.LoadRawTextureData(imageData);
        }
        else if (imageMsg.encoding == "bgr8")
        {
            // BGR8: Need to swap R and B channels
            byte[] rgbData = new byte[imageData.Length];
            for (int i = 0; i < imageData.Length; i += 3)
            {
                rgbData[i] = imageData[i + 2];     // R = B
                rgbData[i + 1] = imageData[i + 1]; // G = G
                rgbData[i + 2] = imageData[i];     // B = R
            }
            cameraTexture.LoadRawTextureData(rgbData);
        }

        // Flip texture vertically (ROS images are typically upside down in Unity)
        FlipTextureVertically(cameraTexture);

        cameraTexture.Apply();

        // Apply to material if set
        if (displayMaterial != null)
        {
            displayMaterial.mainTexture = cameraTexture;
        }
    }

    void FlipTextureVertically(Texture2D texture)
    {
        Color[] pixels = texture.GetPixels();
        Color[] flippedPixels = new Color[pixels.Length];

        int width = texture.width;
        int height = texture.height;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                flippedPixels[x + y * width] = pixels[x + (height - 1 - y) * width];
            }
        }

        texture.SetPixels(flippedPixels);
    }

    void OnDestroy()
    {
        if (ros != null)
        {
            ros.Unsubscribe(topicName);
        }
    }
}