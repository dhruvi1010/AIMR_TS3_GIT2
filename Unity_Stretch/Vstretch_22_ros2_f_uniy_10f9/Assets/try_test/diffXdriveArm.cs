using System.Collections.Generic;
using UnityEngine;

public class diffXdriveArm : MonoBehaviour
{
    private ArticulationBody[] armJoints;
    private int selectedIndex = 0;
    private int previousIndex = 0;
    
    // Force limit is constant for all joints
    private const float FORCE_LIMIT = 1500f;
    
    // Varying stiffness and damping values from screenshot
    // Index mapping: [0]=l3, [1]=l2, [2]=l1, [3]=l0
    private readonly float[] stiffnessValues = { 100000f, 100000f, 10000f, 100000f };
    private readonly float[] dampingValues = { 10f, 100f, 300f, 500f };
    
    [Header("Control")]
    public bool enableKeyboardControl = true;
    public float speed = 0.1f;  // m/s for movement
    
    [Header("Display")]
    [InspectorReadOnly(hideInEditMode: true)]
    public string selectedJoint;
    
    void Start()
    {
        // Find all arm joints - ArticulationBody is on the link GameObjects
        ArticulationBody[] allJoints = GetComponentsInChildren<ArticulationBody>();
        List<ArticulationBody> armJointsList = new List<ArticulationBody>();
        
        // Look for link names, not joint names
        // Order matters: l3, l2, l1, l0 to match stiffness/damping arrays
        string[] armLinkNames = { "link_arm_l3", "link_arm_l2", "link_arm_l1", "link_arm_l0" };
        
        Debug.Log($"[diffXdriveArm] Searching through {allJoints.Length} total ArticulationBody components...");
        
        // Find joints in the correct order (l3, l2, l1, l0)
        foreach (string linkName in armLinkNames)
        {
            foreach (ArticulationBody joint in allJoints)
            {
                // Check if it's a prismatic joint and matches the current link name
                if (joint.jointType == ArticulationJointType.PrismaticJoint && 
                    joint.name.Contains(linkName) && 
                    !armJointsList.Contains(joint))
                {
                    armJointsList.Add(joint);
                    Debug.Log($"[diffXdriveArm] Found arm joint: {joint.name} (Type: {joint.jointType})");
                    break;
                }
            }
        }
        
        armJoints = armJointsList.ToArray();
        
        if (armJoints.Length == 0)
        {
            Debug.LogError("[diffXdriveArm] No arm joints found! Check joint names and types.");
            return;
        }
        
        if (armJoints.Length != 4)
        {
            Debug.LogWarning($"[diffXdriveArm] Expected 4 arm joints, found {armJoints.Length}. Stiffness/damping arrays may not match.");
        }
        
        // Configure drive parameters for each arm joint with varying values
        for (int i = 0; i < armJoints.Length; i++)
        {
            ArticulationBody joint = armJoints[i];
            
            // Get stiffness and damping for this joint (use index, or default if out of range)
            float stiffness = (i < stiffnessValues.Length) ? stiffnessValues[i] : 10000f;
            float damping = (i < dampingValues.Length) ? dampingValues[i] : 175f;
            
            ArticulationDrive drive = joint.xDrive;
            drive.stiffness = stiffness;
            drive.damping = damping;
            drive.forceLimit = FORCE_LIMIT;
            joint.xDrive = drive;
            
            joint.jointFriction = 5f;
            joint.linearDamping = 2f;
            
            Debug.Log($"[diffXdriveArm] Configured {joint.name}: Stiffness={stiffness}, Damping={damping}, ForceLimit={FORCE_LIMIT}, CurrentTarget={drive.target}");
        }
        
        Debug.Log($"[diffXdriveArm] Successfully configured {armJoints.Length} arm joints with varying parameters.");
        
        // Initialize selected joint
        if (armJoints.Length > 0)
        {
            selectedIndex = 0;
            DisplaySelectedJoint(selectedIndex);
        }
    }
    
    void SetSelectedJointIndex(int index)
    {
        if (armJoints != null && armJoints.Length > 0)
        {
            selectedIndex = (index + armJoints.Length) % armJoints.Length;
        }
    }
    
    void DisplaySelectedJoint(int index)
    {
        if (index >= 0 && index < armJoints.Length && armJoints[index] != null)
        {
            selectedJoint = armJoints[index].name + " (" + index + ")";
        }
    }
    
    void Update()
    {
        if (!enableKeyboardControl || armJoints == null || armJoints.Length == 0) return;
        
        // Handle joint selection with Left/Right arrow keys
        bool selectionInputRight = Input.GetKeyDown(KeyCode.RightArrow);
        bool selectionInputLeft = Input.GetKeyDown(KeyCode.LeftArrow);
        
        SetSelectedJointIndex(selectedIndex); // Ensure valid range
        
        if (selectionInputLeft)
        {
            SetSelectedJointIndex(selectedIndex - 1);
            DisplaySelectedJoint(selectedIndex);
            Debug.Log($"[diffXdriveArm] Selected: {selectedJoint}");
        }
        else if (selectionInputRight)
        {
            SetSelectedJointIndex(selectedIndex + 1);
            DisplaySelectedJoint(selectedIndex);
            Debug.Log($"[diffXdriveArm] Selected: {selectedJoint}");
        }
        
        // Handle movement with Up/Down arrow keys (Vertical axis)
        float moveDirection = Input.GetAxis("Vertical");
        
        if (moveDirection != 0f && selectedIndex >= 0 && selectedIndex < armJoints.Length)
        {
            ArticulationBody joint = armJoints[selectedIndex];
            if (joint != null)
            {
                ArticulationDrive drive = joint.xDrive;
                float newTarget = drive.target + moveDirection * speed * Time.deltaTime;
                newTarget = Mathf.Clamp(newTarget, drive.lowerLimit, drive.upperLimit);
                drive.target = newTarget;
                joint.xDrive = drive;
            }
        }
    }
    
    void OnGUI()
    {
        if (enableKeyboardControl)
        {
            GUIStyle centeredStyle = GUI.skin.GetStyle("Label");
            centeredStyle.alignment = TextAnchor.UpperCenter;
            GUI.Label(new Rect(Screen.width / 2 - 200, 10, 400, 20), "Press left/right arrow keys to select an arm joint.", centeredStyle);
            GUI.Label(new Rect(Screen.width / 2 - 200, 30, 400, 20), "Press up/down arrow keys to move " + selectedJoint + ".", centeredStyle);
        }
    }
}
