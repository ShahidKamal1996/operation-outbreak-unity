#if UNITY_EDITOR
using System.Collections.Generic;
using OperationOutbreak.Enemies;
using UnityEditor;
using UnityEngine;

namespace OperationOutbreak.EditorTools
{
    /// <summary>
    /// Milestone 1Q - hybrid ragdoll QA fix #1 - STABILIZED ragdoll authoring.
    ///
    /// The FINAL authoring was physically unstable: every capsule blindly used
    /// the bone's local Y axis, radii were aggressive, connected and nearby
    /// colliders overlapped deeply at handoff, every joint had the same
    /// symmetric ±90-120 degrees of freedom on all axes, and the activation
    /// inherited the Animator's residual velocities. Result: limbs
    /// twisting/kicking/flipping ("random dance").
    ///
    /// WHAT CHANGED (see the policy statics below for the pinned rules):
    ///   1. COLLIDER ALIGNMENT: each capsule now lives on a per-bone child
    ///      GameObject ("RagdollCollider") rotated so the capsule's Y axis
    ///      follows the ACTUAL vector from the bone to its anatomical child
    ///      (never a fixed local-Y assumption). The head keeps a sphere.
    ///   2. COLLIDER SIZES: per-bone-group conservative radius table
    ///      (0.05-0.17 m), capsule height from the measured bone length -
    ///      adjacent connected colliders taper smoothly instead of mushrooming.
    ///   3. SELF-COLLISION: all ragdoll colliders go on the dedicated
    ///      "OO_Ragdoll" layer (TagManager layer 8); EnemyRagdoll disables
    ///      layer-vs-itself collision at runtime (Physics.IgnoreLayerCollision),
    ///      so corpse parts interact ONLY with the environment/road - never
    ///      with each other or other corpses. Joints additionally keep
    ///      enableCollision = false (defense in depth).
    ///   4. JOINTS: axes are computed from the REAL bone chain (twist axis =
    ///      bone direction; hinge axis = cross(parent, child) with degenerate
    ///      fallbacks) and stored child-local. Limits are ANATOMICAL per group:
    ///      elbows/knees are hinge-like (large bend, tiny twist/lateral),
    ///      shoulders/hips wide but controlled, spine modest, head controlled.
    ///   5. PHYSICS: rebalanced hips-heavy masses with connected-pair ratios
    ///      kept sane, maxAngularVelocity capped (7 rad/s - no spin-kicks),
    ///      modest angular drag (0.4 - damps flailing), discrete detection,
    ///      no interpolation - mobile-friendly.
    ///   6. VALIDATION: after generation the tool reports every bone's
    ///      collider (shape/radius/height/layer), connected-pair overlap
    ///      ratios (PROBLEMATIC flags) and connected mass ratios. Also
    ///      available via Tools > Operation Outbreak > Debug Basic Infected
    ///      Ragdoll without modifying the prefab.
    ///
    /// USAGE: Tools > Operation Outbreak > Set Up Basic Infected Ragdoll (or
    /// re-run Set Up Basic Infected Production Visual, which calls this tool).
    /// Commit the modified Zombie_Prototype.prefab.
    /// </summary>
    public static class EnemyRagdollSetup
    {
        // Humanoid bone names (Unity convention, shared with the tests so the
        // mapping can never drift).
        public const string BoneHips = "Hips";
        public const string BoneSpine = "Spine";
        public const string BoneHead = "Head";
        public const string BoneLeftUpperArm = "LeftUpperArm";
        public const string BoneRightUpperArm = "RightUpperArm";
        public const string BoneLeftLowerArm = "LeftLowerArm";
        public const string BoneRightLowerArm = "RightLowerArm";
        public const string BoneLeftUpperLeg = "LeftUpperLeg";
        public const string BoneRightUpperLeg = "RightUpperLeg";
        public const string BoneLeftLowerLeg = "LeftLowerLeg";
        public const string BoneRightLowerLeg = "RightLowerLeg";

        /// <summary>The deterministic child holder name under each bone that
        /// carries the aligned primitive collider.</summary>
        public const string RagdollColliderChildName = "RagdollCollider";

        /// <summary>QA fix #1 - every ragdoll collider lives on this layer. The
        /// layer is defined in ProjectSettings/TagManager.asset (layer 8).</summary>
        public const string RagdollLayerName = EnemyRagdoll.RagdollLayerName;

        private readonly struct BoneDefinition
        {
            public readonly string Name;
            public readonly HumanBodyBones Muscle;
            public readonly float Mass;

            public BoneDefinition(string name, HumanBodyBones muscle, float mass)
            {
                Name = name;
                Muscle = muscle;
                Mass = mass;
            }
        }

