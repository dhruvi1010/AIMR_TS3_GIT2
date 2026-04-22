using RosMessageTypes.Sensor;
using System;
using System.IO;
using System.Threading;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;
using UnityEngine.UI;

public class D435iVRFeed : MonoBehaviour
{
    [Header("ROS Settings")]
    //[SerializeField] private string topicName = "/camera/camera/color/image_raw";
    [SerializeField] private string topicName = "/camera/camera/color/image_raw/compressed";
    // [SerializeField] private bool useCompressedImages = false; // Set to true if using compressed image topic
    [SerializeField] public bool useCompressedImages = false;

    [Header("Display Settings")]
    [SerializeField] private RawImage leftEyeImage;
    [SerializeField] private RawImage rightEyeImage;
    [SerializeField] private bool useSingleImageForBothEyes = true;

    [Header("Performance")]
    [SerializeField] private bool showFPS = true;
    [SerializeField] private bool flipVertically = false; // Flip image vertically (ROS convention) - disabled by default for better performance
    [SerializeField] private Text debugText; // Optional: for showing debug info

    private Texture2D cameraTexture;
    private ROSConnection ros;
    private byte[] latestImageData;
    private int latestWidth;
    private int latestHeight;
    private string latestEncoding;
    private bool newImageReceived = false;
    private object imageLock = new object();
    private int totalFramesReceived = 0;
    private int totalFramesProcessed = 0;

    private int frameCount = 0;
    private float fps = 0;
    private float fpsTimer = 0;
    private float lastFrameTime = 0;
    private float averageFrameInterval = 0;

    void Start()
    {
        Debug.Log("D435i VR Feed starting...");

        // Verify references
        if (leftEyeImage == null)
        {
            Debug.LogError("Left Eye Image is not assigned!");
            return;
        }

        Debug.Log($"Left Eye Image assigned: {leftEyeImage.name}");
        if (rightEyeImage != null)
            Debug.Log($"Right Eye Image assigned: {rightEyeImage.name}");

        // Get ROS connection
        ros = ROSConnection.GetOrCreateInstance();
        Debug.Log($"ROS Connection instance obtained");

        // Auto-detect if using compressed images based on topic name
        bool isCompressedTopic = topicName.Contains("/compressed") || useCompressedImages;
        
        // Subscribe to camera topic (raw or compressed)
        if (isCompressedTopic)
        {
            ros.Subscribe<CompressedImageMsg>(topicName, CompressedImageCallback);
            Debug.Log($"Subscribed to compressed image topic: {topicName}");
        }
        else
        {
            ros.Subscribe<ImageMsg>(topicName, ImageCallback);
            Debug.Log($"Subscribed to raw image topic: {topicName}");
        }


    }

    //public void SaveBytes(byte[] data)
    //{

    //    var filePath = Path.Combine("D:\\Unity\\Vstretch_22_copy_bu", "MyBinaryFile.bin");
    //    Debug.Log($"Saving file to: {filePath}");

    //    try
    //    {
    //        File.WriteAllBytes(filePath, data);
    //        Debug.Log($"File saved to: {filePath}");
    //    }
    //    catch (Exception e)
    //    {
    //        Debug.LogError($"Something went wrong while writing a file: {e}");
    //    }
    //}

