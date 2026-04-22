using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Manages D405 camera feed toggling
/// D435i is always visible (default)
/// Toggles D405 on/off when B button is pressed on right hand controller
/// Similar to wristButton.cs but for camera switching
/// </summary>
public class cameraToggleManager : MonoBehaviour
{
    [Header("Debug")]
    [Tooltip("Show debug logs for button presses and camera switches")]
    public bool showDebugLogs = true;

    // Button state tracking
    private bool lastBButtonState = false;
    
    // Direct XR input (proven to work - like wristButton.cs)
    private UnityEngine.XR.InputDevice rightControllerDevice;

    void Start()
    {
        // Sync initial mode with CameraModeManager (D405 starts hidden)
        CameraModeManager.SetD405(false);
        
        if (showDebugLogs)
        {
            Debug.Log("cameraToggleManager: Initialized");
            Debug.Log("cameraToggleManager: D435i camera is always visible (default)");
            Debug.Log("cameraToggleManager: Press B button (right controller) to toggle D405 camera on/off");
            Debug.Log($"cameraToggleManager: D405 camera is {(CameraModeManager.IsD405Active ? "enabled" : "disabled")}");
        }
    }

    void Update()
    {
        // Handle B button input (right controller secondary button)
        HandleBButtonInput();
    }

    /// <summary>
    /// Handle B button input using direct XR input (proven working method)
    /// B button on right controller = SecondaryButton
    /// </summary>
    void HandleBButtonInput()
    {
        // Get right controller directly (assumed to be always available)
        rightControllerDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        // Read B button directly using XR CommonUsages (SecondaryButton on right = B button)
        // This is the proven working method from wristButton.cs
        if (rightControllerDevice.isValid && 
            rightControllerDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool bButtonPressed))
        {
            // Debug: Log button state periodically
            if (showDebugLogs && UnityEngine.Time.frameCount % 60 == 0) // Log once per second
            {
                Debug.Log($"cameraToggleManager: B Button (Direct XR) pressed: {bButtonPressed}, last state: {lastBButtonState}");
            }

            // Button just pressed (rising edge detection)
            if (bButtonPressed && !lastBButtonState)
            {
                // Button just pressed - toggle D405 camera
                if (showDebugLogs)
                {
                    Debug.Log("cameraToggleManager: B Button detected! Toggling D405 camera...");
                }
                ToggleD405Camera();
            }
            lastBButtonState = bButtonPressed;
        }
        else if (showDebugLogs && UnityEngine.Time.frameCount % 120 == 0)
        {
            // Log warning if controller not found (every 2 seconds)
            Debug.LogWarning("cameraToggleManager: Right controller not found or B button not accessible!");
        }
    }

    /// <summary>
    /// Toggle D405 camera feed on/off
    /// D435i remains always visible
    /// Updates global CameraModeManager
    /// </summary>
    void ToggleD405Camera()
    {
        // Update global mode manager (used by camera feed scripts)
        CameraModeManager.ToggleD405();
        
        if (showDebugLogs)
        {
            Debug.Log($"cameraToggleManager: D405 camera {(CameraModeManager.IsD405Active ? "enabled" : "disabled")}");
        }
    }
}