        /// <summary>
        /// The 11 major ragdoll bones in PARENT-BEFORE-CHILD order (Hips first).
        /// The order is a CONTRACT: EnemyRagdoll.RestoreForReuse restores authored
        /// poses by walking this array, and parents must be restored before their
        /// children. Masses are REBALANCED for stability (QA fix #1): hips remain
        /// heaviest but connected-pair mass ratios stay <= 2.4 - large ratios made
        /// the joint solver fight itself at handoff.
        /// </summary>
        private static readonly BoneDefinition[] BoneDefinitions =
        {
            new BoneDefinition(BoneHips, HumanBodyBones.Hips, 1.8f),
            new BoneDefinition(BoneSpine, HumanBodyBones.Spine, 1.2f),
            new BoneDefinition(BoneHead, HumanBodyBones.Head, 0.8f),
            new BoneDefinition(BoneLeftUpperArm, HumanBodyBones.LeftUpperArm, 0.5f),
            new BoneDefinition(BoneRightUpperArm, HumanBodyBones.RightUpperArm, 0.5f),
            new BoneDefinition(BoneLeftLowerArm, HumanBodyBones.LeftLowerArm, 0.35f),
            new BoneDefinition(BoneRightLowerArm, HumanBodyBones.RightLowerArm, 0.35f),
            new BoneDefinition(BoneLeftUpperLeg, HumanBodyBones.LeftUpperLeg, 0.9f),
            new BoneDefinition(BoneRightUpperLeg, HumanBodyBones.RightUpperLeg, 0.9f),
            new BoneDefinition(BoneLeftLowerLeg, HumanBodyBones.LeftLowerLeg, 0.6f),
            new BoneDefinition(BoneRightLowerLeg, HumanBodyBones.RightLowerLeg, 0.6f),
        };

        public static string[] RequiredBoneNames
        {
            get
            {
                var names = new string[BoneDefinitions.Length];
                for (int i = 0; i < BoneDefinitions.Length; i++)
                {
                    names[i] = BoneDefinitions[i].Name;
                }

                return names;
            }
        }

        /// <summary>1Q FINAL - animation lead-in before the ragdoll handoff.
        /// Chosen inside the 0.25-0.40 s band: at 0.30 s the death clip's body is
        /// already starting its fall, so physics completes it naturally.</summary>
        public const float DefaultHandoffSeconds = 0.3f;

        /// <summary>1Q FINAL - physics settle window before the corpse despawns.</summary>
        public const float DefaultSettleSeconds = 0.6f;

        /// <summary>1Q FINAL - the animation-path grounding window is zeroed when the
        /// ragdoll is configured: the corpse-Y correction must never fight physics.</summary>
        public const float GroundingBypassStartNormalizedTime = 0f;
        public const float GroundingBypassEndNormalizedTime = 0f;

        // ------------------------------------------------------------------ physics tuning

        /// <summary>QA fix #1 - caps the body's angular velocity so no impulse can
        /// spin a limb into a "kick". 7 rad/s (~400 deg/s) still allows a natural
        /// collapse rotation.</summary>
        public const float MaxAngularVelocity = 7f;

        /// <summary>QA fix #1 - modest angular drag damps residual flailing after
        /// the initial contacts without making the corpse floaty. Linear drag stays
        /// 0 so the fall itself is natural.</summary>
        public const float RagdollAngularDrag = 0.4f;

        public const float RigidbodyDrag = 0f;

        // ------------------------------------------------------------------ collider policy

        /// <summary>QA fix #1 - a connected collider pair counts as "significantly
        /// overlapping" when the larger radius exceeds this multiple of the smaller.
        /// The per-group table below keeps every connected pair below it.</summary>
        public const float MaxAcceptableAdjacentOverlapRatio = 2.5f;

        /// <summary>QA fix #1 - connected Rigidbody masses must not differ by more
        /// than this factor; larger ratios make the joint solver unstable at
        /// handoff.</summary>
        public const float MaxAcceptableConnectedMassRatio = 4f;

        /// <summary>QA fix #1 - conservative per-bone-group collider radius table
        /// (metres). Narrow limbs, compact torso/pelvis, small head sphere.
        /// Connected pairs taper smoothly (ratio <= 2.5).</summary>
        public static float GetBoneColliderRadius(string boneName)
        {
            switch (boneName)
            {
                case BoneHips:
                    return 0.17f;
                case BoneSpine:
                    return 0.14f;
                case BoneHead:
                    return 0.13f;
                case BoneLeftUpperArm:
                case BoneRightUpperArm:
                    return 0.06f;
                case BoneLeftLowerArm:
                case BoneRightLowerArm:
                    return 0.05f;
                case BoneLeftUpperLeg:
                case BoneRightUpperLeg:
                    return 0.11f;
                case BoneLeftLowerLeg:
                case BoneRightLowerLeg:
                    return 0.09f;
                default:
                    return 0.05f;
            }
        }

