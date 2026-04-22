using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the main (large) display for camera feeds with DYNAMIC ROTATION (Option A)
/// Rotation changes automatically when cameras swap positions
/// </summary>
public class MainDisplay_OptionA : MonoBehaviour
{
    [Header("Camera Feed References")]
    [Tooltip("D435i camera RawImage (source feed)")]
    public RawImage d435iSourceImage;
    
    [Tooltip("D405 camera RawImage (source feed)")]
    public RawImage d405SourceImage;

    [Header("Display Settings")]
    [Tooltip("Main display RawImage (this will show the selected camera)")]
    public RawImage mainDisplayImage;
    
    [Tooltip("Main display Canvas RectTransform (for rotation control)")]
    public RectTransform mainDisplayCanvas;

    [Header("Rotation Settings")]
    [Tooltip("Rotation angle for D435i camera (when in main position)")]
    public float d435iRotation = -90f;
    
    [Tooltip("Rotation angle for D405 camera (when in main position)")]
    public float d405Rotation = 0f;

    [Header("Debug")]
    [Tooltip("Show debug logs")]
    public bool showDebugLogs = false;

    private bool lastIsD435iMain = true;

    void Start()
    {
        // Validate references
        if (d435iSourceImage == null)
        {
            Debug.LogError("MainDisplay_OptionA: d435iSourceImage not assigned! Please assign the D435i RawImage.");
        }
        
        if (d405SourceImage == null)
        {
            Debug.LogError("MainDisplay_OptionA: d405SourceImage not assigned! Please assign the D405 RawImage.");
        }
        
        if (mainDisplayImage == null)
        {
            Debug.LogError("MainDisplay_OptionA: mainDisplayImage not assigned! Please assign the main display RawImage.");
        }
        
        if (mainDisplayCanvas == null)
        {
            Debug.LogWarning("MainDisplay_OptionA: mainDisplayCanvas not assigned! Rotation will not work. Assign the MainDisplay_Canvas RectTransform.");
        }
        
        // Initialize rotation
        if (mainDisplayCanvas != null)
        {
            UpdateRotation();
        }
        
        if (showDebugLogs)
        {
            Debug.Log("MainDisplay_OptionA: Initialized with Dynamic Rotation");
        }
    }

    void Update()
    {
        // Update main display based on current camera position
        if (mainDisplayImage != null)
        {
            bool currentIsD435iMain = CameraPositionManager.IsD435iMain;
            
            if (currentIsD435iMain)
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
            
            // Update rotation when camera position changes
            if (mainDisplayCanvas != null && currentIsD435iMain != lastIsD435iMain)
            {
                UpdateRotation();
                lastIsD435iMain = currentIsD435iMain;
            }
        }
    }

    /// <summary>
    /// Update canvas rotation based on current camera position
    /// </summary>
    void UpdateRotation()
    {
        if (mainDisplayCanvas == null) return;
        
        float targetRotation = CameraPositionManager.IsD435iMain ? d435iRotation : d405Rotation;
        
        Vector3 currentRotation = mainDisplayCanvas.localEulerAngles;
        mainDisplayCanvas.localEulerAngles = new Vector3(currentRotation.x, currentRotation.y, targetRotation);
        
        if (showDebugLogs)
        {
            Debug.Log($"MainDisplay_OptionA: Rotation updated to {targetRotation}° (Camera: {(CameraPositionManager.IsD435iMain ? "D435i" : "D405")})");
        }
    }
}