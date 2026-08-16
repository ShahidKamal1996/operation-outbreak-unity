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

        // State names shared with the validation utility and the EditMode tests.
        public const string NeutralStanceState = "NeutralStance";
        public const string LocomotionState = "Locomotion";
        public const string GunplayState = "Gunplay";

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
            AssetDatabase.AddObjectToAsset(blendTree, controller);
            blendTree.AddChild(idle, 0.15f);
            blendTree.AddChild(run, 0.85f);
            locomotion.motion = blendTree;

            // FIRING - assault_combat_shoot (non-looping), AnyState trigger with
            // self-transition allowed, exit-time return to idle/run at 80%.
            AnimatorState gunplay = root.AddState(GunplayState, new Vector3(610f, 60f, 0f));
            gunplay.motion = shoot;

            AnimatorStateTransition idleToRun = neutral.AddTransition(locomotion);
            idleToRun.hasExitTime = false;
            idleToRun.duration = 0.12f;
            idleToRun.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");

            AnimatorStateTransition runToIdle = locomotion.AddTransition(neutral);
            runToIdle.hasExitTime = false;
            runToIdle.duration = 0.12f;
            runToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsMoving");

            AnimatorStateTransition anyToGunplay = root.AddAnyStateTransition(gunplay);
            anyToGunplay.hasExitTime = false;
            anyToGunplay.duration = 0.05f;
            anyToGunplay.canTransitionToSelf = true;
            anyToGunplay.AddCondition(AnimatorConditionMode.If, 0f, "Gunplay");
            anyToGunplay.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");

            AnimatorStateTransition gunToRun = gunplay.AddTransition(locomotion);
            gunToRun.hasExitTime = true;
            gunToRun.exitTime = 0.8f;
            gunToRun.duration = 0.08f;
            gunToRun.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");

            AnimatorStateTransition gunToIdle = gunplay.AddTransition(neutral);
            gunToIdle.hasExitTime = true;
            gunToIdle.exitTime = 0.8f;
            gunToIdle.duration = 0.08f;
            gunToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsMoving");

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[1P.5 QA fix] Toon Soldier controller rebuilt with resolved clips: " +
                $"idle='{idle.name}', run='{run.name}', shoot='{shoot.name}'. " +
                "Save the scene and commit the regenerated controller asset.", controller);
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
            if (gunplay == null)
            {
                problems.Add(GunplayState + " state missing.");
            }
            else if (gunplay.motion == null)
            {
                problems.Add(GunplayState + " has no motion.");
            }
            else if (gunplay.motion != shoot)
            {
                problems.Add(GunplayState + " motion does not resolve to the " + ShootFbxPath + " clip.");
            }

            return problems;
        }

        // ------------------------------------------------------------------ helpers

        private static void ClearController(AnimatorController controller)
        {
            // Remove orphaned blend-tree sub-assets from previous rebuilds first.
            Object[] existing = AssetDatabase.LoadAllAssetsAtPath(ControllerPath);
            if (existing != null)
            {
                foreach (Object asset in existing)
                {
                    if (asset is BlendTree)
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
