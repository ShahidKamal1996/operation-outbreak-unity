#if UNITY_EDITOR
using OperationOutbreak.Enemies;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OperationOutbreak.EditorTools
{
    /// <summary>
    /// Milestone 1Q - one-click, idempotent setup that swaps the Basic Infected's
    /// prototype visual for the production Stylized Zombie, WITHOUT touching enemy
    /// gameplay. It edits the Zombie_Prototype.prefab ASSET (the template every
    /// spawner instance comes from), mirroring the 1O.5/1P.5 visual setup workflow:
    /// FBX-instantiation edge cases stay in an editor tool instead of hand-authored
    /// scene/prefab YAML.
    ///
    /// WHAT THE TOOL DOES (idempotent - running it twice leaves the same state):
    ///   1. Rebuilds the OO_BasicInfected.controller from the real Mixamo clips
    ///      (see EnemyAnimationSetup).
    ///   2. Creates ProductionVisual under the enemy root and instantiates
    ///      StylizedZombie_01 beneath it (replacing any previous production instance).
    ///   3. Assigns the controller and the imported StylizedZombieAvatar; enforces
    ///      Apply Root Motion OFF and AlwaysAnimate so gameplay remains the only
    ///      movement authority.
    ///   4. Hides the prototype Visual child's renderers (never deleted - the
    ///      prototype stays as the safe fallback).
    ///   5. Wires EnemyAnimationBridge on the enemy root (gameplay -> animator).
    ///   6. Raises the enemy's deathPresentationDuration so the death clip plays
    ///      before deactivation (default 0.38 = prototype behavior when the tool is
    ///      never run).
    ///
    /// FALLBACK: if the production prefab cannot be resolved the tool aborts with a
    /// dialog and modifies nothing - the prototype visual keeps working exactly as
    /// before, so gameplay/debugging never breaks.
    ///
    /// USAGE: Tools > Operation Outbreak > Set Up Basic Infected Production Visual,
    /// then save and commit the modified Zombie_Prototype.prefab.
    /// </summary>
    public static class EnemyVisualSetup
    {
        public const string ZombiePrefabPath =
            "Assets/_OperationOutbreak/Prefabs/Enemies/Zombie_Prototype.prefab";

        public const string ProductionPrefabPath =
            "Assets/ArtStore3D/Stylized Zombie/Prefab/StylizedZombie_01.prefab";

        public const string ZombieFbxPath =
            "Assets/ArtStore3D/Stylized Zombie/Model/StylizedZombie.fbx";

        public const string ProductionVisualName = "ProductionVisual";
        public const string PrototypeVisualName = "Visual";
        public const string ZombieInstanceName = "StylizedZombie_01";

        /// <summary>Presentation-only placement of the production visual child. Tune here
        /// (never in gameplay) if QA finds the zombie floating/sinking or mis-rotated.</summary>
        public static readonly Vector3 ProductionVisualPosition = Vector3.zero;
        public static readonly Vector3 ProductionVisualRotationEuler = Vector3.zero;
        public static readonly Vector3 ProductionVisualScale = Vector3.one;

        /// <summary>Seconds the defeated production zombie stays visible so the death
        /// animation can play. Written onto the prefab's ZombieController.</summary>
        public const float ProductionDeathPresentationDuration = 1.15f;

        /// <summary>
        /// Pure decision: the prototype visual is hidden exactly when the production
        /// visual is active. Kept static and side-effect free for EditMode tests.
        /// </summary>
        public static bool ShouldHidePrototypeVisual(bool productionVisualActive)
        {
            return productionVisualActive;
        }

        [MenuItem("Tools/Operation Outbreak/Set Up Basic Infected Production Visual")]
        public static void SetUpBasicInfectedVisual()
        {
            GameObject productionPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(ProductionPrefabPath);

            if (productionPrefab == null)
            {
                EditorUtility.DisplayDialog(
                    "Basic Infected Visual",
                    "Production prefab not found at:\n" + ProductionPrefabPath +
                    "\nThe prototype enemy visual remains in use (safe fallback).",
                    "OK");
                return;
            }

            // The controller must exist before it can be assigned.
            if (!EnemyAnimationSetup.RebuildController())
            {
                return;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(ZombiePrefabPath);

            try
            {
                Transform productionVisual = contents.transform.Find(ProductionVisualName);
                if (productionVisual == null)
                {
                    var holder = new GameObject(ProductionVisualName);
                    holder.transform.SetParent(contents.transform, false);
                    productionVisual = holder.transform;
                }

                productionVisual.localPosition = ProductionVisualPosition;
                productionVisual.localRotation = Quaternion.Euler(ProductionVisualRotationEuler);
                productionVisual.localScale = ProductionVisualScale;

                // Exactly one production instance: remove any previous one first.
                for (int i = productionVisual.childCount - 1; i >= 0; i--)
                {
                    Object.DestroyImmediate(productionVisual.GetChild(i).gameObject);
                }

                GameObject zombie = (GameObject)PrefabUtility.InstantiatePrefab(productionPrefab, productionVisual);
                zombie.name = ZombieInstanceName;
                zombie.transform.localPosition = Vector3.zero;
                zombie.transform.localRotation = Quaternion.identity;
                zombie.transform.localScale = Vector3.one;

                // Animator: production controller, imported humanoid avatar, root motion OFF.
                Animator animator = zombie.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    animator = zombie.AddComponent<Animator>();
                }

                AnimatorController controller =
                    AssetDatabase.LoadAssetAtPath<AnimatorController>(EnemyAnimationSetup.ControllerPath);
                if (controller != null)
                {
                    animator.runtimeAnimatorController = controller;
                }

                if (animator.avatar == null || !animator.avatar.isValid)
                {
                    foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(ZombieFbxPath))
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

                // Prototype visual: hidden (not deleted) while the production visual is
                // active, preserving the safe fallback for debugging/QA.
                Transform prototypeVisual = contents.transform.Find(PrototypeVisualName);
                if (prototypeVisual != null && ShouldHidePrototypeVisual(productionVisual.gameObject.activeSelf))
                {
                    foreach (Renderer renderer in prototypeVisual.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.enabled = false;
                    }
                }

                // Bridge wiring: one bridge, one authority - gameplay state -> animator.
                EnemyAnimationBridge bridge = contents.GetComponent<EnemyAnimationBridge>();
                if (bridge == null)
                {
                    bridge = contents.AddComponent<EnemyAnimationBridge>();
                }

                var bridgeSo = new SerializedObject(bridge);
                bridgeSo.FindProperty("zombie").objectReferenceValue =
                    contents.GetComponent<ZombieController>();
                bridgeSo.FindProperty("animator").objectReferenceValue = animator;

                // Milestone 1Q Bug 4 - cadence reference: derive the speed at which the
                // walk clip's feet match world translation from the clip's own average
                // speed, so the bridge's playback multiplier synchronizes the Walk
                // animation with the code-driven movement. Fall back to 1.3 when the
                // clip reports no measurable average speed.
                AnimationClip walkClip = EnemyAnimationSetup.ResolveClip(EnemyAnimationSetup.WalkFbxPath);
                float walkReference = walkClip != null && walkClip.averageSpeed > 0.01f
                    ? walkClip.averageSpeed
                    : 1.3f;
                bridgeSo.FindProperty("walkReferenceSpeed").floatValue = walkReference;
                bridgeSo.ApplyModifiedPropertiesWithoutUndo();

                // Death presentation window for the production death clip.
                ZombieController zombieController = contents.GetComponent<ZombieController>();
                if (zombieController != null)
                {
                    var zombieSo = new SerializedObject(zombieController);
                    SerializedProperty duration = zombieSo.FindProperty("deathPresentationDuration");
                    duration.floatValue = ProductionDeathPresentationDuration;
                    zombieSo.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(contents, ZombiePrefabPath);
                AnimationClip walkClipForLog = EnemyAnimationSetup.ResolveClip(EnemyAnimationSetup.WalkFbxPath);
                Debug.Log(
                    "[1Q] Basic Infected production visual ready. Avatar valid: " +
                    $"{(animator.avatar != null && animator.avatar.isValid)}, controller: " +
                    $"{(controller != null ? controller.name : "MISSING")}, root motion: {animator.applyRootMotion}, " +
                    $"walk cadence reference: {(walkClipForLog != null && walkClipForLog.averageSpeed > 0.01f ? walkClipForLog.averageSpeed.ToString("0.00") : "1.30 (fallback)")} u/s. " +
                    "Commit the modified Zombie_Prototype.prefab.", contents);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
    }
}
#endif
