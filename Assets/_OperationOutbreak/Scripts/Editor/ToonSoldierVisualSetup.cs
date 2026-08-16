#if UNITY_EDITOR
using OperationOutbreak.Player;
using UnityEditor;
using UnityEngine;

namespace OperationOutbreak.EditorTools
{
    /// <summary>
    /// Milestone 1P.5 - one-click, idempotent setup for the Toon Soldier player visual.
    /// Mirrors the Milestone 1O.5 Carl tool exactly, so the project keeps ONE presentation
    /// workflow: an editor tool that instantiates/wires the model FBX (whose internal
    /// fileIDs are machine-generated), applies the project URP material, assigns the
    /// production controller + avatar, pins the single PlayerAnimationBridge to the
    /// visual's Animator, and toggles the visual layers.
    ///
    /// PRESENTATION SWAP CONTRACT (1P.5):
    ///   - "Set Up Toon Soldier Player Visual"  -> ToonSoldierVisual ACTIVE, CarlVisual INACTIVE, bridge -> soldier Animator.
    ///   - "Set Up Carl Player Visual" (1O.5)   -> CarlVisual ACTIVE, ToonSoldierVisual INACTIVE, bridge -> Carl Animator.
    ///   Each tool is idempotent: running it twice leaves exactly one instance and the same state.
    ///
    /// Unlike the 1O.5 Carl tool, an existing soldier instance is REUSED rather than
    /// re-instantiated, so the scene-level controller assignment and bridge reference
    /// authored in Gameplay_Prototype.unity remain valid on machines that already carry
    /// the committed instance.
    ///
    /// USAGE: Tools > Operation Outbreak > Set Up Toon Soldier Player Visual
    /// (with Gameplay_Prototype open), then save the scene.
    /// </summary>
    public static class ToonSoldierVisualSetup
    {
        private const string ModelPath = "Assets/ToonSoldiers_demo/models/ToonSoldier_demo.FBX";
        private const string MaterialPath = "Assets/_OperationOutbreak/Materials/Player/ToonSoldier_Player.mat";
        private const string ControllerPath = "Assets/_OperationOutbreak/Art/Animations/Player/ToonSoldier_Player.controller";

        private const string PlayerName = "Player";
        private const string SoldierVisualName = "ToonSoldierVisual";
        private const string SoldierInstanceName = "ToonSoldier_demo";
        private const string CarlVisualName = "CarlVisual";
        private const string PrototypeVisualName = "PrototypeVisual";

        [MenuItem("Tools/Operation Outbreak/Set Up Toon Soldier Player Visual")]
        public static void SetUpToonSoldierVisual()
        {
            // Milestone 1P.5 QA fix - rebuild the controller from REAL AnimationClip
            // sub-assets before it is applied. This guarantees the animator never runs
            // with unresolved motion references (the "Clip Count: 0" regression).
            if (!ToonSoldierAnimationSetup.RebuildController())
            {
                return;
            }

            GameObject player = GameObject.Find(PlayerName);
            if (player == null)
            {
                EditorUtility.DisplayDialog(
                    "Toon Soldier Setup",
                    "No GameObject named 'Player' found. Open Gameplay_Prototype first.",
                    "OK");
                return;
            }

            Transform soldierVisual = player.transform.Find(SoldierVisualName);
            if (soldierVisual == null)
            {
                var go = new GameObject(SoldierVisualName);
                Undo.RegisterCreatedObjectUndo(go, "Create ToonSoldierVisual");
                go.transform.SetParent(player.transform, false);
                soldierVisual = go.transform;
            }

            // Placement lives on the parent (ToonSoldierVisual), never on Biped bones or
            // the imported FBX. The committed scene carries the normalized transform; the
            // tool re-asserts it so the soldier stands on the gameplay ground plane.
            soldierVisual.localPosition = Vector3.zero;
            soldierVisual.localRotation = Quaternion.identity;
            soldierVisual.localScale = Vector3.one;

            // Reuse an existing instance; only instantiate when the visual is missing.
            // (Carl's tool re-instantiates every run, which is unnecessary for the
            // soldier because its prefab instance is committed in the scene.)
            Transform soldier = null;
            for (int i = 0; i < soldierVisual.childCount; i++)
            {
                Transform child = soldierVisual.GetChild(i);
                if (child.name == SoldierInstanceName && child.GetComponent<Animator>() != null)
                {
                    soldier = child;
                    break;
                }
            }

            if (soldier == null)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                if (model == null)
                {
                    EditorUtility.DisplayDialog(
                        "Toon Soldier Setup", "ToonSoldier_demo.FBX not found at:\n" + ModelPath, "OK");
                    return;
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, soldierVisual);
                Undo.RegisterCreatedObjectUndo(instance, "Instantiate Toon Soldier");
                instance.name = SoldierInstanceName;
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                soldier = instance.transform;
            }

            // Material: the package ships a built-in "Standard" material which is not URP
            // compatible, so the project URP/Lit material (same texture, same colours) is
            // applied here - identical to how the 1O.5 tool applies Carl_Player.mat.
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null)
            {
                foreach (var renderer in soldier.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = renderer.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        mats[i] = material;
                    }

                    renderer.sharedMaterials = mats;
                }
            }

            // Animator: presentation controller, imported humanoid avatar, root motion OFF.
            var animator = soldier.GetComponent<Animator>();
            if (animator == null)
            {
                animator = soldier.gameObject.AddComponent<Animator>();
            }

            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (controller != null)
            {
                animator.runtimeAnimatorController = controller;
            }

            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer != null && animator.avatar == null)
            {
                foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
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

            // Presentation swap (1P.5): soldier active, Carl kept as inactive fallback.
            soldierVisual.gameObject.SetActive(true);

            Transform carlVisual = player.transform.Find(CarlVisualName);
            if (carlVisual != null)
            {
                carlVisual.gameObject.SetActive(false);
            }

            // Prototype visual: retained as a disabled fallback, never deleted.
            Transform prototype = player.transform.Find(PrototypeVisualName);
            if (prototype != null)
            {
                foreach (var renderer in prototype.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = false;
                }
            }

            // Bridge wiring: ONE bridge, ONE authority. The soldier's Animator becomes
            // the target of the existing PlayerAnimationBridge (same contract as Carl).
            var bridge = player.GetComponent<PlayerAnimationBridge>();
            if (bridge == null)
            {
                bridge = Undo.AddComponent<PlayerAnimationBridge>(player);
            }

            var so = new SerializedObject(bridge);
            so.FindProperty("animator").objectReferenceValue = animator;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(player);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(player.scene);

            Debug.Log(
                $"[1P.5] Toon Soldier visual ready. Avatar valid: {(animator.avatar != null && animator.avatar.isValid)}, " +
                $"controller: {(controller != null ? controller.name : "MISSING")}, root motion: {animator.applyRootMotion}. " +
                "Save the scene to persist.", player);
        }
    }
}
#endif
