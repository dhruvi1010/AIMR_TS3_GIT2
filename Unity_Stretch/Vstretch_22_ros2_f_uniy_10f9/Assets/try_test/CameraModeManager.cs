using UnityEngine;

/// <summary>
/// Static mode manager for coordinating camera display modes between different camera feed scripts
/// Provides a single source of truth for camera mode state
/// D435i is always visible (default), D405 can be toggled on/off
/// </summary>
public static class CameraModeManager
{
    /// <summary>
    /// D405 camera active state: true = D405 visible, false = D405 hidden
    /// D435i is always visible regardless of this value
    /// </summary>
    public static bool IsD405Active { get; private set; } = false;

    /// <summary>
    /// Toggle D405 camera feed on/off
    /// D435i remains always visible
    /// </summary>
    public static void ToggleD405()
    {
        IsD405Active = !IsD405Active;

        if (Application.isPlaying)
        {
            Debug.Log($"CameraModeManager: D405 camera {(IsD405Active ? "enabled" : "disabled")}");
        }
    }

    /// <summary>
    /// Set D405 camera state explicitly
    /// </summary>
    /// <param name="d405Active">true to show D405, false to hide D405</param>
    public static void SetD405(bool d405Active)
    {
        IsD405Active = d405Active;

        if (Application.isPlaying)
        {
            Debug.Log($"CameraModeManager: D405 camera {(IsD405Active ? "enabled" : "disabled")}");
        }
    }

    /// <summary>
    /// Reset D405 to default (hidden)
    /// D435i remains visible
    /// </summary>
    public static void ResetCamera()
    {
        IsD405Active = false;

        if (Application.isPlaying)
        {
            Debug.Log("CameraModeManager: D405 camera reset to hidden (D435i remains visible)");
        }
    }
}
