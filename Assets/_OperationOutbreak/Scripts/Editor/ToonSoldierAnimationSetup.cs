#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OperationOutbreak.EditorTools
{
    /// <summary>
    /// Milestone 1P.5 QA fix - the Toon Soldier animator controller is now AUTHORED BY
    /// UNITY instead of hand-written YAML.
    ///
    /// ROOT CAUSE OF THE QA FAILURE ("states active, Clip Count: 0, no visible motion"):
    /// the first 1P.5 controller was hand-authored YAML whose motion references reused
    /// the internal clip fileID (-203655887218126122) observed in Carl's mixamo-derived
    /// animation FBXs. The Toon Soldiers package FBXs are 3ds-Max/Biped exports whose
    /// embedded AnimationClip sub-assets hash to DIFFERENT internal fileIDs, so every
    /// motion reference pointed at a non-existent sub-asset. Unity still loaded the
    /// states and parameters (hence a live-looking state machine) but resolved zero
    /// clips (hence Animator Inspector "Clip Count: 0" and a static pose).
    ///
    /// FIX CONTRACT: never guess FBX sub-asset fileIDs by hand again. This tool resolves
    /// the REAL AnimationClip sub-assets through AssetDatabase and rebuilds the
    /// controller in place with UnityEditor.Animations APIs, so Unity itself generates
    /// every reference. The rebuild preserves the controller asset (and therefore its
    /// GUID, which the scene and the setup tools reference), is idempotent, and never
    /// touches the imported FBXs, the avatar, the model or any gameplay code.
    ///
    /// USAGE:
    ///   Tools > Operation Outbreak > Rebuild Toon Soldier Animator Controller
    ///   Tools > Operation Outbreak > Validate Toon Soldier Animator
    /// (Set Up Toon Soldier Player Visual also runs the rebuild automatically.)
    ///
    /// QA fix #12 - LAYERED SHOOTING: manual QA found that firing while moving froze
    /// the soldier's legs (the character slid forward in a static shoot pose). Root
    /// cause: the full-body shoot clip lived on the BASE Layer next to the locomotion
    /// blend tree, so every Gunplay trigger replaced the locomotion state entirely.
    /// The rebuild now produces a two-layer controller:
    ///   - BASE Layer: NeutralStance (idle) and Locomotion (blend tree) ONLY - the
    ///     legs are never interrupted by firing.
    ///   - SHOOT Layer (weight 1, Override blending, upper-body AvatarMask): an Empty
    ///     default state (passes the base-layer pose through when not firing) plus the
    ///     Gunplay state (assault_combat_shoot). The mask limits the shoot influence
    ///     to the torso/head/arms, keeping pelvis/hips and both legs on the Base Layer,
    ///     and the exit-time transition blends the upper body smoothly back to
    ///     locomotion when firing stops.
    /// </summary>
    public static class ToonSoldierAnimationSetup
    {
        public const string ControllerPath =
            "Assets/_OperationOutbreak/Art/Animations/Player/ToonSoldier_Player.controller";

        public const string IdleFbxPath =
            "Assets/ToonSoldiers_demo/animation/assault_combat_idle.FBX";

        public const string RunFbxPath =
            "Assets/ToonSoldiers_demo/animation/assault_combat_run.FBX";

        public const string ShootFbxPath =
            "Assets/ToonSoldiers_demo/animation/assault_combat_shoot.FBX";

        /// <summary>QA fix #12 - upper-body avatar mask asset created/configured by the rebuild.</summary>
        public const string UpperBodyMaskPath =
            "Assets/_OperationOutbreak/Art/Animations/Player/ToonSoldier_UpperBodyMask.mask";

        // State/layer names shared with the validation utility and the EditMode tests.
        public const string NeutralStanceState = "NeutralStance";
        public const string LocomotionState = "Locomotion";
        public const string GunplayState = "Gunplay";
        public const string ShootLayerName = "Shoot Layer";
        public const string EmptyStateName = "Empty";

        /// <summary>
        /// Resolves the AnimationClip embedded in an animation FBX by type scan, so the
        /// result is correct regardless of the take name Unity assigned on import.
        /// </summary>
        public static AnimationClip ResolveClip(string fbxPath)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (asset is AnimationClip clip)
                {
                    return clip;
                }
            }

            return null;
        }

        [MenuItem("Tools/Operation Outbreak/Rebuild Toon Soldier Animator Controller")]
        public static bool RebuildController()
        {
            AnimationClip idle = ResolveClip(IdleFbxPath);
            AnimationClip run = ResolveClip(RunFbxPath);
            AnimationClip shoot = ResolveClip(ShootFbxPath);

            if (idle == null || run == null || shoot == null)
            {
                EditorUtility.DisplayDialog(
                    "Toon Soldier Animator",
                    "Could not resolve the Toon Soldiers animation clips.\n" +
                    "Check that these assets exist and contain clips:\n" +
                    IdleFbxPath + "\n" + RunFbxPath + "\n" + ShootFbxPath,
                    "OK");
                return false;
            }

            // Rebuild IN PLACE so the asset GUID stays stable (the scene wires the
            // controller by GUID through a prefab-instance override).
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }
            else
            {
                ClearController(controller);
            }

            // Parameter set: the exact existing PlayerAnimationBridge contract.
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Gunplay", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("HitReaction", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);

            AnimatorStateMachine root = controller.layers[0].stateMachine;

            // IDLE - assault_combat_idle (loops in its FBX import settings).
            AnimatorState neutral = root.AddState(NeutralStanceState, new Vector3(290f, 60f, 0f));
            neutral.motion = idle;
            root.defaultState = neutral;

            // MOVING - blend tree: idle at 0.15, run at 0.85, driven by Speed.
            AnimatorState locomotion = root.AddState(LocomotionState, new Vector3(290f, 180f, 0f));
            BlendTree blendTree = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false,
            };

            // QA fix #5 - keep the blend tree a hidden sub-asset of the controller,
            // exactly like the Shoot Layer state machine below. A sub-asset with
            // HideFlags.None serializes as m_ObjectHideFlags: 0, which makes the
            // committed YAML diverge from Unity's canonical controller layout
            // (every nested object is HideInHierarchy) and shows the tree as a
            // stray entry when the controller asset is expanded.
            blendTree.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(blendTree, controller);
            blendTree.AddChild(idle, 0.15f);
            blendTree.AddChild(run, 0.85f);
            locomotion.motion = blendTree;

            AnimatorStateTransition idleToRun = neutral.AddTransition(locomotion);
            idleToRun.hasExitTime = false;
            idleToRun.duration = 0.12f;
            idleToRun.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");

            AnimatorStateTransition runToIdle = locomotion.AddTransition(neutral);
            runToIdle.hasExitTime = false;
            runToIdle.duration = 0.12f;
            runToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsMoving");

            // FIRING - assault_combat_shoot (non-looping). QA fix #12: the shoot clip
            // moved OFF the base layer onto a dedicated upper-body layer, so firing can
            // never replace or freeze the locomotion legs.

            // Create (or reuse) the upper-body avatar mask: torso, head and arms only.
            // Pelvis/hips and both legs stay on the Base Layer, driven by locomotion.
            AvatarMask upperBodyMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
            if (upperBodyMask == null)
            {
                upperBodyMask = new AvatarMask();
                upperBodyMask.name = "ToonSoldier_UpperBodyMask";
                AssetDatabase.CreateAsset(upperBodyMask, UpperBodyMaskPath);
            }
            ConfigureUpperBodyMask(upperBodyMask);

            // Shoot layer: weight 1, Override blending, masked to the upper body.
            AnimatorStateMachine shootMachine = new AnimatorStateMachine
            {
                name = ShootLayerName,
            };

            // QA fix #12B (persistence) - a LAYER state machine is a separate Unity
            // object: it MUST be added as a sub-asset of the controller, otherwise it
            // exists only in memory and the serialized layer keeps
            // m_StateMachine: {fileID: 0}. That is exactly why Unity logged
            // "Statemachine for layer 'Shoot Layer' is missing" after every
            // editor/domain reload and scene restore. HideInHierarchy keeps it from
            // appearing as a stray asset in the Project window (Unity's documented
            // pattern for nested state machines).
            shootMachine.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(shootMachine, controller);

            AnimatorControllerLayer shootLayer = new AnimatorControllerLayer
            {
                name = ShootLayerName,
                stateMachine = shootMachine,
                defaultWeight = 1f,
                blendingMode = AnimatorLayerBlendingMode.Override,
                avatarMask = upperBodyMask,
            };
            controller.AddLayer(shootLayer);

            // Empty default state: under the mask, an empty state leaves the upper body
            // in the base-layer pose, so idle/run show normally when not firing.
            AnimatorState empty = shootMachine.AddState(EmptyStateName, new Vector3(290f, 60f, 0f));
            shootMachine.defaultState = empty;

            AnimatorState gunplay = shootMachine.AddState(GunplayState, new Vector3(610f, 60f, 0f));
            gunplay.motion = shoot;

            AnimatorStateTransition anyToGunplay = shootMachine.AddAnyStateTransition(gunplay);
            anyToGunplay.hasExitTime = false;
            anyToGunplay.duration = 0.05f;
            anyToGunplay.canTransitionToSelf = true;
            anyToGunplay.AddCondition(AnimatorConditionMode.If, 0f, "Gunplay");
            anyToGunplay.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");

            // Smooth removal of the shooting influence when firing stops: exit at 90%
            // of the clip and blend the upper body back over 0.15s. The legs were never
            // touched, so locomotion continues uninterrupted throughout.
            AnimatorStateTransition gunToEmpty = gunplay.AddTransition(empty);
            gunToEmpty.hasExitTime = true;
            gunToEmpty.exitTime = 0.9f;
            gunToEmpty.duration = 0.15f;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[1P.5 QA fix #12] Toon Soldier controller rebuilt: base layer = idle/locomotion, " +
                $"shoot layer = upper-body masked gunplay. idle='{idle.name}', run='{run.name}', " +
                $"shoot='{shoot.name}'. Save the scene and commit the regenerated controller asset " +
                "and the new upper-body mask asset.", controller);
            return true;
        }

        [MenuItem("Tools/Operation Outbreak/Validate Toon Soldier Animator")]
        public static void ValidateController()
        {
            List<string> problems = CollectValidationProblems();

            if (problems.Count == 0)
            {
                Debug.Log("[1P.5] Toon Soldier animator validation PASSED: all states carry " +
                          "resolved Toon Soldiers clips, blend tree and bridge parameters intact.");
                return;
            }

            foreach (string problem in problems)
            {
                Debug.LogWarning("[1P.5] " + problem);
            }

            Debug.LogWarning("[1P.5] Validation failed - run " +
                             "Tools > Operation Outbreak > Rebuild Toon Soldier Animator Controller.");
        }

        /// <summary>
        /// Shared, side-effect-free validation used by both the menu validator and the
        /// EditMode tests: returns every problem found, or an empty list on success.
        /// </summary>
        public static List<string> CollectValidationProblems()
        {
            List<string> problems = new List<string>();

            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

            if (controller == null)
            {
                problems.Add("Controller asset missing at " + ControllerPath);
                return problems;
            }

            AnimationClip idle = ResolveClip(IdleFbxPath);
            AnimationClip run = ResolveClip(RunFbxPath);
            AnimationClip shoot = ResolveClip(ShootFbxPath);

            if (idle == null) problems.Add("No AnimationClip found in " + IdleFbxPath);
            if (run == null) problems.Add("No AnimationClip found in " + RunFbxPath);
            if (shoot == null) problems.Add("No AnimationClip found in " + ShootFbxPath);

            CheckParameter(problems, controller, "Speed", AnimatorControllerParameterType.Float);
            CheckParameter(problems, controller, "IsMoving", AnimatorControllerParameterType.Bool);
            CheckParameter(problems, controller, "Gunplay", AnimatorControllerParameterType.Trigger);
            CheckParameter(problems, controller, "HitReaction", AnimatorControllerParameterType.Trigger);
            CheckParameter(problems, controller, "Dead", AnimatorControllerParameterType.Bool);

            if (controller.layers.Length == 0)
            {
                problems.Add("Controller has no layers.");
                return problems;
            }

            AnimatorStateMachine root = controller.layers[0].stateMachine;

            if (root.defaultState == null)
            {
                problems.Add("No default state set.");
            }
            else if (root.defaultState.name != NeutralStanceState)
            {
                problems.Add($"Default state is '{root.defaultState.name}', expected {NeutralStanceState}.");
            }

            AnimatorState neutral = FindState(root, NeutralStanceState);
            if (neutral == null)
            {
                problems.Add(NeutralStanceState + " state missing.");
            }
            else if (neutral.motion == null)
            {
                problems.Add(NeutralStanceState + " has no motion (the Clip Count: 0 class bug).");
            }
            else if (neutral.motion != idle)
            {
                problems.Add(NeutralStanceState + " motion does not resolve to the " + IdleFbxPath + " clip.");
            }

            AnimatorState locomotion = FindState(root, LocomotionState);
            if (locomotion == null)
            {
                problems.Add(LocomotionState + " state missing.");
            }
            else if (locomotion.motion == null)
            {
                problems.Add(LocomotionState + " has no motion.");
            }
            else if (locomotion.motion is BlendTree tree)
            {
                if (tree.children.Length != 2)
                {
                    problems.Add("Locomotion blend tree must have exactly 2 children, has " +
                                 tree.children.Length + ".");
                }
                else
                {
                    if (tree.children[0].motion != idle)
                        problems.Add("Locomotion blend child 0 is not the idle clip.");
                    if (tree.children[1].motion != run)
                        problems.Add("Locomotion blend child 1 is not the run clip.");
                }
            }
            else
            {
                problems.Add(LocomotionState + " motion is not a BlendTree.");
            }

            AnimatorState gunplay = FindState(root, GunplayState);
            if (gunplay != null)
            {
                problems.Add(GunplayState + " must NOT live on the base layer anymore (QA fix #12) - " +
                             "a full-body shoot state on the base layer freezes locomotion while firing.");
            }

            // ---------------------------------------------------------------- shoot layer

            if (controller.layers.Length < 2)
            {
                problems.Add("QA fix #12: the controller needs a second (shoot) layer.");
                return problems;
            }

            AnimatorControllerLayer shootLayer = controller.layers[1];

            if (shootLayer.name != ShootLayerName)
            {
                problems.Add($"Layer 1 should be '{ShootLayerName}', is '{shootLayer.name}'.");
            }

            if (shootLayer.blendingMode != AnimatorLayerBlendingMode.Override)
            {
                problems.Add("The shoot layer must use Override blending.");
            }

            if (shootLayer.defaultWeight < 0.99f)
            {
                problems.Add("The shoot layer weight must be 1 so firing always shows.");
            }

            if (shootLayer.avatarMask == null)
            {
                problems.Add("QA fix #12: the shoot layer is missing its upper-body AvatarMask - " +
                             "without it the full-body shoot clip would override locomotion again.");
            }
            else
            {
                AvatarMask mask = shootLayer.avatarMask;

                if (!mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body))
                    problems.Add("The shoot mask must include the torso (Body part).");
                if (!mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Head))
                    problems.Add("The shoot mask must include the head.");
                if (!mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm))
                    problems.Add("The shoot mask must include the left arm.");
                if (!mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm))
                    problems.Add("The shoot mask must include the right arm.");
                if (mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg))
                    problems.Add("The shoot mask must NOT include the left leg - legs stay on the base layer.");
                if (mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg))
                    problems.Add("The shoot mask must NOT include the right leg - legs stay on the base layer.");

                // QA fix #12A - Unity's AvatarMask transform APIs take an INDEX, not a
                // bone-name string. Resolve the path to its mask index first; when the
                // mask does not contain a "Hips" path there is nothing to exclude, so
                // only an ACTIVE hips transform is a problem.
                int hipsIndex = FindTransformIndex(mask, "Hips");
                if (hipsIndex >= 0 && mask.GetTransformActive(hipsIndex))
                    problems.Add("The shoot mask must NOT include the hips - the pelvis stays on the base layer.");
            }

            AnimatorStateMachine shootMachine = shootLayer.stateMachine;

            AnimatorState empty = FindState(shootMachine, EmptyStateName);
            if (empty == null)
            {
                problems.Add("The shoot layer needs its Empty default state.");
            }
            else if (empty.motion != null)
            {
                problems.Add("The shoot layer Empty state must have no motion - it passes the " +
                             "base-layer pose through when not firing.");
            }

            if (shootMachine.defaultState == null || shootMachine.defaultState != empty)
            {
                problems.Add("The shoot layer's default state must be the Empty state.");
            }

            AnimatorState shootGunplay = FindState(shootMachine, GunplayState);
            if (shootGunplay == null)
            {
                problems.Add(GunplayState + " state missing from the shoot layer.");
            }
            else if (shootGunplay.motion == null)
            {
                problems.Add(GunplayState + " has no motion.");
            }
            else if (shootGunplay.motion != shoot)
            {
                problems.Add(GunplayState + " motion does not resolve to the " + ShootFbxPath + " clip.");
            }

            return problems;
        }

        /// <summary>
        /// QA fix #12 - configures the upper-body avatar mask: torso (Body), head and
        /// both arms active; legs inactive; hips explicitly inactive so the pelvis and
        /// both legs stay driven by the Base Layer locomotion while shooting.
        /// </summary>
        private static void ConfigureUpperBodyMask(AvatarMask mask)
        {
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, false);

            // Body includes the hips; exclude them so the pelvis keeps the base-layer
            // locomotion pose. QA fix #12A - the transform APIs take a mask INDEX, so
            // the "Hips" path is resolved against the mask's transform list first; when
            // the mask does not carry that path there is nothing to exclude. Idempotent
            // and safe to call on every rebuild.
            SetTransformActiveByPath(mask, "Hips", false);

            EditorUtility.SetDirty(mask);
        }

        /// <summary>
        /// QA fix #12A - returns the mask index whose transform path equals
        /// <paramref name="path"/>, or -1 when the mask does not contain it.
        /// </summary>
        private static int FindTransformIndex(AvatarMask mask, string path)
        {
            for (int i = 0; i < mask.transformCount; i++)
            {
                if (mask.GetTransformPath(i) == path)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// QA fix #12A - activates/deactivates a mask transform addressed by its
        /// humanoid bone path. Unity's AvatarMask.SetTransformActive takes an INDEX,
        /// not a string; the index is derived from the mask's own transform list, so
        /// nothing is hard-coded and a missing path is a safe no-op.
        /// </summary>
        private static void SetTransformActiveByPath(AvatarMask mask, string path, bool active)
        {
            int index = FindTransformIndex(mask, path);

            if (index >= 0)
            {
                mask.SetTransformActive(index, active);
            }
        }

        // ------------------------------------------------------------------ helpers

        private static void ClearController(AnimatorController controller)
        {
            // Remove orphaned sub-assets from previous rebuilds first (blend trees and
            // nested layer state machines). The BASE layer's root state machine is an
            // intrinsic part of the controller and must never be removed.
            Object[] existing = AssetDatabase.LoadAllAssetsAtPath(ControllerPath);
            if (existing != null)
            {
                AnimatorStateMachine rootMachine =
                    controller.layers.Length > 0 ? controller.layers[0].stateMachine : null;

                foreach (Object asset in existing)
                {
                    if (asset is BlendTree)
                    {
                        AssetDatabase.RemoveObjectFromAsset(asset);
                    }
                    else if (asset is AnimatorStateMachine && asset != rootMachine)
                    {
                        AssetDatabase.RemoveObjectFromAsset(asset);
                    }
                }
            }

            for (int i = controller.layers.Length - 1; i > 0; i--)
            {
                controller.RemoveLayer(i);
            }

            AnimatorStateMachine root = controller.layers[0].stateMachine;

            AnimatorStateTransition[] anyStateTransitions = root.anyStateTransitions;
            for (int i = anyStateTransitions.Length - 1; i >= 0; i--)
            {
                root.RemoveAnyStateTransition(anyStateTransitions[i]);
            }

            ChildAnimatorState[] states = root.states;
            for (int i = states.Length - 1; i >= 0; i--)
            {
                root.RemoveState(states[i].state);
            }

            for (int i = controller.parameters.Length - 1; i >= 0; i--)
            {
                controller.RemoveParameter(i);
            }
        }

        private static AnimatorState FindState(AnimatorStateMachine root, string stateName)
        {
            foreach (ChildAnimatorState child in root.states)
            {
                if (child.state.name == stateName)
                {
                    return child.state;
                }
            }

            return null;
        }

        private static void CheckParameter(
            List<string> problems,
            AnimatorController controller,
            string parameterName,
            AnimatorControllerParameterType expectedType)
        {
            for (int i = 0; i < controller.parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = controller.parameters[i];
                if (parameter.name == parameterName)
                {
                    if (parameter.type != expectedType)
                    {
                        problems.Add($"Parameter '{parameterName}' has type {parameter.type}, expected {expectedType}.");
                    }

                    return;
                }
            }

            problems.Add("Bridge parameter '" + parameterName + "' is missing.");
        }
    }
}
#endif
