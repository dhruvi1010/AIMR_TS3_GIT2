using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class GlobalRenderOverride : MonoBehaviour
{
    public static GlobalRenderOverride Instance;

    [Header("Robot Root")]
    public GameObject stretchRobotRoot;

    [Header("Materials")]
    public Material alwaysVisibleMaterial;

    // Store original materials for restoration
    private Dictionary<MeshRenderer, Material> originalMaterials = new Dictionary<MeshRenderer, Material>();

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"[GlobalRenderOverride] Scene loaded: {scene.name}");
        // Clear old material references when loading a new scene
        originalMaterials.Clear();
        
        // Auto-find stretch robot root if not assigned
        if (stretchRobotRoot == null)
        {
            stretchRobotRoot = GameObject.Find("stretch");
            if (stretchRobotRoot == null)
            {
                Debug.LogWarning("[GlobalRenderOverride] Stretch robot root not found! Please assign it in the Inspector.");
            }
        }
        
        ApplyAlwaysVisibleMaterial();
    }

    // Check if a GameObject is a child or descendant of the stretch robot root
    private bool IsChildOfStretchRobot(Transform obj)
    {
        if (stretchRobotRoot == null) return false;
        
        Transform current = obj;
        while (current != null)
        {
            if (current == stretchRobotRoot.transform)
            {
                return true;
            }
            current = current.parent;
        }
        return false;
    }

    public void ApplyAlwaysVisibleMaterial()
    {
        if (alwaysVisibleMaterial == null)
        {
            Debug.LogError("[GlobalRenderOverride] AlwaysVisibleMaterial is not assigned!");
            return;
        }

        if (stretchRobotRoot == null)
        {
            stretchRobotRoot = GameObject.Find("stretch");
            if (stretchRobotRoot == null)
            {
                Debug.LogError("[GlobalRenderOverride] Stretch robot root not found! Please assign it in the Inspector.");
                return;
            }
        }

        MeshRenderer[] renderers = FindObjectsOfType<MeshRenderer>(true);
        Debug.Log($"[GlobalRenderOverride] Found {renderers.Length} MeshRenderers in scene.");

        int swappedCount = 0;
        int skippedCount = 0;

        foreach (MeshRenderer r in renderers)
        {
            // Only process renderers that are children of the stretch robot
            if (!IsChildOfStretchRobot(r.transform))
            {
                skippedCount++;
                continue;
            }

            if (r.sharedMaterial == null)
            {
                Debug.LogWarning($"[GlobalRenderOverride] {r.gameObject.name} has no material!");
                continue;
            }

            // Skip if already using the always visible material
            if (r.sharedMaterial == alwaysVisibleMaterial)
            {
                continue;
            }

            // Store the original material if not already stored
            if (!originalMaterials.ContainsKey(r))
            {
                originalMaterials[r] = r.sharedMaterial;
            }

            // Swap to always visible material
            r.material = alwaysVisibleMaterial;
            swappedCount++;
            Debug.Log($"[GlobalRenderOverride] Swapped {r.gameObject.name} from '{originalMaterials[r].name}' to AlwaysVisibleMaterial.");
        }

        Debug.Log($"[GlobalRenderOverride] Total materials swapped: {swappedCount} (Skipped {skippedCount} non-robot objects)");
    }

    public void RestoreOriginalMaterial()
    {
        if (stretchRobotRoot == null)
        {
            stretchRobotRoot = GameObject.Find("stretch");
            if (stretchRobotRoot == null)
            {
                Debug.LogError("[GlobalRenderOverride] Stretch robot root not found! Please assign it in the Inspector.");
                return;
            }
        }

        MeshRenderer[] renderers = FindObjectsOfType<MeshRenderer>(true);
        int restoredCount = 0;

        foreach (MeshRenderer r in renderers)
        {
            // Only restore renderers that are children of the stretch robot
            if (!IsChildOfStretchRobot(r.transform))
            {
                continue;
            }

            // Check if we have a stored original material for this renderer
            if (originalMaterials.ContainsKey(r) && originalMaterials[r] != null)
            {
                r.material = originalMaterials[r];
                restoredCount++;
                Debug.Log($"[GlobalRenderOverride] Restored {r.gameObject.name} to '{originalMaterials[r].name}'.");
            }
        }

        // Clear the stored materials after restoration
        originalMaterials.Clear();

        Debug.Log($"[GlobalRenderOverride] Total materials restored: {restoredCount}");
    }
}
