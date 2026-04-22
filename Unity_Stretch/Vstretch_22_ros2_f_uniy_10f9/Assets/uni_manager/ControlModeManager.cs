using UnityEngine;

/// <summary>
/// Static mode manager for coordinating control modes between different robot control scripts
/// Provides a single source of truth for control mode state
/// </summary>
public static class ControlModeManager
{
    /// <summary>
    /// Current control mode: true = Wrist Mode, false = Base Mode
    /// When true, left joystick controls wrist pitch/roll
    /// When false, left joystick controls mobile base
    /// </summary>
    public static bool IsWristMode { get; private set; } = false;

    /// <summary>
    /// Toggle between Wrist Mode and Base Mode
    /// </summary>
    public static void ToggleMode()
    {
        IsWristMode = !IsWristMode;
        
        if (Application.isPlaying)
        {
            Debug.Log($"ControlModeManager: Mode switched to {(IsWristMode ? "Wrist Mode" : "Base Mode")}");
            if (IsWristMode)
            {
                Debug.Log("ControlModeManager: Left joystick now controls wrist pitch/roll");
                Debug.Log("ControlModeManager: Left/Right triggers control wrist yaw");
            }
            else
            {
                Debug.Log("ControlModeManager: Left joystick now controls mobile base");
            }
        }
    }

    /// <summary>
    /// Set mode explicitly
    /// </summary>
    /// <param name="wristMode">true for Wrist Mode, false for Base Mode</param>
    public static void SetMode(bool wristMode)
    {
        IsWristMode = wristMode;
        
        if (Application.isPlaying)
        {
            Debug.Log($"ControlModeManager: Mode set to {(IsWristMode ? "Wrist Mode" : "Base Mode")}");
        }
    }

    /// <summary>
    /// Reset mode to default (Base Mode)
    /// </summary>
    public static void ResetMode()
    {
        IsWristMode = false;
        
        if (Application.isPlaying)
        {
            Debug.Log("ControlModeManager: Mode reset to Base Mode");
        }
    }
}