        /// <summary>
        /// QA fix #1 - the head is a sphere; every other major bone is a capsule
        /// aligned to the real bone->child direction. Deterministic by NAME, not
        /// by measured length (the old length heuristic was the instability).
        /// </summary>
        public static bool ShouldUseCapsuleCollider(string boneName)
        {
            return boneName != BoneHead;
        }

        /// <summary>
        /// QA fix #1 - capsule height policy: at least a full diameter (so the
        /// capsule never degenerates into a ball) and otherwise exactly the
        /// measured bone length, keeping the collider compact along the bone.
        /// </summary>
        public static float GetCapsuleHeight(float radius, float boneLength)
        {
            return Mathf.Max(radius * 2f, boneLength);
        }

        /// <summary>
        /// QA fix #1 - the rotation that aligns a holder's +Y with the ACTUAL
        /// bone->child direction (expressed in the bone's LOCAL space). Zero or
        /// near-zero directions fall back to identity (safe no-op). The capsule
        /// on the holder then uses direction = Y, so its axis always follows the
        /// real bone, whatever the skeleton's local frames look like.
        /// </summary>
        public static Quaternion ComputeColliderAlignmentRotation(Vector3 childDirectionInBoneLocalSpace)
        {
            if (childDirectionInBoneLocalSpace.sqrMagnitude < 1e-8f)
            {
                return Quaternion.identity;
            }

            Vector3 direction = childDirectionInBoneLocalSpace.normalized;

            // Already aligned (up or down): exact rotations, no drift.
            if (Vector3.Dot(direction, Vector3.up) > 0.999f)
            {
                return Quaternion.identity;
            }

            if (Vector3.Dot(direction, Vector3.down) > 0.999f)
            {
                return Quaternion.Euler(0f, 0f, 180f);
            }

            return Quaternion.FromToRotation(Vector3.up, direction);
        }

        /// <summary>
        /// QA fix #1 - pure overlap ratio of a connected collider pair: larger
        /// radius over smaller radius. > MaxAcceptableAdjacentOverlapRatio means
        /// the pair mushrooms at the joint (visual poke-through + solver churn).
        /// </summary>
        public static float ComputeAdjacentOverlapRatio(float radiusA, float radiusB)
        {
            float safeMin = Mathf.Max(0.0001f, Mathf.Min(radiusA, radiusB));
            return Mathf.Max(radiusA, radiusB) / safeMin;
        }

        /// <summary>QA fix #1 - pure acceptance rule for connected pairs.</summary>
        public static bool IsAdjacentOverlapAcceptable(float overlapRatio)
        {
            return overlapRatio <= MaxAcceptableAdjacentOverlapRatio;
        }

        /// <summary>QA fix #1 - pure acceptance rule for connected masses.</summary>
        public static bool IsConnectedMassRatioAcceptable(float parentMass, float childMass)
        {
            float safeMin = Mathf.Max(0.0001f, Mathf.Min(parentMass, childMass));
            float ratio = Mathf.Max(parentMass, childMass) / safeMin;
            return ratio <= MaxAcceptableConnectedMassRatio;
        }

        // ------------------------------------------------------------------ joint policy

        /// <summary>
        /// QA fix #1 - computes the joint axes from the REAL bone chain.
        /// primary = the bone's own direction (bone -> child) - the TWIST axis;
        /// secondary = cross(parent -> bone, bone -> child) - the HINGE axis
        /// (the plane normal of the two segments). Degenerate (collinear) chains
        /// fall back to cross(direction, up) and finally to +X. Both axes are
        /// world-space directions here; the tool converts them into the child
        /// bone's local space before storing them on the joint.
        /// </summary>
        public static void ComputeJointAxes(
            Vector3 parentToBone, Vector3 boneToChild, out Vector3 primaryAxis, out Vector3 secondaryAxis)
        {
            Vector3 direction = boneToChild.sqrMagnitude > 1e-8f
                ? boneToChild.normalized
                : (parentToBone.sqrMagnitude > 1e-8f ? parentToBone.normalized : Vector3.up);

            primaryAxis = direction;

            Vector3 hinge = Vector3.Cross(parentToBone, boneToChild);

            if (hinge.sqrMagnitude < 1e-8f)
            {
                hinge = Vector3.Cross(direction, Vector3.up);

                if (hinge.sqrMagnitude < 1e-8f)
                {
                    hinge = Vector3.right;
                }
            }

            secondaryAxis = hinge.normalized;
        }

