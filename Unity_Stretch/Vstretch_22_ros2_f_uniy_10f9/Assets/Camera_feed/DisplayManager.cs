using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Manages camera feed position swapping
/// Handles B button input to swap main/sub camera positions
/// Both cameras remain live and visible, just swap which is in main vs sub position
/// </summary>
public class DisplayManager : MonoBehaviour
{
    [Header("Debug")]
    [Tooltip("Show debug logs for button presses and position swaps")]
    public bool showDebugLogs = true;

    // Button state tracking
    private bool lastBButtonState = false;
    
    // Direct XR input (proven to work - like wristButton.cs)
    private UnityEngine.XR.InputDevice rightControllerDevice;

    void Start()
    {
        // Sync initial position with CameraPositionManager (D435i in main by default)
        CameraPositionManager.SetCameraPosition(true);
        
        if (showDebugLogs)
        {
            Debug.Log("DisplayManager: Initialized");
            Debug.Log("DisplayManager: Both cameras are always live and visible");
            Debug.Log("DisplayManager: Press B button (right controller) to swap main/sub camera positions");
            Debug.Log($"DisplayManager: Initial position - {(CameraPositionManager.IsD435iMain ? "D435i" : "D405")} in main position");
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
                Debug.Log($"DisplayManager: B Button (Direct XR) pressed: {bButtonPressed}, last state: {lastBButtonState}");
            }

            // Button just pressed (rising edge detection)
            if (bButtonPressed && !lastBButtonState)
            {
                // Button just pressed - swap camera positions
                if (showDebugLogs)
                {
                    Debug.Log("DisplayManager: B Button detected! Swapping camera positions...");
                }
                SwapCameraPositions();
            }
            lastBButtonState = bButtonPressed;
        }
        else if (showDebugLogs && UnityEngine.Time.frameCount % 120 == 0)
        {
            // Log warning if controller not found (every 2 seconds)
            Debug.LogWarning("DisplayManager: Right controller not found or B button not accessible!");
        }
    }

    /// <summary>
    /// Swap camera positions (main and sub)
    /// Both cameras remain live and visible
    /// Updates global CameraModeManager
    /// </summary>
    void SwapCameraPositions()
    {
        // Update global position manager (used by MainDisplay and SubDisplay)
        CameraPositionManager.ToggleCameraPosition();
        
        if (showDebugLogs)
        {
            Debug.Log($"DisplayManager: Camera positions swapped - {(CameraPositionManager.IsD435iMain ? "D435i" : "D405")} is now in main position");
        }
    }
}
