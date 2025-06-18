using UnityEditor;
using UnityEngine;

public class ImpostorBaker
{
    private const string MenuItemPath = "Assets/Bake Impostor";

    [MenuItem(MenuItemPath, false, 30)]
    private static void BakeImpostorMenuAction()
    {
        GameObject selectedGameObject = Selection.activeGameObject;
        if (selectedGameObject == null)
        {
            Debug.LogError("No GameObject selected.");
            return;
        }

        ImpostorGenerator generator = selectedGameObject.GetComponent<ImpostorGenerator>();
        if (generator == null)
        {
            Debug.LogError("Selected GameObject does not have an ImpostorGenerator component.");
            return;
        }

        Debug.Log($"Attempting to bake impostor for: {generator.name}");

        // Define texture size for baking (can be made configurable)
        int textureWidth = generator.defaultBakeTextureWidth;
        int textureHeight = generator.defaultBakeTextureHeight;

        // 1. Create a temporary baking camera
        GameObject bakingCameraGo = new GameObject(generator.name + "_ImpostorBakingCamera");
        Camera bakingCamera = bakingCameraGo.AddComponent<Camera>();

        // 2. Position and orient the camera
        // This is a crucial step and might need sophisticated logic depending on desired baking angle.
        // For now, place it in front of the object based on its bounds and make it look at the center.
        Bounds objectBounds = new Bounds(selectedGameObject.transform.position, Vector3.zero);
        Renderer[] renderers = selectedGameObject.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            objectBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                objectBounds.Encapsulate(renderers[i].bounds);
            }
        }
        else if (generator.associatedMeshes != null && generator.associatedMeshes.Count > 0 && generator.associatedMeshes[0] != null)
        {
             // Fallback to manual bounds computation if no renderers (e.g. if objects are on specific layers not rendered by default)
             // This relies on ImpostorGenerator's internal bounds calculation if it's already populated.
             // For simplicity, let's assume renderers are available or generator.computeImpostorBounds() could be made public.
             // For now, we'll use a rough estimate or require renderers.
             Debug.LogWarning("Cannot accurately determine bounds if no Renderer components are present. Using selected object position as center.");
             objectBounds = new Bounds(selectedGameObject.transform.position, Vector3.one * 2); // Default if no renderers
        }


        // Position camera to view the object. Distance based on bounds size.
        float cameraDistance = Mathf.Max(objectBounds.size.x, objectBounds.size.y, objectBounds.size.z) * 2.0f;
        if (cameraDistance < 0.1f) cameraDistance = 5f; // Default distance for very small objects

        bakingCameraGo.transform.position = objectBounds.center - selectedGameObject.transform.forward * cameraDistance;
        bakingCameraGo.transform.LookAt(objectBounds.center);
        // Adjust field of view or use orthographic projection for better fit if needed
        // bakingCamera.orthographic = true; // Or perspective with tight FOV
        // bakingCamera.orthographicSize = Mathf.Max(objectBounds.extents.x, objectBounds.extents.y);
        bakingCamera.nearClipPlane = 0.01f * cameraDistance;
        bakingCamera.farClipPlane = 2.0f * cameraDistance;


        // 3. Configure it using a method from ImpostorManager
        // These layers would typically be defined in your project's TagManager
        // For editor tools, it's safer to get them by name if ImpostorManager instance isn't available
        // For now, assume fixed layer numbers or that the ImpostorManager static method handles it.
        int impostorRegenerationLayer = LayerMask.NameToLayer("Impostor Regeneration");
        if (impostorRegenerationLayer == -1) {
            Debug.LogError("Layer 'Impostor Regeneration' not found. Please create it in TagManager. Defaulting to layer 30 for baking.");
            impostorRegenerationLayer = 30; // Default fallback
        }

        ImpostorManager.SetupBakingCamera(bakingCamera, impostorRegenerationLayer, "URP_Renderer_Impostors");


        // 4. Define save path and asset name
        string objectName = selectedGameObject.name.Replace(" ", "_");
        string savePath = $"Assets/BakedImpostors/{objectName}";
        if (!System.IO.Directory.Exists(savePath))
        {
            System.IO.Directory.CreateDirectory(savePath);
        }
        string assetName = objectName + "_Impostor";

        // 5. Call generator.BakeImpostorInEditor
        bool success = false;
        try
        {
            // Ensure meshes are on the correct layer for baking camera to see them.
            // The BakeImpostorInEditor method itself handles moving meshes to the bakingLayer based on camera's culling mask.
            success = generator.BakeImpostorInEditor(bakingCamera, savePath, textureWidth, textureHeight, assetName);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Exception during impostor baking: {e}");
            success = false;
        }
        finally
        {
            // 6. Cleanup camera
            if (bakingCameraGo != null)
            {
                Object.DestroyImmediate(bakingCameraGo);
            }
        }

        // 7. Log success/failure
        if (success)
        {
            Debug.Log($"Successfully baked impostor assets for {generator.name} at {savePath}");

            // Create Prefab
            string materialPath = System.IO.Path.Combine(savePath, assetName + "_Material.mat");
            Material bakedMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            if (bakedMaterial != null)
            {
                GameObject prefabRoot = GameObject.CreatePrimitive(PrimitiveType.Cube);
                prefabRoot.name = objectName + "_ImpostorPrefab";

                // Use the same bounds calculated earlier for camera positioning
                prefabRoot.transform.position = objectBounds.center;
                prefabRoot.transform.localScale = objectBounds.size; // bounds.size is 2*extents

                MeshRenderer renderer = prefabRoot.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = bakedMaterial;

                Collider collider = prefabRoot.GetComponent<Collider>();
                if (collider != null)
                {
                    Object.DestroyImmediate(collider);
                }

                string prefabPath = System.IO.Path.Combine(savePath, prefabRoot.name + ".prefab");
                try
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                    Debug.Log($"Successfully created impostor prefab at {prefabPath}");
                    EditorUtility.DisplayDialog("Impostor Bake Complete", $"Successfully baked impostor for {generator.name}.\nAssets saved at: {savePath}\nPrefab created at: {prefabPath}", "OK");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Failed to save prefab for {generator.name}: {e.Message}");
                    EditorUtility.DisplayDialog("Impostor Bake Warning", $"Baked assets for {generator.name} but failed to create prefab. Check console.", "OK");
                }
                finally
                {
                    Object.DestroyImmediate(prefabRoot);
                }
            }
            else
            {
                Debug.LogError($"Could not load baked material at {materialPath} to create prefab.");
                EditorUtility.DisplayDialog("Impostor Bake Error", $"Failed to load baked material for {generator.name}. Prefab not created.", "OK");
            }
        }
        else
        {
            Debug.LogError($"Failed to bake impostor for {generator.name}");
            EditorUtility.DisplayDialog("Impostor Bake Error", $"Failed to bake impostor for {generator.name}. Check console for errors.", "OK");
        }
    }

    [MenuItem(MenuItemPath, true)]
    private static bool BakeImpostorMenuValidation()
    {
        GameObject selectedGameObject = Selection.activeGameObject;
        if (selectedGameObject == null)
            return false;

        return selectedGameObject.GetComponent<ImpostorGenerator>() != null;
    }
}