        /// <summary>
        /// QA fix #1 - ANATOMICAL twist limit (degrees) around the bone's own
        /// axis. Elbows/knees barely twist (hinge-like); shoulders/hips keep
        /// moderate twist; the spine twists modestly.
        /// </summary>
        public static float GetJointTwistLimitDegrees(string boneName)
        {
            switch (boneName)
            {
                case BoneHips:
                    return 0f;      // physics root - no joint
                case BoneSpine:
                    return 25f;
                case BoneHead:
                    return 40f;
                case BoneLeftUpperArm:
                case BoneRightUpperArm:
                    return 60f;
                case BoneLeftLowerArm:
                case BoneRightLowerArm:
                    return 15f;     // elbow: hinge-like
                case BoneLeftUpperLeg:
                case BoneRightUpperLeg:
                    return 40f;
                case BoneLeftLowerLeg:
                case BoneRightLowerLeg:
                    return 15f;     // knee: hinge-like
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// QA fix #1 - ANATOMICAL bend limit (degrees) around the hinge axis.
        /// Elbows/knees bend freely enough to collapse (100-110) but can never
        /// free-flail; the spine bends modestly; shoulders/hips are the widest
        /// controlled groups.
        /// </summary>
        public static float GetJointBendLimitDegrees(string boneName)
        {
            switch (boneName)
            {
                case BoneHips:
                    return 0f;
                case BoneSpine:
                    return 30f;
                case BoneHead:
                    return 45f;
                case BoneLeftUpperArm:
                case BoneRightUpperArm:
                    return 80f;
                case BoneLeftLowerArm:
                case BoneRightLowerArm:
                    return 100f;    // elbow bend
                case BoneLeftUpperLeg:
                case BoneRightUpperLeg:
                    return 70f;
                case BoneLeftLowerLeg:
                case BoneRightLowerLeg:
                    return 110f;    // knee bend
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// QA fix #1 - ANATOMICAL lateral limit (degrees) around the third
        /// (perpendicular) axis. Small everywhere: limbs may not flail sideways.
        /// </summary>
        public static float GetJointLateralLimitDegrees(string boneName)
        {
            switch (boneName)
            {
                case BoneHips:
                    return 0f;
                case BoneSpine:
                    return 15f;
                case BoneHead:
                    return 30f;
                case BoneLeftUpperArm:
                case BoneRightUpperArm:
                    return 60f;
                case BoneLeftLowerArm:
                case BoneRightLowerArm:
                    return 10f;     // elbow lateral
                case BoneLeftUpperLeg:
                case BoneRightUpperLeg:
                    return 50f;
                case BoneLeftLowerLeg:
                case BoneRightLowerLeg:
                    return 10f;     // knee lateral
                default:
                    return 0f;
            }
        }

        /// <summary>QA fix #1 - a joint axis with no meaningful freedom is LOCKED
        /// rather than limited (cheaper and stiffer where nothing may move).</summary>
        public static bool ShouldLockJointAxis(float limitDegrees)
        {
            return limitDegrees <= 0f;
        }

        /// <summary>Pure policy: the deterministic joint parent for each ragdoll
        /// bone. The Hips are the physics root (null parent).</summary>
        public static string GetJointParentBoneName(string boneName)
        {
            switch (boneName)
            {
                case BoneHips:
                    return null;                // physics root - no joint
                case BoneSpine:
                    return BoneHips;
                case BoneHead:
                    return BoneSpine;
                case BoneLeftUpperArm:
                case BoneRightUpperArm:
                    return BoneSpine;
                case BoneLeftLowerArm:
                    return BoneLeftUpperArm;
                case BoneRightLowerArm:
                    return BoneRightUpperArm;
                case BoneLeftUpperLeg:
                case BoneRightUpperLeg:
                    return BoneHips;
                case BoneLeftLowerLeg:
                    return BoneLeftUpperLeg;
                case BoneRightLowerLeg:
                    return BoneRightUpperLeg;
                default:
                    return null;
            }
        }

        /// <summary>Pure policy: deterministic body mass per bone group
        /// (hips-heavy, connected ratios kept stable - QA fix #1).</summary>
        public static float GetBoneMass(string boneName)
        {
            for (int i = 0; i < BoneDefinitions.Length; i++)
            {
                if (BoneDefinitions[i].Name == boneName)
                {
                    return BoneDefinitions[i].Mass;
                }
            }

            return 0f;
        }

        [MenuItem("Tools/Operation Outbreak/Set Up Basic Infected Ragdoll")]
        public static void SetUpBasicInfectedRagdoll()
        {
            // The controller must exist before anything references it.
            if (!EnemyAnimationSetup.RebuildController())
            {
                return;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(EnemyVisualSetup.ZombiePrefabPath);

            try
            {
                if (ConfigureRagdollOnContents(contents))
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, EnemyVisualSetup.ZombiePrefabPath);
                }
                else
                {
                    Debug.LogWarning(
                        "[1Q FINAL] Ragdoll setup aborted BEFORE modifying anything - " +
                        "the prefab was NOT saved. The prototype fallback keeps working.",
                        contents);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        [MenuItem("Tools/Operation Outbreak/Debug Basic Infected Ragdoll")]
        public static void DebugBasicInfectedRagdoll()
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(EnemyVisualSetup.ZombiePrefabPath);

            try
            {
                EnemyRagdoll ragdoll = contents.GetComponent<EnemyRagdoll>();

                if (ragdoll == null || !ragdoll.IsConfigured)
                {
                    Debug.LogWarning(
                        "[1Q FINAL] No configured ragdoll on " +
                        EnemyVisualSetup.ZombiePrefabPath +
                        " - run 'Set Up Basic Infected Ragdoll' first.", contents);
                    return;
                }

                LogRagdollDiagnostics(contents, ragdoll);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// Configures the stabilized ragdoll on an already-loaded prefab contents
        /// root (also called by the production visual setup tool before its save).
        /// Validation-first: nothing is modified unless every bone resolves.
        /// </summary>
        public static bool ConfigureRagdollOnContents(GameObject contents)
        {
            Transform productionVisual = contents.transform.Find(EnemyVisualSetup.ProductionVisualName);

            if (productionVisual == null)
            {
                Debug.LogWarning(
                    "[1Q FINAL] Ragdoll setup skipped: no ProductionVisual under " +
                    EnemyVisualSetup.ZombiePrefabPath + " (prototype-only enemy keeps " +
                    "the animation-only death).", contents);
                return false;
            }

            Animator animator = productionVisual.GetComponentInChildren<Animator>(true);

            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
            {
                Debug.LogWarning(
                    "[1Q FINAL] Ragdoll setup skipped: the production Animator has no " +
                    "valid HUMAN avatar to resolve bones from.", contents);
                return false;
            }

            // ---- Phase 1: resolve EVERY bone + measure real child vectors BEFORE
            // ---- touching anything (validation-first).
            var boneTransforms = new Transform[BoneDefinitions.Length];
            var boneChildDirections = new Vector3[BoneDefinitions.Length];
            var boneLengths = new float[BoneDefinitions.Length];

            for (int i = 0; i < BoneDefinitions.Length; i++)
            {
                Transform bone = animator.GetBoneTransform(BoneDefinitions[i].Muscle);

                if (bone == null)
                {
                    Debug.LogWarning(
                        "[1Q FINAL] Ragdoll setup aborted: humanoid bone '" +
                        BoneDefinitions[i].Name + "' does not resolve on the production " +
                        "skeleton. Nothing was modified.", contents);
                    return false;
                }

                boneTransforms[i] = bone;
                TryMeasureBoneChildVector(bone, out boneChildDirections[i], out boneLengths[i]);
            }

            // ---- Phase 2: deterministic build. ----
            var bodies = new Rigidbody[BoneDefinitions.Length];
            var ragdollColliders = new Collider[BoneDefinitions.Length];

            for (int i = 0; i < BoneDefinitions.Length; i++)
            {
                bodies[i] = EnsureRagdollRigidbody(boneTransforms[i].gameObject, BoneDefinitions[i].Mass);
                ragdollColliders[i] = EnsureRagdollCollider(
                    boneTransforms[i].gameObject, BoneDefinitions[i].Name,
                    boneChildDirections[i], boneLengths[i], contents);
            }

            int jointCount = 0;

            for (int i = 1; i < BoneDefinitions.Length; i++)
            {
                string parentName = GetJointParentBoneName(BoneDefinitions[i].Name);
                int parentIndex = IndexOfBone(parentName);

                if (parentIndex < 0)
                {
                    continue;
                }

                EnsureRagdollJoint(
                    boneTransforms[i].gameObject,
                    bodies[i],
                    bodies[parentIndex],
                    BoneDefinitions[i].Name,
                    boneTransforms[i],
                    boneTransforms[parentIndex],
                    boneChildDirections[i],
                    boneLengths[i]);
                jointCount++;
            }

            // ---- Phase 3: wire the runtime components. ----
            EnemyRagdoll ragdoll = contents.GetComponent<EnemyRagdoll>();
            if (ragdoll == null)
            {
                ragdoll = contents.AddComponent<EnemyRagdoll>();
            }

            var ragdollSo = new SerializedObject(ragdoll);
            SerializedProperty bodiesProperty = ragdollSo.FindProperty("ragdollBodies");
            SerializedProperty collidersProperty = ragdollSo.FindProperty("ragdollColliders");
            bodiesProperty.arraySize = bodies.Length;
            collidersProperty.arraySize = ragdollColliders.Length;

            for (int i = 0; i < bodies.Length; i++)
            {
                bodiesProperty.GetArrayElementAtIndex(i).objectReferenceValue = bodies[i];
                collidersProperty.GetArrayElementAtIndex(i).objectReferenceValue = ragdollColliders[i];
            }

            ragdollSo.ApplyModifiedPropertiesWithoutUndo();

            EnemyAnimationBridge bridge = contents.GetComponent<EnemyAnimationBridge>();
            if (bridge != null)
            {
                var bridgeSo = new SerializedObject(bridge);
                bridgeSo.FindProperty("ragdoll").objectReferenceValue = ragdoll;
                bridgeSo.FindProperty("deathRagdollHandoffSeconds").floatValue = DefaultHandoffSeconds;
                bridgeSo.FindProperty("deathRagdollSettleSeconds").floatValue = DefaultSettleSeconds;

                // 1Q FINAL - bypass the animation-path corpse-Y correction: with
                // physics owning the corpse, the grounding blend must be a no-op.
                bridgeSo.FindProperty("deathGroundingStartNormalizedTime").floatValue =
                    GroundingBypassStartNormalizedTime;
                bridgeSo.FindProperty("deathGroundingEndNormalizedTime").floatValue =
                    GroundingBypassEndNormalizedTime;
                bridgeSo.ApplyModifiedPropertiesWithoutUndo();
            }

            Debug.Log(
                "[1Q FINAL] Stabilized hybrid ragdoll death configured: " + bodies.Length +
                " bones (" + string.Join(", ", RequiredBoneNames) + "), " + jointCount +
                " ConfigurableJoints (anatomical per-axis limits, axes from the real bone " +
                "chain), capsules aligned to each bone's actual child direction, " +
                $"self-collision OFF (layer '{RagdollLayerName}'), maxAngularVelocity={MaxAngularVelocity}, " +
                $"angularDrag={RagdollAngularDrag}, handoff={DefaultHandoffSeconds:0.00} s, " +
                $"settle={DefaultSettleSeconds:0.00} s, animation grounding window BYPASSED. " +
                "Bodies authored KINEMATIC, ragdoll colliders authored DISABLED. " +
                "Commit the modified Zombie_Prototype.prefab.", contents);

            LogRagdollDiagnostics(contents, ragdoll);
            return true;
        }

        /// <summary>
        /// QA fix #1 - post-generation validation report: per-bone collider
        /// description, connected-pair overlap ratios (PROBLEMATIC flags) and
        /// connected mass ratios. Deterministic and read-only.
        /// </summary>
        public static void LogRagdollDiagnostics(GameObject contents, EnemyRagdoll ragdoll)
        {
            Rigidbody[] bodies = ragdoll.ConfiguredBodies;
            Collider[] colliders = ragdoll.ConfiguredColliders;
            string[] names = RequiredBoneNames;

            Debug.Log(
                "[1Q FINAL] Ragdoll diagnostics - " + names.Length + " bones, " +
                $"layer '{RagdollLayerName}' (index {LayerMask.NameToLayer(RagdollLayerName)}):",
                contents);

            for (int i = 0; i < names.Length; i++)
            {
                string shape = "MISSING";

                if (i < colliders.Length && colliders[i] != null)
                {
                    if (colliders[i] is CapsuleCollider capsule)
                    {
                        shape = $"capsule r={capsule.radius:0.000} h={capsule.height:0.000} " +
                                $"dir={capsule.direction} layer='{LayerMask.LayerToName(colliders[i].gameObject.layer)}'";
                    }
                    else if (colliders[i] is SphereCollider sphere)
                    {
                        shape = $"sphere r={sphere.radius:0.000} layer='{LayerMask.LayerToName(colliders[i].gameObject.layer)}'";
                    }
                }

                float mass = bodies != null && i < bodies.Length && bodies[i] != null
                    ? bodies[i].mass
                    : 0f;

                Debug.Log(
                    $"  [{i}] {names[i]}: {shape}, mass={mass:0.00}, " +
                    $"parent={(GetJointParentBoneName(names[i]) ?? "ROOT")}", contents);
            }

            // Connected-pair overlap ratios (pure policy applied to the tables).
            for (int i = 1; i < names.Length; i++)
            {
                string parent = GetJointParentBoneName(names[i]);
                int parentIndex = IndexOfBone(parent);

                if (parentIndex < 0)
                {
                    continue;
                }

                float ratio = ComputeAdjacentOverlapRatio(
                    GetBoneColliderRadius(parent), GetBoneColliderRadius(names[i]));
                bool acceptable = IsAdjacentOverlapAcceptable(ratio);

                if (!acceptable)
                {
                    Debug.LogWarning(
                        $"[1Q FINAL] PROBLEMATIC overlap: {parent}<->{names[i]} ratio={ratio:0.00} " +
                        $"(> {MaxAcceptableAdjacentOverlapRatio:0.0}).", contents);
                }

                float massRatio = Mathf.Max(GetBoneMass(parent), GetBoneMass(names[i])) /
                                  Mathf.Max(0.0001f, Mathf.Min(GetBoneMass(parent), GetBoneMass(names[i])));

                if (!IsConnectedMassRatioAcceptable(GetBoneMass(parent), GetBoneMass(names[i])))
                {
                    Debug.LogWarning(
                        $"[1Q FINAL] PROBLEMATIC mass ratio: {parent}<->{names[i]} " +
                        $"ratio={massRatio:0.00} (>{MaxAcceptableConnectedMassRatio:0.0}).", contents);
                }
            }
        }

        private static int IndexOfBone(string boneName)
        {
            for (int i = 0; i < BoneDefinitions.Length; i++)
            {
                if (BoneDefinitions[i].Name == boneName)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// QA fix #1 - measures the ACTUAL vector from the bone to its farthest
        /// child (the anatomical continuation: shoulder->elbow->wrist,
        /// hip->knee->ankle) in the bone's LOCAL space, plus that child distance
        /// as the bone length. Bones without children (the head) report zero.
        /// </summary>
        private static bool TryMeasureBoneChildVector(
            Transform bone, out Vector3 localChildDirection, out float length)
        {
            Transform farthest = null;
            float longest = 0f;

            for (int i = 0; i < bone.childCount; i++)
            {
                Transform child = bone.GetChild(i);
                float distance = Vector3.Distance(bone.position, child.position);

                if (distance > longest)
                {
                    longest = distance;
                    farthest = child;
                }
            }

            if (farthest == null)
            {
                localChildDirection = Vector3.up;
                length = 0f;
                return false;
            }

            localChildDirection =
                bone.InverseTransformDirection((farthest.position - bone.position).normalized);
            length = longest;
            return true;
        }

        private static Rigidbody EnsureRagdollRigidbody(GameObject bone, float mass)
        {
            Rigidbody body = bone.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = bone.AddComponent<Rigidbody>();
            }

            body.mass = mass;
            body.drag = RigidbodyDrag;
            body.angularDrag = RagdollAngularDrag;
            body.useGravity = true;
            body.isKinematic = true;                 // alive: never simulated
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.constraints = RigidbodyConstraints.None;
            body.maxAngularVelocity = MaxAngularVelocity; // QA fix #1: no spin-kicks
            return body;
        }

        /// <summary>
        /// QA fix #1 - ONE primitive collider per bone, living on a dedicated
        /// child holder rotated so the capsule's Y axis follows the ACTUAL
        /// bone->child direction (never a fixed local-Y assumption). The head is
        /// a sphere. The holder is placed on the 'OO_Ragdoll' layer (self-
        /// collision off). Authored DISABLED.
        /// </summary>
        private static Collider EnsureRagdollCollider(
            GameObject bone, string boneName, Vector3 localChildDirection, float boneLength, GameObject context)
        {
            // Remove any colliders authored directly on the bone by an earlier
            // tool version - the holder is now the single source of truth.
            foreach (Collider existing in bone.GetComponents<Collider>())
            {
                Object.DestroyImmediate(existing);
            }

            Transform holder = bone.transform.Find(RagdollColliderChildName);
            if (holder == null)
            {
                holder = new GameObject(RagdollColliderChildName).transform;
                holder.SetParent(bone.transform, false);
            }

            holder.localPosition = Vector3.zero;
            holder.localScale = Vector3.one;

            bool capsule = ShouldUseCapsuleCollider(boneName);
            holder.localRotation = capsule
                ? ComputeColliderAlignmentRotation(localChildDirection)
                : Quaternion.identity;

            // QA fix #1 - self-collision policy: everything ragdoll goes on the
            // dedicated layer; EnemyRagdoll disables layer-vs-itself collisions
            // at runtime (Physics.IgnoreLayerCollision), so corpse parts only
            // interact with the environment/road.
            int ragdollLayer = LayerMask.NameToLayer(RagdollLayerName);
            if (ragdollLayer >= 0)
            {
                holder.gameObject.layer = ragdollLayer;
            }
            else
            {
                Debug.LogWarning(
                    "[1Q FINAL] Layer '" + RagdollLayerName + "' is missing from " +
                    "ProjectSettings/TagManager.asset - ragdoll self-collision cannot be " +
                    "disabled by layer. Add the layer and re-run the setup.", context);
            }

            // One collider per holder: remove strays from earlier runs.
            foreach (Collider existing in holder.GetComponents<Collider>())
            {
                Object.DestroyImmediate(existing);
            }

            float radius = GetBoneColliderRadius(boneName);

            if (capsule)
            {
                CapsuleCollider capsuleCollider = holder.gameObject.AddComponent<CapsuleCollider>();
                capsuleCollider.radius = radius;
                capsuleCollider.height = GetCapsuleHeight(radius, boneLength);
                capsuleCollider.direction = 1;       // the holder's Y = real bone direction
                capsuleCollider.center = Vector3.zero;
                capsuleCollider.enabled = false;     // alive: never collides
                return capsuleCollider;
            }

            SphereCollider sphere = holder.gameObject.AddComponent<SphereCollider>();
            sphere.radius = radius;
            sphere.center = Vector3.zero;
            sphere.enabled = false;                  // alive: never collides
            return sphere;
        }

        /// <summary>
        /// QA fix #1 - anatomical ConfigurableJoint: axes computed from the real
        /// bone chain and stored child-local; per-axis twist/bend/lateral limits
        /// from the bone-group tables (hinge-like elbows/knees, controlled
        /// shoulders/hips, modest spine, controlled head); zero-freedom axes are
        /// LOCKED. No projection, no connected-pair collision.
        /// </summary>
        private static void EnsureRagdollJoint(
            GameObject bone, Rigidbody body, Rigidbody connectedBody, string boneName,
            Transform boneTransform, Transform parentTransform,
            Vector3 localChildDirection, float boneLength)
        {
            // One joint per bone: remove any hand-authored joint first.
            CharacterJoint legacyCharacter = bone.GetComponent<CharacterJoint>();
            if (legacyCharacter != null)
            {
                Object.DestroyImmediate(legacyCharacter);
            }

            ConfigurableJoint joint = bone.GetComponent<ConfigurableJoint>();
            if (joint == null)
            {
                joint = bone.AddComponent<ConfigurableJoint>();
            }

            // Real bone chain, in world space:
            Vector3 parentToBone = boneTransform.position - parentTransform.position;
            Vector3 boneToChild = boneTransform.TransformDirection(localChildDirection) * boneLength;

            ComputeJointAxes(parentToBone, boneToChild, out Vector3 primaryAxis, out Vector3 secondaryAxis);

            joint.connectedBody = connectedBody;
            joint.anchor = Vector3.zero;             // the bone's own pivot
            joint.autoConfigureConnectedAnchor = true;
            joint.enableCollision = false;           // connected parts never collide
            joint.enablePreprocessing = true;
            joint.projectionMode = JointProjectionMode.None; // mobile: no projection cost

            // Axes are stored in the CHILD bone's local space.
            joint.axis = boneTransform.InverseTransformDirection(primaryAxis);
            joint.secondaryAxis = boneTransform.InverseTransformDirection(secondaryAxis);

            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;

            float twist = GetJointTwistLimitDegrees(boneName);
            float bend = GetJointBendLimitDegrees(boneName);
            float lateral = GetJointLateralLimitDegrees(boneName);

            joint.angularXMotion = ShouldLockJointAxis(twist)
                ? ConfigurableJointMotion.Locked
                : ConfigurableJointMotion.Limited;
            joint.angularYMotion = ShouldLockJointAxis(bend)
                ? ConfigurableJointMotion.Locked
                : ConfigurableJointMotion.Limited;
            joint.angularZMotion = ShouldLockJointAxis(lateral)
                ? ConfigurableJointMotion.Locked
                : ConfigurableJointMotion.Limited;

            joint.lowAngularXLimit = MakeSoftLimit(twist);
            joint.highAngularXLimit = MakeSoftLimit(twist);
            joint.angularYLimit = MakeSoftLimit(bend);
            joint.angularZLimit = MakeSoftLimit(lateral);
        }

        private static SoftJointLimit MakeSoftLimit(float degrees)
        {
            return new SoftJointLimit
            {
                limit = Mathf.Max(0f, degrees),
                bounciness = 0f,
                contactDistance = 0.01f,
            };
        }
    }
}
#endif
