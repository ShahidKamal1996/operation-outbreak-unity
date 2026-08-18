#if UNITY_EDITOR
using System.Collections.Generic;
using OperationOutbreak.Enemies;
using UnityEditor;
using UnityEngine;

namespace OperationOutbreak.EditorTools
{
    /// <summary>
    /// Milestone 1Q - FINAL production death upgrade (hybrid animation -> ragdoll):
    /// deterministic editor authoring of the lightweight mobile ragdoll on the
    /// production zombie skeleton.
    ///
    /// WHAT THE TOOL DOES (idempotent - running it twice leaves the same state):
    ///   1. Resolves the MAJOR humanoid bones from the imported
    ///      StylizedZombieAvatar via Animator.GetBoneTransform (no hand-authored
    ///      FBX fileIDs, no manual collider/joint creation): Hips, Spine, Head,
    ///      Left/Right Upper/Lower Arm, Left/Right Upper/Lower Leg. Fingers, toes,
    ///      hands and feet are deliberately excluded (mobile budget).
    ///   2. Per bone: a Rigidbody (kinematic while alive; masses distributed hips-
    ///      heavy) and ONE primitive collider (capsule along the bone for long
    ///      bones, sphere for short ones like the head; radius derived from bone
    ///      length, capped). Colliders are authored DISABLED.
    ///   3. Connects the bones with ConfigurableJoints (hard symmetric angular
    ///      limits per bone group, linear motion locked, no projection, no
    ///      collision between connected bodies).
    ///   4. Adds/rewires the EnemyRagdoll component on the enemy root with the
    ///      bodies + colliders arrays in PARENT-BEFORE-CHILD order (the reuse
    ///      reset depends on that order).
    ///   5. Wires EnemyAnimationBridge: ragdoll reference, handoff time
    ///      (default 0.30 s - inside the 0.25-0.40 s band of the death clip's
    ///      fall), settle time (0.6 s), and BYPASSES the animation grounding by
    ///      zeroing the grounding window - the corpse-Y correction must never
    ///      fight ragdoll physics.
    ///
    /// FALLBACK: if the production visual/Animator/avatar or any required bone is
    /// missing, the tool aborts BEFORE modifying anything (validation first) and
    /// the prototype visual + animation-only death keep working exactly as before.
    ///
    /// USAGE: Tools > Operation Outbreak > Set Up Basic Infected Ragdoll, or
    /// simply re-run Tools > Operation Outbreak > Set Up Basic Infected Production
    /// Visual (it calls this tool automatically). Commit the modified
    /// Zombie_Prototype.prefab afterwards.
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
        /// children.
        /// </summary>
        private static readonly BoneDefinition[] BoneDefinitions =
        {
            new BoneDefinition(BoneHips, HumanBodyBones.Hips, 2.5f),
            new BoneDefinition(BoneSpine, HumanBodyBones.Spine, 1.5f),
            new BoneDefinition(BoneHead, HumanBodyBones.Head, 1.0f),
            new BoneDefinition(BoneLeftUpperArm, HumanBodyBones.LeftUpperArm, 0.6f),
            new BoneDefinition(BoneRightUpperArm, HumanBodyBones.RightUpperArm, 0.6f),
            new BoneDefinition(BoneLeftLowerArm, HumanBodyBones.LeftLowerArm, 0.4f),
            new BoneDefinition(BoneRightLowerArm, HumanBodyBones.RightLowerArm, 0.4f),
            new BoneDefinition(BoneLeftUpperLeg, HumanBodyBones.LeftUpperLeg, 1.2f),
            new BoneDefinition(BoneRightUpperLeg, HumanBodyBones.RightUpperLeg, 1.2f),
            new BoneDefinition(BoneLeftLowerLeg, HumanBodyBones.LeftLowerLeg, 0.8f),
            new BoneDefinition(BoneRightLowerLeg, HumanBodyBones.RightLowerLeg, 0.8f),
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

        /// <summary>Bones at least this long get a capsule (along their length);
        /// shorter bones (e.g. the head) get a sphere.</summary>
        public const float LongBoneLengthThreshold = 0.1f;

        /// <summary>Collider radius = boneLength * this scale / 2 (capped).</summary>
        public const float ColliderRadiusScale = 0.9f;

        /// <summary>Never thicker than this, whatever the bone length says.</summary>
        public const float ColliderRadiusCap = 0.3f;

        public const float RigidbodyDrag = 0f;
        public const float RigidbodyAngularDrag = 0.05f;

        /// <summary>
        /// Pure policy: a bone long enough to matter gets a capsule (limbs/torso),
        /// a short bone (head) gets a sphere. Static for EditMode tests.
        /// </summary>
        public static bool ShouldUseCapsuleCollider(float boneLength)
        {
            return boneLength >= LongBoneLengthThreshold;
        }

        /// <summary>
        /// Pure policy: collider radius from the bone length - half the bone width
        /// scaled down a touch, floored and capped. Static for EditMode tests.
        /// </summary>
        public static float ComputeColliderRadius(float boneLength)
        {
            return Mathf.Min(ColliderRadiusCap, Mathf.Max(0.01f, boneLength * ColliderRadiusScale * 0.5f));
        }

        /// <summary>
        /// Pure policy: the deterministic joint parent for each ragdoll bone. The
        /// Hips are the physics root (null parent). Static for EditMode tests.
        /// </summary>
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
                    return null;                // unknown bones have no policy
            }
        }

        /// <summary>Pure policy: deterministic body mass per bone group (hips-heavy).</summary>
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

        /// <summary>
        /// Pure policy: deterministic symmetric joint angular limit per bone group
        /// (degrees). Hips have no joint (physics root).
        /// </summary>
        public static float GetJointAngularLimitDegrees(string boneName)
        {
            switch (boneName)
            {
                case BoneSpine:
                    return 45f;
                case BoneHead:
                    return 80f;
                case BoneLeftUpperArm:
                case BoneRightUpperArm:
                    return 100f;
                case BoneLeftLowerArm:
                case BoneRightLowerArm:
                    return 120f;
                case BoneLeftUpperLeg:
                case BoneRightUpperLeg:
                case BoneLeftLowerLeg:
                case BoneRightLowerLeg:
                    return 90f;
                default:
                    return 0f; // Hips: no joint
            }
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

        /// <summary>
        /// Configures the ragdoll on an already-loaded prefab contents root
        /// (also called by the production visual setup tool before its save).
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

            // ---- Phase 1: resolve EVERY bone before touching anything. ----
            var boneTransforms = new Transform[BoneDefinitions.Length];
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
                boneLengths[i] = MeasureBoneLength(bone);
            }

            // ---- Phase 2: deterministic build. ----
            var bodies = new Rigidbody[BoneDefinitions.Length];
            var ragdollColliders = new Collider[BoneDefinitions.Length];

            for (int i = 0; i < BoneDefinitions.Length; i++)
            {
                bodies[i] = EnsureRagdollRigidbody(
                    boneTransforms[i].gameObject, BoneDefinitions[i].Mass);
                ragdollColliders[i] = EnsureRagdollCollider(
                    boneTransforms[i].gameObject, boneLengths[i]);
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
                    GetJointAngularLimitDegrees(BoneDefinitions[i].Name));
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
                "[1Q FINAL] Hybrid ragdoll death configured: " + bodies.Length + " bones (" +
                string.Join(", ", RequiredBoneNames) + "), " + jointCount + " ConfigurableJoints, " +
                $"handoff={DefaultHandoffSeconds:0.00} s (0.25-0.40 band), settle={DefaultSettleSeconds:0.00} s, " +
                "animation grounding window BYPASSED (0 -> 0). Bodies authored KINEMATIC, " +
                "ragdoll colliders authored DISABLED (gameplay CapsuleCollider is the only " +
                "live collider). Commit the modified Zombie_Prototype.prefab.", contents);

            return true;
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

        /// <summary>Bone length = distance to the FARTHEST direct child (the next
        /// bone in the chain; for the head it stays small -> sphere).</summary>
        private static float MeasureBoneLength(Transform bone)
        {
            float longest = 0f;

            for (int i = 0; i < bone.childCount; i++)
            {
                float distance = Vector3.Distance(bone.position, bone.GetChild(i).position);
                if (distance > longest)
                {
                    longest = distance;
                }
            }

            return longest;
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
            body.angularDrag = RigidbodyAngularDrag;
            body.useGravity = true;
            body.isKinematic = true;                 // alive: never simulated
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.constraints = RigidbodyConstraints.None;
            return body;
        }

        private static Collider EnsureRagdollCollider(GameObject bone, float boneLength)
        {
            // One primitive collider per bone - remove anything else so a re-run
            // (or a hand-edited state) can never leave duplicate colliders.
            foreach (Collider existing in bone.GetComponents<Collider>())
            {
                Object.DestroyImmediate(existing);
            }

            float radius = ComputeColliderRadius(boneLength);

            if (ShouldUseCapsuleCollider(boneLength))
            {
                CapsuleCollider capsule = bone.AddComponent<CapsuleCollider>();
                capsule.radius = radius;
                capsule.height = Mathf.Max(radius * 2f, boneLength);
                capsule.direction = 1; // local Y: along the bone toward its child
                capsule.center = Vector3.zero;
                capsule.enabled = false;             // alive: never collides
                return capsule;
            }

            SphereCollider sphere = bone.AddComponent<SphereCollider>();
            sphere.radius = radius;
            sphere.center = Vector3.zero;
            sphere.enabled = false;                  // alive: never collides
            return sphere;
        }

        private static void EnsureRagdollJoint(
            GameObject bone, Rigidbody body, Rigidbody connectedBody, float limitDegrees)
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

            joint.connectedBody = connectedBody;
            joint.anchor = Vector3.zero;             // the bone's own pivot
            joint.autoConfigureConnectedAnchor = true;
            joint.enableCollision = false;           // no corpse self-collision churn
            joint.enablePreprocessing = true;
            joint.projectionMode = JointProjectionMode.None; // mobile: no projection cost
            joint.axis = Vector3.right;
            joint.secondaryAxis = Vector3.up;

            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;

            var limit = new SoftJointLimit
            {
                limit = limitDegrees,
                bounciness = 0f,
                contactDistance = 0.01f,
            };

            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.lowAngularXLimit = limit;
            joint.highAngularXLimit = limit;
            joint.angularYLimit = limit;
            joint.angularZLimit = limit;
        }
    }
}
#endif
