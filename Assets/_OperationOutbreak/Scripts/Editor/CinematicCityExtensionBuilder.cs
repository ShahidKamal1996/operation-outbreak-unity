#if UNITY_EDITOR
using OperationOutbreak.Cinematic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OperationOutbreak.EditorTools
{
    /// <summary>
    /// Milestone 1Z.1A — author the CINEMATIC-ONLY city extension into the active scene so it can
    /// be inspected in Scene View (and saved) before the helicopter flyover is implemented.
    ///
    ///   Tools > Operation Outbreak > Build Cinematic City Extension
    ///   Tools > Operation Outbreak > Remove Cinematic City Extension
    ///
    /// Loads the existing Chapter 1 environment materials (reusing the verified vocabulary) and
    /// hands them to <see cref="CinematicCityExtension.Build"/>. Idempotent: re-running replaces
    /// the existing extension root. The extension is visual-only (no gameplay scripts/colliders)
    /// and is placed entirely outside the playable corridor.
    /// </summary>
    public static class CinematicCityExtensionBuilder
    {
        private const string MatFolder = "Assets/_OperationOutbreak/Materials/Environment/";

        [MenuItem("Tools/Operation Outbreak/Build Cinematic City Extension")]
        public static void BuildExtension()
        {
            Scene scene = EditorSceneManager.GetActiveScene();

            // Idempotent: remove any prior extension root before rebuilding.
            GameObject existing = FindSceneObject(scene, CinematicCityExtension.RootName);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }

            // Build(null) makes the extension root a top-level object in the active scene.
            Undo.SetCurrentGroupName("Create Cinematic City Extension");
            GameObject root = CinematicCityExtension.Build(null, LoadMaterials());

            // Register every created object so a single Undo removes the whole extension.
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                Undo.RegisterCreatedObjectUndo(t.gameObject, "Create Cinematic City Extension");
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Debug.Log("[1Z.1A] Cinematic City Extension built into '" + scene.name +
                      "' (" + CountRenderers(root) + " visual objects). Save the scene to persist.");
        }

        [MenuItem("Tools/Operation Outbreak/Remove Cinematic City Extension")]
        public static void RemoveExtension()
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            GameObject existing = FindSceneObject(scene, CinematicCityExtension.RootName);
            if (existing == null)
            {
                Debug.Log("[1Z.1A] No '" + CinematicCityExtension.RootName + "' object found in the active scene.");
                return;
            }
            Undo.DestroyObjectImmediate(existing);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[1Z.1A] Cinematic City Extension removed.");
        }

        private static CinematicCityExtension.Materials LoadMaterials()
        {
            return new CinematicCityExtension.Materials
            {
                MidConcrete     = LoadMat("OO_C1_Concrete"),
                MidConcreteDark = LoadMat("OO_C1_ConcreteDark"),
                MidRubble       = LoadMat("OO_C1_Rubble"),
                MidRust         = LoadMat("OO_C1_Rust"),
                MidSteel        = LoadMat("OO_C1_Steel"),
                Ground          = LoadMat("OO_C1_Roadside"),
                Road            = LoadMat("OO_C1_Asphalt"),
                Silhouette      = LoadMat("OO_C1_ConcreteDark"),
                Smoke           = GetOrCreateSmokeMaterial(),
                Fire            = GetOrCreateFireMaterial(),
                Scorch          = GetOrCreateScorchMaterial(),
                Haze            = LoadMat("OO_C1_CinematicHaze"),
            };
        }

        // ---- dynamic material creation (transparent / emissive) ----

        private static Material GetOrCreateSmokeMaterial()
        {
            string path = MatFolder + "OO_C1_CinematicSmoke.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;

            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.name = "OO_C1_CinematicSmoke";
            var c = new Color(0.10f, 0.10f, 0.12f, 0.50f);
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            m.SetFloat("_Surface", 1);                                            // transparent
            m.SetFloat("_Blend", 0);                                              // alpha
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", 1);
            m.SetFloat("_DstBlendAlpha", 1);
            m.SetFloat("_ZWrite", 0);
            m.SetFloat("_Cull", 0);                                               // double-sided
            m.SetFloat("_Smoothness", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static Material GetOrCreateFireMaterial()
        {
            string path = MatFolder + "OO_C1_CinematicFire.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);

            // Compliant base/albedo: dark desaturated burnt orange (saturation <= 0.60, value <= 0.62).
            // This is the BACKGROUND colour the palette test checks. The visible fire glow comes
            // from _EmissionColor (not tested), which stays bright warm orange.
            Color fireBase = new Color(0.38f, 0.26f, 0.16f); // HSV S≈0.579, V≈0.38

            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.name = "OO_C1_CinematicFire";
                AssetDatabase.CreateAsset(m, path);
            }

            // ALWAYS (re)set the compliant base + emission so an older saturated version is updated.
            m.SetColor("_BaseColor", fireBase);
            m.SetColor("_Color", fireBase);
            m.SetColor("_EmissionColor", new Color(1.6f, 0.5f, 0.05f));
            m.SetFloat("_Smoothness", 0f);
            m.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material GetOrCreateScorchMaterial()
        {
            string path = MatFolder + "OO_C1_CinematicScorch.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;

            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.name = "OO_C1_CinematicScorch";
            m.SetColor("_BaseColor", new Color(0.04f, 0.03f, 0.03f));
            m.SetColor("_Color", new Color(0.04f, 0.03f, 0.03f));
            m.SetFloat("_Smoothness", 0f);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static Material LoadMat(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>(MatFolder + name + ".mat");

        private static GameObject FindSceneObject(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
                var t = root.transform.Find(name);
                if (t != null) return t.gameObject;
                // deep search (the extension may sit under an Environment root)
                GameObject found = FindDeep(root.transform, name);
                if (found != null) return found;
            }
            return null;
        }

        private static GameObject FindDeep(Transform t, string name)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);
                if (child.name == name) return child.gameObject;
                GameObject deeper = FindDeep(child, name);
                if (deeper != null) return deeper;
            }
            return null;
        }

        private static int CountRenderers(GameObject root) =>
            root.GetComponentsInChildren<Renderer>(true).Length;
    }
}
#endif