    void CompressedImageCallback(CompressedImageMsg msg)
    {
        // This runs on ROS thread, so  need to be thread-safe
        // Keep lock as short as possible - just copy the data
        
        // Log first message to verify callback is being called
        if (totalFramesReceived == 0)
        {
            Debug.Log($"CompressedImageCallback called! Format: '{msg.format}', Data size: {msg.data?.Length ?? 0}");
        }
        
        lock (imageLock)
        {
            // Validate message data first
            if (msg.data == null || msg.data.Length == 0)
            {
                Debug.LogWarning($"Received empty compressed image data. Format: '{msg.format}'");
                return;
            }

            // Decompress JPEG/PNG image
            Texture2D decompressedTexture = new Texture2D(2, 2);
            
            // Normalize format string (handle formats like "rgb8; jpeg compressed bgr8")
            string formatLower = msg.format.ToLower();
            bool isJpeg = formatLower.Contains("jpeg") || formatLower.Contains("jpg");
            bool isPng = formatLower.Contains("png");
            
            // Log format for debugging (first few messages)
            if (totalFramesReceived < 3)
            {
                Debug.Log($"Compressed image format: '{msg.format}' (detected as JPEG: {isJpeg}, PNG: {isPng})");
            }
            
            try
            {
                if (isJpeg)
                {
                    if (ImageConversion.LoadImage(decompressedTexture, msg.data))
                    {
                        latestWidth = decompressedTexture.width;
                        latestHeight = decompressedTexture.height;
                        latestEncoding = "rgb8"; // Decompressed is RGB

                        // Allocate or resize buffer if needed
                        int dataSize = latestWidth * latestHeight * 3;
                        if (latestImageData == null || latestImageData.Length != dataSize)
                        {
                            latestImageData = new byte[dataSize];
                        }

                        // Get raw RGB data from decompressed texture
                        Color32[] pixels = decompressedTexture.GetPixels32();
                        for (int i = 0; i < pixels.Length; i++)
                        {
                            int idx = i * 3;
                            latestImageData[idx] = pixels[i].r;
                            latestImageData[idx + 1] = pixels[i].g;
                            latestImageData[idx + 2] = pixels[i].b;
                        }

                        newImageReceived = true;
                    }
                    else
                    {
                        Debug.LogWarning($"Failed to decompress JPEG image. Format: {msg.format}, Data size: {msg.data.Length}");
                        return;
                    }
                }
                else if (isPng)
                {
                    if (ImageConversion.LoadImage(decompressedTexture, msg.data))
                    {
                        latestWidth = decompressedTexture.width;
                        latestHeight = decompressedTexture.height;
                        latestEncoding = "rgb8";

                        int dataSize = latestWidth * latestHeight * 3;
                        if (latestImageData == null || latestImageData.Length != dataSize)
                        {
                            latestImageData = new byte[dataSize];
                        }

                        Color32[] pixels = decompressedTexture.GetPixels32();
                        for (int i = 0; i < pixels.Length; i++)
                        {
                            int idx = i * 3;
                            latestImageData[idx] = pixels[i].r;
                            latestImageData[idx + 1] = pixels[i].g;
                            latestImageData[idx + 2] = pixels[i].b;
                        }

                        newImageReceived = true;
                    }
                    else
                    {
                        Debug.LogWarning($"Failed to decompress PNG image. Format: {msg.format}, Data size: {msg.data.Length}");
                        return;
                    }
                }
                else
                {
                    Debug.LogWarning($"Unsupported compressed image format: '{msg.format}'. Supported: JPEG, PNG");
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

        // Increment counters outside lock
        frameCount++;
        totalFramesReceived++;
        
        // Calculate time between frames
        float currentTime = Time.realtimeSinceStartup;
        if (lastFrameTime > 0)
        {
            float frameInterval = currentTime - lastFrameTime;
            averageFrameInterval = (averageFrameInterval * 0.9f) + (frameInterval * 0.1f);
        }
        lastFrameTime = currentTime;
        
        // Log first few messages for debugging
        if (totalFramesReceived <= 5)
        {
            float interval = lastFrameTime > 0 ? (currentTime - lastFrameTime) : 0;
            Debug.Log($"Compressed image received #{totalFramesReceived}: {latestWidth}x{latestHeight}, Format: {msg.format}, Compressed size: {msg.data.Length / 1024}KB, Interval: {interval:F3}s");
        }
    }

    void ImageCallback(ImageMsg msg)
    {
        // This runs on ROS thread, so need to be thread-safe
        // Keep lock as short as possible - just copy the data
        lock (imageLock)
        {
            // Validate message data first
            if (msg.data == null || msg.data.Length == 0)
            {
                Debug.LogWarning("Received empty image data");
                return;
            }

            latestWidth = (int)msg.width;
            latestHeight = (int)msg.height;
            latestEncoding = msg.encoding;

            // Allocate or resize buffer if needed
            if (latestImageData == null || latestImageData.Length != msg.data.Length)
            {
                latestImageData = new byte[msg.data.Length];
            }

            // Copy image data quickly
            System.Array.Copy(msg.data, latestImageData, msg.data.Length);
            newImageReceived = true;
        }

        // Increment counters outside lock
        frameCount++;
        totalFramesReceived++;
        
        // Calculate time between frames
        float currentTime = Time.realtimeSinceStartup;
        if (lastFrameTime > 0)
        {
            float frameInterval = currentTime - lastFrameTime;
            averageFrameInterval = (averageFrameInterval * 0.9f) + (frameInterval * 0.1f); // Moving average
        }
        lastFrameTime = currentTime;
        
        // Log first few messages for debugging
        if (totalFramesReceived <= 5)
        {
            float interval = lastFrameTime > 0 ? (currentTime - lastFrameTime) : 0;
            Debug.Log($"Image received #{totalFramesReceived}: {msg.width}x{msg.height}, Encoding: {msg.encoding}, Data size: {msg.data.Length / 1024}KB, Interval: {interval:F3}s");
        }
    }

    void Update()
    {
        // Update FPS counter
        fpsTimer += Time.deltaTime;
        if (fpsTimer >= 1.0f)
        {
            fps = frameCount / fpsTimer;
            int tempFrameCount = frameCount; // Store before reset
            frameCount = 0;
            fpsTimer = 0;

            if (showFPS)
            {
                float expectedFPS = averageFrameInterval > 0 ? (1.0f / averageFrameInterval) : 0;
                Debug.Log($"Camera FPS: {fps:F1} (Received {tempFrameCount} frames/sec, Avg interval: {averageFrameInterval:F3}s, Expected FPS: {expectedFPS:F1}, Total received: {totalFramesReceived}, Total processed: {totalFramesProcessed})");
            }
        }

        // Check if we have a new image to process
        // Copy data outside lock to avoid blocking incoming messages
        byte[] dataToProcess = null;
        int widthToProcess = 0;
        int heightToProcess = 0;
        string encodingToProcess = null;
        bool shouldProcess = false;

        lock (imageLock)
        {
            if (newImageReceived && latestImageData != null)
            {
                // Copy data for processing outside the lock
                dataToProcess = new byte[latestImageData.Length];
                System.Array.Copy(latestImageData, dataToProcess, latestImageData.Length);
                widthToProcess = latestWidth;
                heightToProcess = latestHeight;
                encodingToProcess = latestEncoding;
                shouldProcess = true;
                newImageReceived = false;
            }
        }

        // Process outside the lock to avoid blocking incoming messages
        if (shouldProcess)
        {
            ProcessImage(dataToProcess, widthToProcess, heightToProcess, encodingToProcess);
            totalFramesProcessed++;
        }

        // Update debug text if available
        if (debugText != null)
        {
            debugText.text = $"FPS: {fps:F1}\nResolution: {latestWidth}x{latestHeight}\nEncoding: {latestEncoding ?? "N/A"}\nReceived: {totalFramesReceived}\nProcessed: {totalFramesProcessed}";
        }
    }

    void ProcessImage(byte[] imageData, int width, int height, string encoding)
    {
        // Validate image data
        if (imageData == null || width == 0 || height == 0)
        {
            Debug.LogWarning($"Invalid image data - Data: {(imageData == null ? "null" : imageData.Length.ToString())}, Size: {width}x{height}");
            return;
        }

        // Validate expected data size
        int expectedSize = width * height * 3; // RGB/BGR = 3 bytes per pixel
        if (imageData.Length != expectedSize)
        {
            Debug.LogWarning($"Image data size mismatch. Expected: {expectedSize}, Got: {imageData.Length}, Size: {width}x{height}, Encoding: {encoding}");
            return;
        }

        // Create or resize texture on main thread
        if (cameraTexture == null || cameraTexture.width != width || cameraTexture.height != height)
        {
            // Destroy old texture if it exists
            if (cameraTexture != null)
            {
                Destroy(cameraTexture);
            }
            
            cameraTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
            cameraTexture.filterMode = FilterMode.Bilinear;
            cameraTexture.wrapMode = TextureWrapMode.Clamp;
            Debug.Log($"Created texture: {width}x{height}, Encoding: {encoding}");
        }

        // Process image based on encoding and flip if needed
        byte[] processedData;
        
        if (encoding == "rgb8")
        {
            processedData = imageData;
        }
        else if (encoding == "bgr8")
        {
            // Convert BGR to RGB (D435i typically sends BGR8)
            processedData = new byte[imageData.Length];
            for (int i = 0; i < imageData.Length; i += 3)
            {
                processedData[i] = imageData[i + 2];     // R = B
                processedData[i + 1] = imageData[i + 1]; // G = G
                processedData[i + 2] = imageData[i];     // B = R
            }
        }
        else
        {
            Debug.LogWarning($"Unsupported encoding: {encoding}. Supported: rgb8, bgr8");
            return;
        }

        // Flip vertically if needed (much faster to flip raw bytes than Colour array)
        if (flipVertically)
        {
            processedData = FlipImageDataVertically(processedData, width, height);
        }

        cameraTexture.LoadRawTextureData(processedData);
        cameraTexture.Apply();

        // Update UI
        if (leftEyeImage != null && cameraTexture != null)
        {
            leftEyeImage.texture = cameraTexture;
        }

        if (rightEyeImage != null && useSingleImageForBothEyes && cameraTexture != null)
        {
            rightEyeImage.texture = cameraTexture;
        }
    }

    // Optimized vertical flip using raw byte data (much faster than Color array operations)
    byte[] FlipImageDataVertically(byte[] imageData, int width, int height)
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
        if (ros != null)
        {
            ros.Unsubscribe(topicName);
            Debug.Log($"Unsubscribed from {topicName}");
        }

        if (cameraTexture != null)
        {
            Destroy(cameraTexture);
        }
    }
}