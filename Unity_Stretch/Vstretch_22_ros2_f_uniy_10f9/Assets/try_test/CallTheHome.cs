using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CallTheHome - Simple script to trigger robot homing with Enter key
/// 
/// Press Enter key to send home command to the physical Stretch 3 robot
/// </summary>
public class CallTheHome : MonoBehaviour
{
    [Header("Home Robot Reference")]
    [Tooltip("Assign HomeRobot component here, or it will be found automatically")]
    public HomeRobot homeRobot;
    
    [Header("Settings")]
    [Tooltip("Show debug messages")]
    public bool showDebugLogs = true;
    
    [Tooltip("Key to press for homing (default: Enter/Return)")]
    public KeyCode homeKey = KeyCode.Return; // Enter key
    
    void Start()
    {
        // Try to find HomeRobot if not assigned
        if (homeRobot == null)
        {
            homeRobot = FindObjectOfType<HomeRobot>();
            
            if (homeRobot == null)
            {
                Debug.LogError("CallTheHome: HomeRobot component not found! Please add HomeRobot script to a GameObject in the scene.");
            }
            else if (showDebugLogs)
            {
                Debug.Log("CallTheHome: Found HomeRobot component automatically");
            }
        }
        
        if (showDebugLogs && homeRobot != null)
        {
            Debug.Log($"CallTheHome: Ready! Press {homeKey} to home the robot");
        }
    }

    void Update()
    {
        // Check if Enter key is pressed
        if (Input.GetKeyDown(homeKey))
        {
            if (homeRobot == null)
            {
                Debug.LogError("CallTheHome: Cannot home robot - HomeRobot component not found!");
                return;
            }
            
            if (showDebugLogs)
            {
                Debug.Log("CallTheHome: Enter key pressed - Sending home command to robot...");
            }
            
            // Call the Home() method to send command to robot
            bool success = homeRobot.Home();
            
            if (!success)
            {
                if (showDebugLogs)
                {
                    Debug.LogWarning("CallTheHome: Home command was not sent. Robot may already be homing or ROS2 not connected.");
                }
            }
        }
    }
}
