using ROS2;  
using sensor_msgs.msg;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ROS2ForUnity-based D435i camera feed receiver.
/// Supports both raw Image messages and CompressedImage messages.
/// </summary>
public class ros2d435iListner : MonoBehaviour
{
    [Header("ROS Settings")]
    [Tooltip("Topic name - use /compressed suffix for compressed images")]
    // public string topicName = "/camera/camera/color/image_raw";
    public string topicName = "/camera/camera/color/image_raw/compressed";
    
    [Header("Display Settings")]
    public RawImage targetImage;
    public bool flipVertical = false;
    
    [Header("Debug")]
    public bool showDebugLogs = true;
    public int logFirstNFrames = 5;

    // ROS2 components
    private ROS2UnityComponent ros2Unity;
    private ROS2Node node;
    private ISubscription<sensor_msgs.msg.Image> rawImageSub;
    private ISubscription<sensor_msgs.msg.CompressedImage> compressedImageSub;
    
    // Image processing
    private Texture2D tex;
    private byte[] latestImageData;
    private byte[] latestCompressedImageData; // For compressed images
    private int latestWidth, latestHeight;
    private string latestEncoding;
    private string latestCompressedFormat; // For compressed images
    private bool hasNewImage = false;
    private bool isCompressedImage = false; // Flag to indicate if image is compressed
    private object imageLock = new object();
    
    // Statistics
    private int totalFramesReceived = 0;
    private int totalFramesProcessed = 0;
    private float lastFrameTime = 0f;

