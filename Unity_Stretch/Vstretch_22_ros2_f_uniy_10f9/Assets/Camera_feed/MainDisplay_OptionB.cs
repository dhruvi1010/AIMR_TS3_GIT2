using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the main (large) display for camera feeds with FIXED ROTATION (Option B)
/// Rotation is set in Unity Editor and does not change at runtime
/// </summary>
public class MainDisplay_OptionB : MonoBehaviour
{
    [Header("Camera Feed References")]
    [Tooltip("D435i camera RawImage (source feed)")]
    public RawImage d435iSourceImage;
    
    [Tooltip("D405 camera RawImage (source feed)")]
    public RawImage d405SourceImage;

    [Header("Display Settings")]
    [Tooltip("Main display RawImage (this will show the selected camera)")]
    public RawImage mainDisplayImage;

    [Header("Debug")]
    [Tooltip("Show debug logs")]
    public bool showDebugLogs = false;

    void Start()
    {
        // Validate references
        if (d435iSourceImage == null)
        {
            Debug.LogError("MainDisplay_OptionB: d435iSourceImage not assigned! Please assign the D435i RawImage.");
        }
        
        if (d405SourceImage == null)
        {
            Debug.LogError("MainDisplay_OptionB: d405SourceImage not assigned! Please assign the D405 RawImage.");
        }
        
        if (mainDisplayImage == null)
        {
            Debug.LogError("MainDisplay_OptionB: mainDisplayImage not assigned! Please assign the main display RawImage.");
        }
        
        if (showDebugLogs)
        {
            Debug.Log("MainDisplay_OptionB: Initialized with Fixed Rotation (set rotation in Unity Editor)");
        }
    }

    void Update()
    {
        // Update main display based on current camera position
        if (mainDisplayImage != null)
        {
            if (CameraPositionManager.IsD435iMain)
            {
                // D435i is in main position - copy from D435i source
                if (d435iSourceImage != null && d435iSourceImage.texture != null)
                {
                    mainDisplayImage.texture = d435iSourceImage.texture;
                }
            }
            else
            {
                // D405 is in main position - copy from D405 source
                if (d405SourceImage != null && d405SourceImage.texture != null)
                {
                    mainDisplayImage.texture = d405SourceImage.texture;
                }
            }
        }
    }
}