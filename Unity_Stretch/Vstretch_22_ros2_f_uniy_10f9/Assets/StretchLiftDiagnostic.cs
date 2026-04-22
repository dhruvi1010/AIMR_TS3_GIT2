using UnityEngine;
using ROS2;

/// <summary>
/// Diagnostic script to test lift visualization
/// Attach this temporarily to debug movement issues
/// </summary>
public class StretchLiftDiagnostic : MonoBehaviour
{
    [Header("Test Settings")]
    [Tooltip("The lift transform to test")]
    public Transform testLiftTransform;

    [Tooltip("Test different axes")]
    public Vector3[] axesToTest = new Vector3[]
    {
        new Vector3(0, 1, 0),   // Y-up
        new Vector3(0, -1, 0),  // Y-down
        new Vector3(0, 0, 1),   // Z-forward
        new Vector3(0, 0, -1),  // Z-back
        new Vector3(1, 0, 0),   // X-right
        new Vector3(-1, 0, 0)   // X-left
    };

    [Tooltip("Test movement distance")]
    public float testDistance = 0.1f;

    [Header("Current Test")]
    public int currentAxisIndex = 0;
    public Vector3 currentAxis;

    [Header("Monitor")]
    public Vector3 initialPosition;
    public Vector3 currentPosition;
    public Vector3 positionDelta;

    private Vector3 startPosition;
    private bool isTestRunning = false;
    private float testStartTime;

    void Start()
    {
        if (testLiftTransform == null)
        {
            Debug.LogError("Test Lift Transform not assigned!");
            return;
        }

        startPosition = testLiftTransform.localPosition;
        initialPosition = startPosition;
        currentPosition = startPosition;

        Debug.Log($"=== STRETCH LIFT DIAGNOSTIC START ===");
        Debug.Log($"Testing transform: {testLiftTransform.name}");
        Debug.Log($"Initial position: {startPosition}");
        Debug.Log($"Press SPACE to cycle through axes");
        Debug.Log($"Press T to test current axis");
        Debug.Log($"Press R to reset position");
        Debug.Log($"Press L to log transform hierarchy");
        Debug.Log($"=====================================");
    }

    void Update()
    {
        if (testLiftTransform == null)
            return;

        // Monitor current position
        currentPosition = testLiftTransform.localPosition;
        positionDelta = currentPosition - initialPosition;

        // Cycle through axes
        if (Input.GetKeyDown(KeyCode.Space))
        {
            CycleAxis();
        }

        // Test current axis
        if (Input.GetKeyDown(KeyCode.T))
        {
            TestCurrentAxis();
        }

        // Reset position
        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetPosition();
        }

        // Log hierarchy
        if (Input.GetKeyDown(KeyCode.L))
        {
            LogTransformHierarchy();
        }

        // Auto-reset after test
        if (isTestRunning && Time.time - testStartTime > 2f)
        {
            ResetPosition();
            isTestRunning = false;
        }
    }

    void CycleAxis()
    {
        currentAxisIndex = (currentAxisIndex + 1) % axesToTest.Length;
        currentAxis = axesToTest[currentAxisIndex];

        string axisName = GetAxisName(currentAxis);
        Debug.Log($">>> Switched to axis: {axisName} {currentAxis}");
    }

    void TestCurrentAxis()
    {
        if (testLiftTransform == null)
            return;

        currentAxis = axesToTest[currentAxisIndex];
        Vector3 targetPosition = startPosition + (currentAxis * testDistance);

        testLiftTransform.localPosition = targetPosition;
        isTestRunning = true;
        testStartTime = Time.time;

        string axisName = GetAxisName(currentAxis);
        Debug.Log($">>> TESTING {axisName}: Moving from {startPosition} to {targetPosition}");
        Debug.Log($"    Delta: {currentAxis * testDistance}");
    }

    void ResetPosition()
    {
        if (testLiftTransform == null)
            return;

        testLiftTransform.localPosition = startPosition;
        Debug.Log($">>> Reset to: {startPosition}");
    }

    string GetAxisName(Vector3 axis)
    {
        if (axis == new Vector3(0, 1, 0)) return "Y-UP";
        if (axis == new Vector3(0, -1, 0)) return "Y-DOWN";
        if (axis == new Vector3(0, 0, 1)) return "Z-FORWARD";
        if (axis == new Vector3(0, 0, -1)) return "Z-BACK";
        if (axis == new Vector3(1, 0, 0)) return "X-RIGHT";
        if (axis == new Vector3(-1, 0, 0)) return "X-LEFT";
        return "CUSTOM";
    }

    void LogTransformHierarchy()
    {
        if (testLiftTransform == null)
            return;

        Debug.Log("=== TRANSFORM HIERARCHY ===");
        Transform current = testLiftTransform;
        int depth = 0;

        // Log upward hierarchy
        while (current != null)
        {
            string indent = new string(' ', depth * 2);
            Debug.Log($"{indent}? {current.name}");
            Debug.Log($"{indent}  Local Pos: {current.localPosition}");
            Debug.Log($"{indent}  World Pos: {current.position}");
            current = current.parent;
            depth++;
        }

        Debug.Log("=== CHILDREN ===");
        LogChildren(testLiftTransform, 0);
    }

    void LogChildren(Transform t, int depth)
    {
        string indent = new string(' ', depth * 2);
        Debug.Log($"{indent} down {t.name} (Local: {t.localPosition})");

        foreach (Transform child in t)
        {
            LogChildren(child, depth + 1);
        }
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 400, 300));
        GUILayout.Box("=== LIFT DIAGNOSTIC ===");

        if (testLiftTransform != null)
        {
            GUILayout.Label($"Transform: {testLiftTransform.name}");
            GUILayout.Label($"Initial: {initialPosition}");
            GUILayout.Label($"Current: {currentPosition}");
            GUILayout.Label($"Delta: {positionDelta}");
            GUILayout.Label("---");
            GUILayout.Label($"Testing Axis: {GetAxisName(currentAxis)}");
            GUILayout.Label($"Axis Vector: {currentAxis}");
            GUILayout.Label($"Test Distance: {testDistance}");
        }
        else
        {
            GUILayout.Label(" No transform assigned!");
        }

        GUILayout.Label("---");
        GUILayout.Label("CONTROLS:");
        GUILayout.Label("SPACE = Cycle axis");
        GUILayout.Label("T = Test current axis");
        GUILayout.Label("R = Reset position");
        GUILayout.Label("L = Log hierarchy");

        GUILayout.EndArea();
    }

    void OnDrawGizmos()
    {
        if (testLiftTransform == null)
            return;

        // Draw all test axes
        for (int i = 0; i < axesToTest.Length; i++)
        {
            Vector3 axis = axesToTest[i];
            Color color = (i == currentAxisIndex) ? Color.green : Color.gray;

            Gizmos.color = color;
            Vector3 start = testLiftTransform.position;
            Vector3 end = start + (axis * testDistance);
            Gizmos.DrawLine(start, end);
            Gizmos.DrawSphere(end, 0.01f);
        }

        // Draw current position
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(testLiftTransform.position, 0.02f);
    }
}