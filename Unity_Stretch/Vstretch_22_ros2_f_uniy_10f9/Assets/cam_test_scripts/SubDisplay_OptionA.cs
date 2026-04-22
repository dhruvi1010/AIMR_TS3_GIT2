using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the sub (small) display for camera feeds with DYNAMIC ROTATION (Option A)
/// Rotation changes automatically when cameras swap positions
/// </summary>
public class SubDisplay_OptionA : MonoBehaviour
{
    [Header("Camera Feed References")]
    [Tooltip("D435i camera RawImage (source feed)")]
    public RawImage d435iSourceImage;
    
    [Tooltip("D405 camera RawImage (source feed)")]
    public RawImage d405SourceImage;

    [Header("Display Settings")]
    [Tooltip("Sub display RawImage (this will show the selected camera)")]
    public RawImage subDisplayImage;
    
    [Tooltip("Sub display Canvas RectTransform (for rotation control)")]
    public RectTransform subDisplayCanvas;

    [Header("Rotation Settings")]
    [Tooltip("Rotation angle for D435i camera (when in sub position)")]
    public float d435iRotation = -90f;
    
    [Tooltip("Rotation angle for D405 camera (when in sub position)")]
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
            Debug.LogError("SubDisplay_OptionA: d435iSourceImage not assigned! Please assign the D435i RawImage.");
        }
        
        if (d405SourceImage == null)
        {
            Debug.LogError("SubDisplay_OptionA: d405SourceImage not assigned! Please assign the D405 RawImage.");
        }
        
        if (subDisplayImage == null)
        {
            Debug.LogError("SubDisplay_OptionA: subDisplayImage not assigned! Please assign the sub display RawImage.");
        }
        
        if (subDisplayCanvas == null)
        {
            Debug.LogWarning("SubDisplay_OptionA: subDisplayCanvas not assigned! Rotation will not work. Assign the SubDisplay_Canvas RectTransform.");
        }
        
        // Initialize rotation
        if (subDisplayCanvas != null)
        {
            UpdateRotation();
        }
        
        if (showDebugLogs)
        {
            Debug.Log("SubDisplay_OptionA: Initialized with Dynamic Rotation");
        }
    }

    void Update()
    {
        // Update sub display based on current camera position (opposite of main)
        if (subDisplayImage != null)
        {
            bool currentIsD435iMain = CameraPositionManager.IsD435iMain;
            
            if (currentIsD435iMain)
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
            
            // Update rotation when camera position changes
            if (subDisplayCanvas != null && currentIsD435iMain != lastIsD435iMain)
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
        if (subDisplayCanvas == null) return;
        
        // Sub display shows opposite camera of main
        bool isD435iInSub = !CameraPositionManager.IsD435iMain;
        float targetRotation = isD435iInSub ? d435iRotation : d405Rotation;
        
        Vector3 currentRotation = subDisplayCanvas.localEulerAngles;
        subDisplayCanvas.localEulerAngles = new Vector3(currentRotation.x, currentRotation.y, targetRotation);
        
        if (showDebugLogs)
        {
            Debug.Log($"SubDisplay_OptionA: Rotation updated to {targetRotation}° (Camera: {(isD435iInSub ? "D435i" : "D405")})");
        }
    }
}