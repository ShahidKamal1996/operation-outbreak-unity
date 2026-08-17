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

        /// <summary>Presentation-only placement of the production visual child. X/Z stay 0;
        /// Y is computed by the tool from the actual mesh geometry (see
        /// TryComputeProductionGroundingOffsetY) so the feet sit on the lane.</summary>
        public static readonly Vector3 ProductionVisualPosition = new Vector3(0f, 0f, 0f);
        public static readonly Vector3 ProductionVisualRotationEuler = Vector3.zero;
        public static readonly Vector3 ProductionVisualScale = Vector3.one;

        /// <summary>
        /// The enemy gameplay root sits at world Y = 1 (the spawner/ground convention),
        /// with the lane surface at Y = 0. The production zombie's feet are near its own
        /// model origin, so the visual must be lowered by one unit minus the measured
        /// foot offset. This is the same convention Carl's setup tool establishes.
        /// </summary>
        public const float EnemyRootGroundHeight = 1f;

        /// <summary>QA fix #1B (Bug 3) - extra seconds added after the death clip length,
        /// so the animation visibly completes before the enemy deactivates.</summary>
        public const float DeathPresentationMarginSeconds = 0.3f;

        /// <summary>Fallback death presentation window used only when the death clip
        /// cannot be resolved from the project at setup time.</summary>
        public const float FallbackDeathPresentationDuration = 1.15f;

        /// <summary>
        /// Pure decision: the prototype visual is hidden exactly when the production
        /// visual is active. Kept static and side-effect free for EditMode tests.
        /// </summary>
        public static bool ShouldHidePrototypeVisual(bool productionVisualActive)
        {
            return productionVisualActive;
        }

        /// <summary>
        /// QA fix #1B (Bug 3) - deterministic death presentation window: the death
        /// clip's full length plus a small safe margin. The old constant (1.15 s) was
        /// shorter than the imported zombie death clip (~2.8-3.0 s), so the enemy was
        /// deactivated mid-animation. The margin is clamped to a safe minimum.
        /// </summary>
        public static float ComputeDeathPresentationDuration(float clipLengthSeconds, float marginSeconds)
        {
            float safeMargin = Mathf.Max(0.1f, marginSeconds);
            return Mathf.Max(0.05f, clipLengthSeconds) + safeMargin;
        }

        /// <summary>
        /// QA fix #1B (Bug 1) - computes the deterministic grounding offset for the
        /// production zombie: the lowest point of the instance's renderer bounds,
        /// expressed in the instance root's local space, is pushed down so the feet
        /// reach the lane surface (enemy root local Y = -EnemyRootGroundHeight).
        /// Returns false when the instance has no renderers.
        /// </summary>
        public static bool TryComputeProductionGroundingOffsetY(GameObject zombieInstance, out float offsetY)
        {
            offsetY = 0f;

            if (zombieInstance == null)
            {
                return false;
            }

            float lowestLocalY = float.MaxValue;
            bool found = false;

            foreach (Renderer renderer in zombieInstance.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                {
                    continue;
                }

                // Bounds are world-space AABBs; convert the min corner into the
                // instance root's local space so the offset is root-relative.
                float localY = zombieInstance.transform.InverseTransformPoint(renderer.bounds.min).y;

                if (localY < lowestLocalY)
                {
                    lowestLocalY = localY;
                    found = true;
                }
            }

            if (!found)
            {
                return false;
            }

            // Feet at enemy-root-local -1 (the ground plane under the y=1 root).
            offsetY = -EnemyRootGroundHeight - lowestLocalY;
            return true;
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

                // QA fix #1B (Bug 1) - deterministic grounding: the production visual
                // is lowered until the zombie's lowest mesh point reaches the lane
                // surface, derived from the ACTUAL instance geometry every run (never a
                // blind guess). X/Z stay 0.
                Vector3 productionVisualPosition = ProductionVisualPosition;
                if (TryComputeProductionGroundingOffsetY(zombie, out float groundingY))
                {
                    productionVisualPosition.y = groundingY;
                }
                productionVisual.localPosition = productionVisualPosition;

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
                // QA fix #1C - AnimationClip.averageSpeed is a Vector3 (average
                // root-motion velocity) in Unity, so the cadence reference is its
                // MAGNITUDE, never the vector compared with a float.
                AnimationClip walkClip = EnemyAnimationSetup.ResolveClip(EnemyAnimationSetup.WalkFbxPath);
                float walkReference = walkClip != null && walkClip.averageSpeed.magnitude > 0.01f
                    ? walkClip.averageSpeed.magnitude
                    : 1.3f;
                bridgeSo.FindProperty("walkReferenceSpeed").floatValue = walkReference;
                bridgeSo.ApplyModifiedPropertiesWithoutUndo();

                // QA fix #1B (Bug 3) - death presentation window derived from the ACTUAL
                // death clip length plus a safe margin, so the animation visibly
                // completes before deactivation. The imported zombie death clip is
                // ~2.8-3.0 s, far longer than the old 1.15 s constant that truncated it.
                ZombieController zombieController = contents.GetComponent<ZombieController>();
                if (zombieController != null)
                {
                    AnimationClip deathClip = EnemyAnimationSetup.ResolveClip(EnemyAnimationSetup.DeathFbxPath);
                    float deathPresentation = deathClip != null
                        ? ComputeDeathPresentationDuration(deathClip.length, DeathPresentationMarginSeconds)
                        : FallbackDeathPresentationDuration;

                    var zombieSo = new SerializedObject(zombieController);
                    SerializedProperty duration = zombieSo.FindProperty("deathPresentationDuration");
                    duration.floatValue = deathPresentation;
                    zombieSo.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(contents, ZombiePrefabPath);
                AnimationClip walkClipForLog = EnemyAnimationSetup.ResolveClip(EnemyAnimationSetup.WalkFbxPath);
                AnimationClip deathClipForLog = EnemyAnimationSetup.ResolveClip(EnemyAnimationSetup.DeathFbxPath);
                bool grounded = TryComputeProductionGroundingOffsetY(zombie, out float groundingForLog);
                Debug.Log(
                    "[1Q] Basic Infected production visual ready. Avatar valid: " +
                    $"{(animator.avatar != null && animator.avatar.isValid)}, controller: " +
                    $"{(controller != null ? controller.name : "MISSING")}, root motion: {animator.applyRootMotion}, " +
                    $"grounding Y: {(grounded ? groundingForLog.ToString("0.000") : "n/a")}, " +
                    $"death window: {(deathClipForLog != null ? (deathClipForLog.length + DeathPresentationMarginSeconds).ToString("0.00") : "n/a")} s, " +
                    $"walk cadence reference: {(walkClipForLog != null && walkClipForLog.averageSpeed.magnitude > 0.01f ? walkClipForLog.averageSpeed.magnitude.ToString("0.00") : "1.30 (fallback)")} u/s. " +
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