    void Start()
    {
        ros2Unity = GetComponent<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            Debug.LogError("ros2d435iListnerr: ROS2UnityComponent not found! Please add ROS2UnityComponent to this GameObject.");
        }
    }

    void Update()
    {
        // Create subscription when ROS2 is ready
        if (node == null && ros2Unity != null && ros2Unity.Ok())
        {
            node = ros2Unity.CreateNode("unity_d435i_listener");  // ------- node náme will be visible in node list & ROS2 graph ---------

            // Determine if  using compressed or raw images based on topic name
            bool isCompressedTopic = topicName.Contains("/compressed");
            
            if (isCompressedTopic)
            {
                // Use sensor QoS profile for better performance with compressed images
                QualityOfServiceProfile sensorQos = new QualityOfServiceProfile(QosPresetProfile.SENSOR_DATA);
                compressedImageSub = node.CreateSubscription<sensor_msgs.msg.CompressedImage>(topicName, OnCompressedImage, sensorQos);
                Debug.Log($"[D435i Listener] Subscribed to COMPRESSED image topic: {topicName}");
            }
            else
            {
                // Use sensor QoS profile for raw images
                QualityOfServiceProfile sensorQos = new QualityOfServiceProfile(QosPresetProfile.SENSOR_DATA);
                rawImageSub = node.CreateSubscription<sensor_msgs.msg.Image>(topicName, OnRawImage, sensorQos); //--------- topic will be visible in topic list & ROS2 graph ---------
                Debug.Log($"[D435i Listener] Subscribed to RAW image topic: {topicName}");  
            }
        }

        // Process new images on main thread
        if (hasNewImage)
        {
            ProcessImageOnMainThread();
        }
    }

    /// <summary>
    /// Callback for raw Image messages (sensor_msgs/Image)
    /// This runs on a ROS thread, so we need to be thread-safe
    /// </summary>
    void OnRawImage(sensor_msgs.msg.Image msg)
    {
        if (msg.Data == null || msg.Data.Length == 0)
        {
            if (showDebugLogs && totalFramesReceived < logFirstNFrames)
                Debug.LogWarning($"[D435i Listener] Received empty raw image data");
            return;
        }

        lock (imageLock)
        {
            latestWidth = (int)msg.Width;
            latestHeight = (int)msg.Height;
            latestEncoding = msg.Encoding;

            // Validate expected data size
            int expectedSize = latestWidth * latestHeight * 3; // RGB/BGR = 3 bytes per pixel
            if (msg.Data.Length != expectedSize)
            {
                if (showDebugLogs && totalFramesReceived < logFirstNFrames)
                    Debug.LogWarning($"[D435i Listener] Image data size mismatch. Expected: {expectedSize}, Got: {msg.Data.Length}, Encoding: {latestEncoding}");
                return;
            }

            // Allocate or resize buffer if needed
            if (latestImageData == null || latestImageData.Length != msg.Data.Length)
            {
                latestImageData = new byte[msg.Data.Length];
            }

            // Copy image data (thread-safe copy)
            System.Array.Copy(msg.Data, latestImageData, msg.Data.Length);
            hasNewImage = true;
        }

        totalFramesReceived++;
        
        // Note: Cannot use Time.realtimeSinceStartup here - this runs on ROS thread
        // Timing will be logged on main thread in Update() if needed
    }

    /// <summary>
    /// Callback for compressed Image messages (sensor_msgs/CompressedImage)
    /// This runs on a ROS thread, so we need to be thread-safe
    /// We only copy the compressed data here - decompression happens on main thread
    /// </summary>
    void OnCompressedImage(sensor_msgs.msg.CompressedImage msg)
    {
        if (msg.Data == null || msg.Data.Length == 0)
        {
            if (showDebugLogs && totalFramesReceived < logFirstNFrames)
                Debug.LogWarning($"[D435i Listener] Received empty compressed image data. Format: '{msg.Format}'");
            return;
        }

        // Normalize format string
        string formatLower = msg.Format.ToLower();
        bool isJpeg = formatLower.Contains("jpeg") || formatLower.Contains("jpg");
        bool isPng = formatLower.Contains("png");
        
        if (!isJpeg && !isPng)
        {
            if (showDebugLogs && totalFramesReceived < logFirstNFrames)
                Debug.LogWarning($"[D435i Listener] Unsupported compressed image format: '{msg.Format}'. Supported: JPEG, PNG");
            return;
        }
        
        if (showDebugLogs && totalFramesReceived < 3)
        {
            Debug.Log($"[D435i Listener] Compressed image format: '{msg.Format}' (JPEG: {isJpeg}, PNG: {isPng})");
        }

        // Copy compressed data to buffer (thread-safe, no Unity API calls)
        lock (imageLock)
        {
            // Allocate or resize buffer if needed
            if (latestCompressedImageData == null || latestCompressedImageData.Length != msg.Data.Length)
            {
                latestCompressedImageData = new byte[msg.Data.Length];
            }

            // Copy compressed image data
            System.Array.Copy(msg.Data, latestCompressedImageData, msg.Data.Length);
            latestCompressedFormat = msg.Format;
            isCompressedImage = true;
            hasNewImage = true;
        }

        totalFramesReceived++;
        
        // Note: Cannot use Time.realtimeSinceStartup here - this runs on ROS thread
        // Timing will be logged on main thread in Update() if needed
    }

    /// <summary>
    /// Process image on main thread (Unity API calls must be on main thread)
    /// </summary>
    void ProcessImageOnMainThread()
    {
        byte[] dataToProcess = null;
        byte[] compressedDataToProcess = null;
        int widthToProcess = 0;
        int heightToProcess = 0;
        string encodingToProcess = null;
        string compressedFormatToProcess = null;
        bool isCompressed = false;
        int frameNumber = 0;

        // Copy data outside lock to avoid blocking incoming messages
        lock (imageLock)
        {
            if (hasNewImage)
            {
                if (isCompressedImage && latestCompressedImageData != null)
                {
                    // Handle compressed image
                    compressedDataToProcess = new byte[latestCompressedImageData.Length];
                    System.Array.Copy(latestCompressedImageData, compressedDataToProcess, latestCompressedImageData.Length);
                    compressedFormatToProcess = latestCompressedFormat;
                    isCompressed = true;
                }
                else if (latestImageData != null)
                {
                    // Handle raw image
                    dataToProcess = new byte[latestImageData.Length];
                    System.Array.Copy(latestImageData, dataToProcess, latestImageData.Length);
                    widthToProcess = latestWidth;
                    heightToProcess = latestHeight;
                    encodingToProcess = latestEncoding;
                }
                
                hasNewImage = false;
                isCompressedImage = false; // Reset flag
                frameNumber = totalFramesReceived;
            }
        }

        // Handle compressed images - decompress on main thread
        if (compressedDataToProcess != null)
        {
            Texture2D decompressedTexture = new Texture2D(2, 2);
            
            try
            {
                if (ImageConversion.LoadImage(decompressedTexture, compressedDataToProcess))
                {
                    widthToProcess = decompressedTexture.width;
                    heightToProcess = decompressedTexture.height;
                    encodingToProcess = "rgb8"; // Decompressed is RGB

                    // Convert Color32 array to RGB byte array
                    int dataSize = widthToProcess * heightToProcess * 3;
                    dataToProcess = new byte[dataSize];

                    Color32[] pixels = decompressedTexture.GetPixels32();
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        int idx = i * 3;
                        dataToProcess[idx] = pixels[i].r;
                        dataToProcess[idx + 1] = pixels[i].g;
                        dataToProcess[idx + 2] = pixels[i].b;
                    }
                }
                else
                {
                    if (showDebugLogs && totalFramesProcessed < logFirstNFrames)
                        Debug.LogWarning($"[D435i Listener] Failed to decompress image. Format: {compressedFormatToProcess}, Data size: {compressedDataToProcess.Length}");
                    return;
                }
            }
            finally
            {
                if (decompressedTexture != null)
                {
                    Destroy(decompressedTexture);
                }
            }
        }

        if (dataToProcess == null) return;

        // Log first few messages on main thread (where Time API is safe)
        if (showDebugLogs && totalFramesProcessed < logFirstNFrames)
        {
            string imageType = isCompressed ? "compressed" : "raw";
            float interval = lastFrameTime > 0 ? (Time.realtimeSinceStartup - lastFrameTime) : 0;
            Debug.Log($"[D435i Listener] Processing {imageType} image #{frameNumber}: {widthToProcess}x{heightToProcess}, Encoding: {encodingToProcess}, Size: {dataToProcess.Length / 1024}KB, Interval: {interval:F3}s");
            lastFrameTime = Time.realtimeSinceStartup;
        }

        // Validate image data
        if (widthToProcess == 0 || heightToProcess == 0)
        {
            Debug.LogWarning($"[D435i Listener] Invalid image dimensions: {widthToProcess}x{heightToProcess}");
            return;
        }

        int expectedSize = widthToProcess * heightToProcess * 3;
        if (dataToProcess.Length != expectedSize)
        {
            Debug.LogWarning($"[D435i Listener] Image data size mismatch. Expected: {expectedSize}, Got: {dataToProcess.Length}");
            return;
        }

        // Create or resize texture
        if (tex == null || tex.width != widthToProcess || tex.height != heightToProcess)
        {
            if (tex != null) Destroy(tex);
            tex = new Texture2D(widthToProcess, heightToProcess, TextureFormat.RGB24, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            
            if (showDebugLogs)
                Debug.Log($"[D435i Listener] Created texture: {widthToProcess}x{heightToProcess}, Encoding: {encodingToProcess}");
        }

        // Convert BGR8 to RGB8 if needed (D435i typically sends BGR8)
        byte[] processedData = dataToProcess;
        
        if (encodingToProcess == "bgr8")
        {
            // Convert BGR to RGB
            processedData = new byte[dataToProcess.Length];
            for (int i = 0; i < dataToProcess.Length; i += 3)
            {
                processedData[i] = dataToProcess[i + 2];     // R = B
                processedData[i + 1] = dataToProcess[i + 1]; // G = G
                processedData[i + 2] = dataToProcess[i];     // B = R
            }
        }
        else if (encodingToProcess != "rgb8")
        {
            if (showDebugLogs && totalFramesProcessed < logFirstNFrames)
                Debug.LogWarning($"[D435i Listener] Unsupported encoding: {encodingToProcess}. Supported: rgb8, bgr8");
            return;
        }

        // Flip vertically if needed
        if (flipVertical)
        {
            processedData = FlipImageVertically(processedData, widthToProcess, heightToProcess);
        }

        // Load texture data
        tex.LoadRawTextureData(processedData);
        tex.Apply();

        // Update UI
        if (targetImage != null)
        {
            targetImage.texture = tex;
        }
        else if (showDebugLogs && totalFramesProcessed == 1)
        {
            Debug.LogWarning("[D435i Listener] RawImage (targetImage) is not assigned! Camera feed will not be displayed.");
        }

        totalFramesProcessed++;
        
        // Log periodic status every 100 frames
        if (showDebugLogs && totalFramesProcessed % 100 == 0)
        {
            Debug.Log($"[D435i Listener] Status: Received {totalFramesReceived} frames, Processed {totalFramesProcessed} frames, Resolution: {widthToProcess}x{heightToProcess}");
        }
    }

    /// <summary>
    /// Flip image vertically by swapping rows
    /// </summary>
    byte[] FlipImageVertically(byte[] imageData, int width, int height)
    {
        byte[] flipped = new byte[imageData.Length];
        int rowSize = width * 3; // 3 bytes per pixel (RGB)

        for (int y = 0; y < height; y++)
        {
            int sourceRow = y * rowSize;
            int destRow = (height - 1 - y) * rowSize;
            System.Array.Copy(imageData, sourceRow, flipped, destRow, rowSize);
        }

        return flipped;
    }

    void OnDestroy()
    {
        rawImageSub = null;
        compressedImageSub = null;
        node = null;
        
        if (tex != null)
        {
            Destroy(tex);
            tex = null;
        }
        
        if (showDebugLogs)
            Debug.Log($"[D435i Listener] Destroyed. Total received: {totalFramesReceived}, Total processed: {totalFramesProcessed}");
    }
}