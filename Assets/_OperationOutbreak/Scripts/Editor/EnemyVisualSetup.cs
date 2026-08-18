#if UNITY_EDITOR
using System.Collections.Generic;
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
    ///   7. QA fix #10/#11 - measures the near-final DEATH pose once (vertical
    ///      profile 0.95/0.99/0.999 logged for diagnosis; calibration from the
    ///      true near-end pose at t=0.999) and serializes the STABLE final grounded
    ///      death Y (EnemyAnimationBridge.deathGroundedVisualY) plus the small
    ///      downward contact margin (0.02) and the death-time grounding window
    ///      (0.25 -> 0.85 normalized). The runtime never resamples the death pose.
    ///   8. 1Q FINAL - configures the hybrid animation -> ragdoll death via
    ///      EnemyRagdollSetup (11 major humanoid bones, primitive colliders,
    ///      ConfigurableJoints; bodies authored kinematic, colliders authored
    ///      disabled; handoff 0.30 s / settle 0.6 s; the animation grounding
    ///      window is zeroed so corpse-Y correction never fights physics).
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

        /// <summary>
        /// QA fix #3 (Bug 2 - magenta on clean clones) - Operation Outbreak-owned URP
        /// materials for the production zombie. The vendor .mat files use the BUILT-IN
        /// Standard shader, which renders magenta under URP; the old PC had locally
        /// converted vendor materials that were never committed. These OO-owned
        /// materials are source-controlled (URP/Lit + vendor textures), so a clean
        /// clone renders identically without any manual conversion.
        /// </summary>
        public const string OoZombieMaterial01Path =
            "Assets/_OperationOutbreak/Art/Materials/Enemies/OO_Zombie_01.mat";

        public const string OoZombieMaterial02Path =
            "Assets/_OperationOutbreak/Art/Materials/Enemies/OO_Zombie_02.mat";

        public const string ProductionVisualName = "ProductionVisual";
        public const string PrototypeVisualName = "Visual";
        public const string ZombieInstanceName = "StylizedZombie_01";

        /// <summary>Presentation-only placement of the production visual child. X/Z stay 0;
        /// Y uses <see cref="ProductionVisualGroundingOffsetY"/> - a DETERMINISTIC,
        /// FBX-derived value, never a runtime bounds measurement (see QA fix #2).</summary>
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

        /// <summary>
        /// QA fix #2 (Bug: still floating) - DETERMINISTIC grounding offset.
        ///
        /// WHY NOT MEASURE RENDERER BOUNDS: QA fix #1B derived the offset from the
        /// vendor prefab's renderer bounds inside prefab-contents, where the mesh is in
        /// its EDITOR/REFERENCE pose (the vendor ships a crouched cartoon pose). At
        /// runtime the Animator drives the Mixamo idle stance, whose foot height differs
        /// from that editor pose - so the measured offset (-0.628 in the QA run) left
        /// the animated feet floating.
        ///
        /// THE STABLE SOURCE OF TRUTH: the vendor FBX itself. Its lowest mesh vertex
        /// sits at +0.536 cm ABOVE the model root (parsed from StylizedZombie.fbx;
        /// Unity imports cm->m with useFileUnits), and retargeted Mixamo idle feet sit
        /// at the humanoid reference height - effectively the model root. With the
        /// enemy root at y=1 and the lane at y=0, the visual must therefore sit at
        /// y = -(1 + 0.00536) = -1.005. This value is static, pose-independent and
        /// applied unconditionally by the tool on every run.
        /// </summary>
        public const float ProductionVisualGroundingOffsetY = -1.005f;

        /// <summary>QA fix #1B (Bug 3) - extra seconds added after the death clip length,
        /// so the animation visibly completes before the enemy deactivates.</summary>
        public const float DeathPresentationMarginSeconds = 0.3f;

        /// <summary>Fallback death presentation window used only when the death clip
        /// cannot be resolved from the project at setup time.</summary>
        public const float FallbackDeathPresentationDuration = 1.15f;

        /// <summary>
        /// QA fix #10 - death-time-driven grounding window. The ProductionVisual
        /// blends from the standing Y (-1.005) to the stable final grounded death Y
        /// between these Death clip normalized times: it starts once the body is
        /// clearly falling (0.25) and finishes before the final lying pose (0.85),
        /// so the corpse ALREADY rests on the road when the animation ends. The
        /// clip-finish gate sits at 0.999 - the grounding therefore always
        /// completes inside the animation, never after it.
        /// </summary>
        public const float DeathGroundingStartNormalizedTime = 0.25f;
        public const float DeathGroundingEndNormalizedTime = 0.85f;

        /// <summary>
        /// QA fix #10/#11 - the Death clip normalized time at which the near-final
        /// corpse pose is sampled at setup time, to derive the stable serialized
        /// final grounded death Y.
        ///
        /// QA fix #11 - the QA fix #10 value (0.95) was slightly TOO EARLY: manual
        /// QA showed the corpse resting a little above the road, i.e. the clip
        /// keeps changing vertically after 0.95 and the sampled pose was not yet
        /// the true final resting pose. The sample now sits at 1.0 minus a tiny
        /// epsilon (0.001) - the last evaluable instant of the clip, which IS the
        /// true near-end resting pose. The setup log prints the vertical profile
        /// (0.95 / 0.99 / 0.999) so the tail movement is directly visible.
        /// </summary>
        public const float DeathPoseMeasurementNormalizedTime = 0.999f;

        /// <summary>
        /// QA fix #11 - vertical-profile sample times logged for diagnosis. They
        /// must be strictly increasing and end on the calibration sample
        /// (DeathPoseMeasurementNormalizedTime); the first entry keeps the old
        /// QA fix #10 sample so the tail's vertical movement shows up in the log.
        /// </summary>
        public static readonly float[] DeathPoseProfileNormalizedTimes =
        {
            0.95f,
            0.99f,
            DeathPoseMeasurementNormalizedTime,
        };

        /// <summary>
        /// QA fix #11 - small DOWNWARD contact margin subtracted from the measured
        /// final grounded Y (0.02 = 2 cm on a 2 m zombie): the corpse prefers a
        /// very slight contact/intersection with the road over visible hovering.
        /// Written onto EnemyAnimationBridge.deathGroundingContactMargin every run
        /// (clamped to [0, 0.05] at runtime) and tunable per-prefab in the
        /// Inspector without re-running the tool. Do NOT make it large - the body
        /// must never sink deeply.
        /// </summary>
        public const float DeathGroundingContactMarginY = 0.02f;

        /// <summary>QA fix #10 - documented fallback constant used when the death-pose
        /// measurement is unavailable (shared with EnemyAnimationBridge).</summary>
        public const float FallbackDeathGroundedVisualY =
            EnemyAnimationBridge.FallbackDeathGroundedVisualY;

        /// <summary>
        /// Pure decision: the prototype visual is hidden exactly when the production
        /// visual is active. Kept static and side-effect free for EditMode tests.
        /// </summary>
        public static bool ShouldHidePrototypeVisual(bool productionVisualActive)
        {
            return productionVisualActive;
        }

        /// <summary>
        /// QA fix #3 (Bug 2) - selects the Operation Outbreak-owned URP material for a
        /// renderer based on the renderer's CURRENT vendor material name: anything
        /// containing "02" maps to OO_Zombie_02 (the second vendor material/variant),
        /// everything else (including an unresolvable/unknown name) falls back to
        /// OO_Zombie_01. Deterministic and LOD-safe: every renderer of the production
        /// instance is assigned by this rule on every setup run.
        /// </summary>
        public static string SelectProductionMaterialForRenderer(string currentMaterialName)
        {
            if (!string.IsNullOrEmpty(currentMaterialName) && currentMaterialName.Contains("02"))
            {
                return OoZombieMaterial02Path;
            }

            return OoZombieMaterial01Path;
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
        /// QA fix #10/#11 - measures the near-final DEATH pose at setup time and
        /// returns the STABLE final grounded local Y for the ProductionVisual:
        /// the value that places the corpse's lowest vertex exactly on the lane
        /// surface, computed with the same world-space-delta formula the runtime
        /// used to derive at run time. The result is serialized onto
        /// EnemyAnimationBridge.deathGroundedVisualY; the runtime NEVER resamples.
        ///
        /// QA fix #11 - the pose is sampled across a small VERTICAL PROFILE
        /// (0.95 / 0.99 / 0.999) so the clip's tail movement is logged and the
        /// diagnosis is directly visible in the console. The CALIBRATION value is
        /// taken from the LAST sample (DeathPoseMeasurementNormalizedTime = 0.999,
        /// i.e. 1.0 minus a tiny epsilon) - the true near-end resting pose. The
        /// QA fix #10 sample (0.95) was slightly too early, which is exactly why
        /// the corpse hovered a little above the road.
        ///
        /// Every transform under the animator is recorded first and restored in
        /// the finally block, so the prefab is always saved in its standing pose.
        ///
        /// Returns the documented fallback constant when anything is unavailable
        /// (renderer, mesh, animator/avatar, clip, sampling failure) and reports
        /// whether a real measurement was produced.
        /// </summary>
        private static float MeasureFinalDeathGroundedVisualY(
            Transform productionVisual, Transform enemyRoot, AnimationClip deathClip, out bool measured)
        {
            measured = false;

            if (productionVisual == null || enemyRoot == null || deathClip == null)
            {
                return FallbackDeathGroundedVisualY;
            }

            SkinnedMeshRenderer renderer = null;
            foreach (SkinnedMeshRenderer candidate in
                     productionVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (candidate.sharedMesh != null && candidate.sharedMesh.vertexCount > 0)
                {
                    renderer = candidate;
                    break;
                }
            }

            Animator animator = productionVisual.GetComponentInChildren<Animator>(true);

            if (renderer == null || animator == null || animator.avatar == null || !animator.avatar.isValid)
            {
                return FallbackDeathGroundedVisualY;
            }

            // Record every transform under the animator root so the sampled death
            // poses can be fully reverted before the prefab is saved - the prefab
            // must keep its authored standing pose.
            var recorded = new List<RecordedTransform>();
            foreach (Transform child in animator.transform.GetComponentsInChildren<Transform>(true))
            {
                recorded.Add(new RecordedTransform(
                    child, child.localPosition, child.localRotation, child.localScale));
            }

            try
            {
                // QA fix #11 - vertical profile: sample each pose in turn, bake the
                // skinned mesh and record the lowest visible world Y. The log line
                // shows exactly how much the corpse still moves vertically through
                // the tail of the clip (the QA fix #11 diagnosis).
                var profile = new string[DeathPoseProfileNormalizedTimes.Length];
                float lowestAtCalibration = float.MaxValue;

                for (int i = 0; i < DeathPoseProfileNormalizedTimes.Length; i++)
                {
                    float normalizedTime = DeathPoseProfileNormalizedTimes[i];
                    AnimationMode.SampleAnimationClip(
                        animator.gameObject, deathClip, deathClip.length * normalizedTime);

                    if (!TryMeasureLowestCorpseWorldY(renderer, out float lowestWorldY))
                    {
                        Debug.LogWarning(
                            "[1Q QA fix #10/#11] Death pose produced no measurable vertices at " +
                            $"normalized {normalizedTime:0.000} - using the documented fallback " +
                            "final grounded Y " + FallbackDeathGroundedVisualY.ToString("0.000") + ".",
                            enemyRoot);
                        return FallbackDeathGroundedVisualY;
                    }

                    profile[i] = $"t={normalizedTime:0.000}:{lowestWorldY:0.000}";

                    if (i == DeathPoseProfileNormalizedTimes.Length - 1)
                    {
                        lowestAtCalibration = lowestWorldY;
                    }
                }

                Debug.Log(
                    "[1Q QA fix #11] Death pose vertical profile (lowest corpse world Y at each " +
                    $"sample; calibration = LAST): {string.Join(", ", profile)}. " +
                    "If the values keep changing through the tail, the earlier sample was not " +
                    "the true resting pose - exactly the QA fix #10->#11 calibration drift.",
                    enemyRoot);

                // World-space delta (identical to the QA fix #7 runtime formula):
                // targetLocalY = currentVisualLocalY + (groundWorldY - lowestCorpseWorldY).
                float groundWorldY = enemyRoot.position.y - EnemyRootGroundHeight;
                float measuredGroundedY = EnemyAnimationBridge.ComputeDeathGroundedTargetLocalY(
                    productionVisual.localPosition.y, lowestAtCalibration, groundWorldY);

                measured = true;
                Debug.Log(
                    "[1Q QA fix #10/#11] Final death grounded Y calibrated at normalized " +
                    DeathPoseMeasurementNormalizedTime.ToString("0.000") +
                    $": lowestCorpseWorldY={lowestAtCalibration:0.000}, groundWorldY={groundWorldY:0.000}, " +
                    $"standingVisualY={productionVisual.localPosition.y:0.000}, " +
                    $"measuredFinalDeathGroundedY={measuredGroundedY:0.000}, " +
                    $"contact margin={DeathGroundingContactMarginY:0.000} -> effective " +
                    $"{EnemyAnimationBridge.ApplyDeathGroundingContactMargin(measuredGroundedY, DeathGroundingContactMarginY):0.000} " +
                    "(stable - serialized onto the bridge).",
                    enemyRoot);
                return measuredGroundedY;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning(
                    "[1Q QA fix #10/#11] Death pose measurement failed (" + exception.Message +
                    ") - using the documented fallback final grounded Y " +
                    FallbackDeathGroundedVisualY.ToString("0.000") + ".", enemyRoot);
                return FallbackDeathGroundedVisualY;
            }
            finally
            {
                // Restore the standing pose unconditionally - sampling must never
                // leak into the saved prefab.
                foreach (RecordedTransform entry in recorded)
                {
                    if (entry.Target != null)
                    {
                        entry.Target.localPosition = entry.LocalPosition;
                        entry.Target.localRotation = entry.LocalRotation;
                        entry.Target.localScale = entry.LocalScale;
                    }
                }
            }
        }

        /// <summary>
        /// QA fix #10/#11 - bakes the production skinned mesh (in its currently
        /// sampled pose) and returns the lowest vertex WORLD Y. Measuring in world
        /// space keeps the identical QA fix #7 convention: the baked vertices are
        /// transformed through the renderer transform into world space.
        /// </summary>
        private static bool TryMeasureLowestCorpseWorldY(
            SkinnedMeshRenderer renderer, out float lowestWorldY)
        {
            lowestWorldY = 0f;

            if (renderer == null || renderer.sharedMesh == null || renderer.sharedMesh.vertexCount == 0)
            {
                return false;
            }

            Mesh baked = new Mesh();
            renderer.BakeMesh(baked);
            Vector3[] vertices = baked.vertices;

            float minimum = float.MaxValue;
            for (int i = 0; i < vertices.Length; i++)
            {
                float worldY = renderer.transform.TransformPoint(vertices[i]).y;
                if (worldY < minimum)
                {
                    minimum = worldY;
                }
            }

            Object.DestroyImmediate(baked);

            if (vertices.Length == 0 || minimum >= float.MaxValue * 0.5f)
            {
                return false;
            }

            lowestWorldY = minimum;
            return true;
        }

        private readonly struct RecordedTransform
        {
            public readonly Transform Target;
            public readonly Vector3 LocalPosition;
            public readonly Quaternion LocalRotation;
            public readonly Vector3 LocalScale;

            public RecordedTransform(
                Transform target, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
            {
                Target = target;
                LocalPosition = localPosition;
                LocalRotation = localRotation;
                LocalScale = localScale;
            }
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

                // QA fix #2 (Bug: still floating) - DETERMINISTIC grounding: the
                // production visual is lowered by the static FBX-derived offset
                // (see ProductionVisualGroundingOffsetY). The pre-#2 bounds measurement
                // read the vendor's EDITOR pose, not the animated runtime stance, so it
                // produced a wrong value. X/Z stay 0.
                Vector3 productionVisualPosition = ProductionVisualPosition;
                productionVisualPosition.y = ProductionVisualGroundingOffsetY;
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

                // QA fix #3 (Bug 2) - assign the Operation Outbreak-owned URP materials
                // to EVERY renderer of the production instance (LOD0 and LOD1 alike),
                // selected deterministically from the vendor material names. The vendor
                // materials use the built-in Standard shader and render magenta under
                // URP, so relying on uncommitted local conversions is forbidden - the
                // OO materials are source-controlled and this step runs on every setup.
                Material material01 = AssetDatabase.LoadAssetAtPath<Material>(OoZombieMaterial01Path);
                Material material02 = AssetDatabase.LoadAssetAtPath<Material>(OoZombieMaterial02Path);

                if (material01 == null || material02 == null)
                {
                    EditorUtility.DisplayDialog(
                        "Basic Infected Visual",
                        "One or more OO zombie URP materials are missing:\n" +
                        OoZombieMaterial01Path + "\n" + OoZombieMaterial02Path +
                        "\nThe production zombie would render magenta (vendor built-in shader). " +
                        "Restore the materials and re-run.",
                        "OK");
                    return;
                }

                int assignedRenderers = 0;
                foreach (Renderer renderer in zombie.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] sharedMaterials = renderer.sharedMaterials;
                    bool changed = false;

                    for (int i = 0; i < sharedMaterials.Length; i++)
                    {
                        string currentName = sharedMaterials[i] != null ? sharedMaterials[i].name : string.Empty;
                        string selectedPath = SelectProductionMaterialForRenderer(currentName);
                        Material selected = selectedPath == OoZombieMaterial02Path ? material02 : material01;

                        if (sharedMaterials[i] != selected)
                        {
                            sharedMaterials[i] = selected;
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        renderer.sharedMaterials = sharedMaterials;
                    }

                    assignedRenderers++;
                }

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

                // QA fix #10 - death-time-driven grounding: measure the near-final
                // death pose ONCE here and serialize the STABLE final grounded death Y
                // plus the grounding window onto the bridge. The runtime blends
                // standingY -> finalDeathGroundedY as a smoothstep of the Death clip's
                // normalized time between start (0.25) and end (0.85) - the corpse
                // reaches the road DURING the fall and never sinks after it.
                // QA fix #11 - the calibration sample moved to the true near-end pose
                // (t=0.999; the 0.95 sample was slightly too early, leaving the corpse
                // a little above the road) and a small downward contact margin (0.02)
                // is serialized alongside, so the final pose very slightly contacts
                // the road instead of hovering.
                AnimationClip deathClipForGrounding = EnemyAnimationSetup.ResolveClip(EnemyAnimationSetup.DeathFbxPath);
                float finalDeathGroundedY = MeasureFinalDeathGroundedVisualY(
                    productionVisual, contents.transform, deathClipForGrounding, out bool deathGroundingMeasured);

                if (!deathGroundingMeasured)
                {
                    Debug.LogWarning(
                        "[1Q QA fix #10] Final death pose could not be measured - " +
                        "deathGroundedVisualY uses the DOCUMENTED FALLBACK " +
                        finalDeathGroundedY.ToString("0.000") + ". Tune it manually on the bridge if needed.",
                        contents);
                }

                bridgeSo.FindProperty("deathGroundedVisualY").floatValue = finalDeathGroundedY;
                bridgeSo.FindProperty("deathGroundingStartNormalizedTime").floatValue =
                    DeathGroundingStartNormalizedTime;
                bridgeSo.FindProperty("deathGroundingEndNormalizedTime").floatValue =
                    DeathGroundingEndNormalizedTime;

                // QA fix #11 - small downward contact margin: the corpse must prefer
                // a very slight contact with the road over visible hovering. Written
                // every run (deterministic) and tunable per-prefab afterwards.
                bridgeSo.FindProperty("deathGroundingContactMargin").floatValue =
                    DeathGroundingContactMarginY;
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

                // 1Q FINAL - hybrid animation -> ragdoll death: deterministic
                // ragdoll authoring on the production skeleton (11 major humanoid
                // bones, kinematic while alive, colliders disabled) + bridge wiring
                // (handoff/settle timings + animation-grounding bypass). Validation-
                // first: if any bone is missing the ragdoll is skipped and the
                // animation-only death path keeps working.
                bool ragdollReady = EnemyRagdollSetup.ConfigureRagdollOnContents(contents);

                PrefabUtility.SaveAsPrefabAsset(contents, ZombiePrefabPath);
                AnimationClip walkClipForLog = EnemyAnimationSetup.ResolveClip(EnemyAnimationSetup.WalkFbxPath);
                AnimationClip deathClipForLog = EnemyAnimationSetup.ResolveClip(EnemyAnimationSetup.DeathFbxPath);

                // QA fix #3 - validate the death presentation before finishing setup.
                List<string> controllerProblems = EnemyAnimationSetup.CollectValidationProblems();
                bool deathResolves = deathClipForLog != null && controllerProblems.Count == 0;

                if (!deathResolves)
                {
                    Debug.LogWarning(
                        "[1Q] Basic Infected death presentation may not resolve: " +
                        (deathClipForLog == null ? "death clip missing; " : string.Empty) +
                        string.Join("; ", controllerProblems), contents);
                }

                Debug.Log(
                    "[1Q] Basic Infected production visual ready. Avatar valid: " +
                    $"{(animator.avatar != null && animator.avatar.isValid)}, controller: " +
                    $"{(controller != null ? controller.name : "MISSING")}, root motion: {animator.applyRootMotion}, " +
                    $"grounding Y: {ProductionVisualGroundingOffsetY:0.000} (deterministic FBX-derived), " +
                    $"final death grounded Y: {finalDeathGroundedY:0.000} " +
                    $"({(deathGroundingMeasured ? "measured at t=" + DeathPoseMeasurementNormalizedTime.ToString("0.000") + " (true near-end pose)" : "documented fallback")}) " +
                    $"- contact margin {DeathGroundingContactMarginY:0.000}, " +
                    $"death grounding window: {DeathGroundingStartNormalizedTime:0.00} -> {DeathGroundingEndNormalizedTime:0.00} normalized, " +
                    $"death window: {(deathClipForLog != null ? (deathClipForLog.length + DeathPresentationMarginSeconds).ToString("0.00") : "n/a")} s, " +
                    $"death state resolves: {deathResolves}, " +
                    $"hybrid ragdoll death: {(ragdollReady ? "configured (animation lead-in + physics fall)" : "NOT configured (animation-only fallback)")}, " +
                    $"materials assigned: {assignedRenderers} renderers -> OO_Zombie URP materials, " +
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
