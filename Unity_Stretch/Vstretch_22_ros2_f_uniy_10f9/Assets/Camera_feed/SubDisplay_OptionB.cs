using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the sub (small) display for camera feeds with FIXED ROTATION (Option B)
/// Rotation is set in Unity Editor and does not change at runtime
/// </summary>
public class SubDisplay_OptionB : MonoBehaviour
{
    [Header("Camera Feed References")]
    [Tooltip("D435i camera RawImage (source feed)")]
    public RawImage d435iSourceImage;
    
    [Tooltip("D405 camera RawImage (source feed)")]
    public RawImage d405SourceImage;

    [Header("Display Settings")]
    [Tooltip("Sub display RawImage (this will show the selected camera)")]
    public RawImage subDisplayImage;

    [Header("Debug")]
    [Tooltip("Show debug logs")]
    public bool showDebugLogs = false;

    void Start()
    {
        // Validate references
        if (d435iSourceImage == null)
        {
            Debug.LogError("SubDisplay_OptionB: d435iSourceImage not assigned! Please assign the D435i RawImage.");
        }
        
        if (d405SourceImage == null)
        {
            Debug.LogError("SubDisplay_OptionB: d405SourceImage not assigned! Please assign the D405 RawImage.");
        }
        
        if (subDisplayImage == null)
        {
            Debug.LogError("SubDisplay_OptionB: subDisplayImage not assigned! Please assign the sub display RawImage.");
        }
        
        if (showDebugLogs)
        {
            Debug.Log("SubDisplay_OptionB: Initialized with Fixed Rotation (set rotation in Unity Editor)");
        }
    }

    void Update()
    {
        // Update sub display based on current camera position (opposite of main)
        if (subDisplayImage != null)
        {
            if (CameraPositionManager.IsD435iMain)
            {
                // D435i is in main, so D405 goes in sub position
                if (d405SourceImage != null && d405SourceImage.texture != null)
                {
                    subDisplayImage.texture = d405SourceImage.texture;
                }
            }
            else
            {
                // D405 is in main, so D435i goes in sub position
                if (d435iSourceImage != null && d435iSourceImage.texture != null)
                {
                    subDisplayImage.texture = d435iSourceImage.texture;
                }
            }
        }
    }
}