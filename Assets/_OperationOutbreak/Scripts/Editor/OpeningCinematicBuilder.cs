#if UNITY_EDITOR
using OperationOutbreak.Cinematic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OperationOutbreak.EditorTools
{
    /// <summary>
    /// Milestone 1Z.1B — authors the [Cinematic] Opening Sequence hierarchy into the active scene.
    /// Idempotent: re-running replaces the existing root. Loads the real Copter_2 model.
    ///
    ///   Tools ▸ Operation Outbreak ▸ Build/Refresh Opening Cinematic
    /// </summary>
    public static class OpeningCinematicBuilder
    {
        private const string RootName = "[Cinematic] Opening Sequence";
        private const string ModelPath = "Helicopter/Model/Copter_2";

        private static readonly Vector3[] PathPositions =
        {
            new Vector3(-35f, 65f, -55f),   // high, left, behind the city (establishing)
            new Vector3(-20f, 60f, -10f),    // descending, approaching
            new Vector3(0f, 55f, 30f),       // over the near city
            new Vector3(12f, 50f, 60f),      // over corridor, descending
            new Vector3(0f, 45f, 90f),        // high transition point (above far corridor end)
        };

        [MenuItem("Tools/Operation Outbreak/Build/Refresh Opening Cinematic")]
        public static void Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            // Idempotent: remove prior root.
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == RootName) { Undo.DestroyObjectImmediate(go); break; }
            }

            GameObject root = BuildInto(null);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                Undo.RegisterCreatedObjectUndo(t.gameObject, "Create Opening Cinematic");
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Debug.Log("[1Z.1B] Opening Cinematic hierarchy built. Press Play to preview the exterior flyover.");
        }

        /// <summary>Builds the full hierarchy under <paramref name="parent"/> (null = scene root).</summary>
        public static GameObject BuildInto(Transform parent)
        {
            // Idempotent within parent.
            if (parent != null)
                for (int i = 0; i < parent.childCount; i++)
                    if (parent.GetChild(i).name == RootName) { Object.DestroyImmediate(parent.GetChild(i).gameObject); break; }

            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);

            // --- Helicopter flight root + visual ---
            var flightRoot = new GameObject("HelicopterFlightRoot");
            flightRoot.transform.SetParent(root.transform, false);
            flightRoot.transform.position = PathPositions[0];

            var heliVisual = new GameObject("HelicopterVisual");
            heliVisual.transform.SetParent(flightRoot.transform, false);

            GameObject model = BuildHelicopterModel(heliVisual.transform);
            // Copter_2's nose may not align with Unity +Z. Apply a yaw correction on the visual
            // wrapper so the model flies nose-first without corrupting the flight root orientation.
            heliVisual.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Transform mainRotor = BuildRotorOverlay(heliVisual.transform, model);

            // Rotor presentation.
            var rotor = heliVisual.AddComponent<HelicopterRotorPresentation>();
            var rotorSO = new SerializedObject(rotor);
            rotorSO.FindProperty("mainRotor").objectReferenceValue = mainRotor;
            rotorSO.ApplyModifiedPropertiesWithoutUndo();

            // --- Flight path points ---
            var pathGroup = new GameObject("FlightPath");
            pathGroup.transform.SetParent(root.transform, false);
            var points = new Transform[PathPositions.Length];
            for (int i = 0; i < PathPositions.Length; i++)
            {
                var pt = new GameObject($"Point_{i:D2}");
                pt.transform.SetParent(pathGroup.transform, false);
                pt.transform.position = PathPositions[i];
                points[i] = pt.transform;
            }

            // --- Cameras ---
            var camGroup = new GameObject("Cameras");
            camGroup.transform.SetParent(root.transform, false);
            var camGo = new GameObject("ExteriorCamera");
            camGo.transform.SetParent(camGroup.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = 45f;
            cam.depth = 10f;
            cam.clearFlags = CameraClearFlags.Skybox;

            // --- Camera focus target (helicopter-relative, moves with the helicopter) ---
            var focusTarget = new GameObject("CameraFocusTarget");
            focusTarget.transform.SetParent(flightRoot.transform, false);
            focusTarget.transform.localPosition = new Vector3(0f, 1f, 4f); // slightly forward + above helicopter

            // --- Controller ---
            var controller = root.AddComponent<OpeningCinematicController>();
            var so = new SerializedObject(controller);
            so.FindProperty("flightRoot").objectReferenceValue = flightRoot.transform;
            so.FindProperty("helicopterVisual").objectReferenceValue = heliVisual.transform;
            so.FindProperty("exteriorCamera").objectReferenceValue = cam;
            so.FindProperty("cameraFocusTarget").objectReferenceValue = focusTarget.transform;

            var pathProp = so.FindProperty("flightPathPoints");
            pathProp.arraySize = points.Length;
            for (int i = 0; i < points.Length; i++)
                pathProp.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        private static GameObject BuildHelicopterModel(Transform parent)
        {
            GameObject prefab = Resources.Load<GameObject>(ModelPath);
            if (prefab != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                inst.name = "Copter_2";
                // Scale to cinematic size (~7 m).
                Bounds b = CombineBounds(inst);
                float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
                if (maxDim > 0.01f) inst.transform.localScale = Vector3.one * (7f / maxDim);
                b = CombineBounds(inst);
                inst.transform.localPosition = new Vector3(0f, -b.min.y + 0.1f, 0f);
                return inst;
            }

            // Fallback placeholder.
            var fallback = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            fallback.name = "TEMPORARY_HelicopterPlaceholder";
            fallback.transform.SetParent(parent, false);
            fallback.transform.localScale = new Vector3(1.2f, 3f, 1.2f);
            var col = fallback.GetComponent<Collider>(); if (col) col.enabled = false;
            Debug.LogWarning("[1Z.1B] Copter_2 not found in Resources — using temporary placeholder.");
            return fallback;
        }

        private static Transform BuildRotorOverlay(Transform parent, GameObject model)
        {
            // Copter_2 is a baked mesh with no separate rotor bone; add a spinning disc overlay.
            Bounds b = CombineBounds(model);
            float radius = Mathf.Max(1.5f, Mathf.Max(b.size.x, b.size.z) * 0.5f);
            var rotor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rotor.name = "MainRotor";
            rotor.transform.SetParent(parent, false);
            rotor.transform.localScale = new Vector3(radius, 0.03f, radius);
            rotor.transform.localRotation = Quaternion.identity;
            rotor.transform.localPosition = new Vector3(0f, b.max.y - parent.position.y + 0.1f, 0f);
            var col = rotor.GetComponent<Collider>(); if (col) col.enabled = false;
            var mr = rotor.GetComponent<MeshRenderer>();
            if (mr)
            {
                mr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                if (mr.sharedMaterial == null || mr.sharedMaterial.shader.name == "Hidden/InternalErrorShader")
                    mr.sharedMaterial = new Material(Shader.Find("Unlit/Color"));
                mr.sharedMaterial.color = new Color(0.05f, 0.05f, 0.05f, 0.4f);
            }
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return rotor.transform;
        }

        private static Bounds CombineBounds(GameObject go)
        {
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            return b;
        }
    }
}
#endif
