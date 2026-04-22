using UnityEngine;

/// <summary>
/// Static manager for camera position swapping (main vs sub display)
/// Tracks which camera is in main (large) position vs sub (small) position
/// Both cameras are always live and visible, this only tracks their display positions
/// </summary>
public static class CameraPositionManager
{
    /// <summary>
    /// Camera position state: true = D435i in main position, false = D405 in main position
    /// When true: D435i = main (large), D405 = sub (small)
    /// When false: D405 = main (large), D435i = sub (small)
    /// Both cameras are always live and visible
    /// </summary>
    public static bool IsD435iMain { get; private set; } = true;

    /// <summary>
    /// Toggle camera positions (swap main and sub)
    /// Both cameras remain live and visible
    /// </summary>
    public static void ToggleCameraPosition()
    {
        IsD435iMain = !IsD435iMain;

        if (Application.isPlaying)
        {
            Debug.Log($"CameraPositionManager: Camera positions swapped - {(IsD435iMain ? "D435i" : "D405")} is now in main position");
        }
    }

    /// <summary>
    /// Set camera position explicitly
    /// </summary>
    /// <param name="d435iMain">true for D435i in main, false for D405 in main</param>
    public static void SetCameraPosition(bool d435iMain)
    {
        IsD435iMain = d435iMain;

        if (Application.isPlaying)
        {
            Debug.Log($"CameraPositionManager: Camera position set - {(IsD435iMain ? "D435i" : "D405")} is in main position");
        }
    }

    /// <summary>
    /// Reset camera position to default (D435i in main)
    /// </summary>
    public static void ResetCameraPosition()
    {
        IsD435iMain = true;

        if (Application.isPlaying)
        {
            Debug.Log("CameraPositionManager: Camera position reset to default (D435i in main)");
        }
    }
}
