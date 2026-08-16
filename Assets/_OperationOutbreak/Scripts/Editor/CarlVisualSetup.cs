#if UNITY_EDITOR
using OperationOutbreak.Player;
using UnityEditor;
using UnityEngine;

namespace OperationOutbreak.EditorTools
{
    /// <summary>
    /// Milestone 1O.5 - one-click, idempotent setup for the Carl player visual.
    ///
    /// WHY THIS EXISTS RATHER THAN RAW SCENE YAML:
    /// instantiating an imported FBX writes a model-prefab instance whose internal
    /// fileIDs (SkinnedMeshRenderer, every bone Transform, the generated Avatar) are
    /// produced by Unity's importer on the machine that imports the model. Those IDs
    /// cannot be authored by hand without risking missing references, so the mesh is
    /// placed through the real AssetDatabase/PrefabUtility API instead. Everything the
    /// tool does is deterministic and re-runnable: running it twice leaves exactly one
    /// Carl in the scene.
    ///
    /// USAGE: Tools > Operation Outbreak > Set Up Carl Player Visual
    /// (with Gameplay_Prototype open), then save the scene.
    /// </summary>
    public static class CarlVisualSetup
    {
        private const string CarlModelPath = "Assets/_OperationOutbreak/Art/Characrters/Player/carl.fbx";
        private const string CarlMaterialPath = "Assets/_OperationOutbreak/Materials/Player/Carl_Player.mat";
        private const string ControllerPath = "Assets/_OperationOutbreak/Art/Animations/Player/Carl_Player.controller";

        private const string PlayerName = "Player";
        private const string CarlVisualName = "CarlVisual";
        private const string CarlInstanceName = "Carl";
        private const string PrototypeVisualName = "PrototypeVisual";

        /// <summary>Milestone 1P.5 - the Toon Soldier visual layer. Toggled off by this tool
        /// when Carl is restored as the active presentation, never deleted.</summary>
        private const string ToonSoldierVisualName = "ToonSoldierVisual";

        [MenuItem("Tools/Operation Outbreak/Set Up Carl Player Visual")]
        public static void SetUpCarlVisual()
        {
            GameObject player = GameObject.Find(PlayerName);
            if (player == null)
            {
                EditorUtility.DisplayDialog(
                    "Carl Setup",
                    "No GameObject named 'Player' found. Open Gameplay_Prototype first.",
                    "OK");
                return;
            }

            Transform carlVisual = player.transform.Find(CarlVisualName);
            if (carlVisual == null)
            {
                var go = new GameObject(CarlVisualName);
                Undo.RegisterCreatedObjectUndo(go, "Create CarlVisual");
                go.transform.SetParent(player.transform, false);
                carlVisual = go.transform;
            }

            // Visual-only correction lives here and nowhere else: the Player root sits at
            // y = 1, and Carl's feet are authored at his own origin, so the visual is
            // pushed down one unit to place the feet on the ground plane at y = 0.
            carlVisual.localPosition = new Vector3(0f, -1f, 0f);
            carlVisual.localRotation = Quaternion.identity;
            carlVisual.localScale = Vector3.one;

            // Exactly one Carl: remove any previous instance before adding a fresh one.
            for (int i = carlVisual.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(carlVisual.GetChild(i).gameObject);
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CarlModelPath);
            if (model == null)
            {
                EditorUtility.DisplayDialog("Carl Setup", "carl.fbx not found at:\n" + CarlModelPath, "OK");
                return;
            }

            var carl = (GameObject)PrefabUtility.InstantiatePrefab(model, carlVisual);
            Undo.RegisterCreatedObjectUndo(carl, "Instantiate Carl");
            carl.name = CarlInstanceName;
            carl.transform.localPosition = Vector3.zero;
            carl.transform.localRotation = Quaternion.identity;
            carl.transform.localScale = Vector3.one;

            // Material
            var material = AssetDatabase.LoadAssetAtPath<Material>(CarlMaterialPath);
            if (material != null)
            {
                foreach (var renderer in carl.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = renderer.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        mats[i] = material;
                    }

                    renderer.sharedMaterials = mats;
                }
            }

            // Animator: production controller, imported humanoid avatar, root motion OFF.
            var animator = carl.GetComponent<Animator>();
            if (animator == null)
            {
                animator = carl.AddComponent<Animator>();
            }

            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (controller != null)
            {
                animator.runtimeAnimatorController = controller;
            }

            var importer = AssetImporter.GetAtPath(CarlModelPath) as ModelImporter;
            if (importer != null && animator.avatar == null)
            {
                foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(CarlModelPath))
                {
                    if (sub is Avatar avatar && avatar.isValid)
                    {
                        animator.avatar = avatar;
                        break;
                    }
                }
            }

            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Prototype visual: retained as a disabled fallback, never deleted.
            Transform prototype = player.transform.Find(PrototypeVisualName);
            if (prototype != null)
            {
                foreach (var renderer in prototype.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = false;
                }
            }

            // Bridge wiring
            var bridge = player.GetComponent<PlayerAnimationBridge>();
            if (bridge == null)
            {
                bridge = Undo.AddComponent<PlayerAnimationBridge>(player);
            }

            var so = new SerializedObject(bridge);
            so.FindProperty("animator").objectReferenceValue = animator;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Milestone 1P.5 - fallback swap: Carl becomes the active presentation again,
            // and the Toon Soldier visual (if present) is parked inactive. Idempotent -
            // running the soldier tool afterwards reverses the toggle.
            carlVisual.gameObject.SetActive(true);

            Transform toonSoldierVisual = player.transform.Find(ToonSoldierVisualName);
            if (toonSoldierVisual != null)
            {
                toonSoldierVisual.gameObject.SetActive(false);
            }

            EditorUtility.SetDirty(player);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(player.scene);

            Debug.Log(
                $"[1O.5] Carl visual ready. Avatar valid: {(animator.avatar != null && animator.avatar.isValid)}, " +
                $"controller: {(controller != null ? controller.name : "MISSING")}, root motion: {animator.applyRootMotion}. " +
                "Save the scene to persist.", player);
        }

        /// <summary>
        /// Reports the bone a weapon would attach to in a later milestone. Read-only:
        /// nothing is moved, created or reparented. Weapon attachment is out of scope for 1O.5.
        /// </summary>
        [MenuItem("Tools/Operation Outbreak/Report Carl Weapon Bone")]
        public static void ReportWeaponBone()
        {
            GameObject player = GameObject.Find(PlayerName);
            Transform carlVisual = player != null ? player.transform.Find(CarlVisualName) : null;
            Transform carl = carlVisual != null && carlVisual.childCount > 0 ? carlVisual.GetChild(0) : null;
            var animator = carl != null ? carl.GetComponent<Animator>() : null;

            if (animator == null || !animator.isHuman)
            {
                Debug.LogWarning("[1O.5] No humanoid Animator found under CarlVisual.");
                return;
            }

            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Debug.Log(
                rightHand != null
                    ? $"[1O.5] Weapon attach bone (future milestone): '{rightHand.name}' at path '{GetPath(rightHand)}'."
                    : "[1O.5] Right hand bone not mapped on this avatar.",
                player);
        }

        private static string GetPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }

            return path;
        }
    }
}
#endif
