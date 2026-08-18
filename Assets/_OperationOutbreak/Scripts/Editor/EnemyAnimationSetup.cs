#if UNITY_EDITOR
using System.Collections.Generic;
using OperationOutbreak.Enemies;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OperationOutbreak.EditorTools
{
    /// <summary>
    /// Milestone 1Q - authors the Operation Outbreak-owned Basic Infected animator
    /// controller WITH UNITY, from the real Mixamo clips. This follows the project
    /// rule established by 1P.5 QA fix #1: FBX clip sub-asset references are NEVER
    /// hand-authored - the clips are resolved through AssetDatabase and the controller
    /// is built with UnityEditor.Animations APIs, so Unity generates every reference.
    /// The rebuild is idempotent and preserves the controller asset GUID.
    ///
    /// STATE MACHINE (smallest set that maps to gameplay):
    ///   Parameters: Speed (float), Attack (trigger), Dead (bool) - the exact
    ///   EnemyAnimationBridge contract.
    ///   Idle    (default) = zombie idle  (loops)
    ///   Walk               = zombie walk  (loops)
    ///   Attack             = zombie attack (non-looping; AnyState trigger with
    ///                        self-transition, exit-time return to Idle/Walk by Speed)
    ///   Death              = zombie death (non-looping; AnyState on Dead, no exits)
    ///   The zombie run clip is deliberately NOT wired: it is reserved for future
    ///   Runner variants and must not redefine Basic Infected locomotion.
    ///
    /// USAGE:
    ///   Tools > Operation Outbreak > Rebuild Basic Infected Animator Controller
    ///   Tools > Operation Outbreak > Validate Basic Infected Animator
    /// (Set Up Basic Infected Production Visual also runs the rebuild automatically.)
    /// </summary>
    public static class EnemyAnimationSetup
    {
        public const string ControllerPath =
            "Assets/_OperationOutbreak/Art/Animations/Enemies/OO_BasicInfected.controller";

        public const string IdleFbxPath =
            "Assets/_OperationOutbreak/Art/Animations/Enemies/Mixamo/zombie idle.fbx";

        public const string WalkFbxPath =
            "Assets/_OperationOutbreak/Art/Animations/Enemies/Mixamo/zombie walk.fbx";

        public const string RunFbxPath =
            "Assets/_OperationOutbreak/Art/Animations/Enemies/Mixamo/zombie run.fbx";

        public const string AttackFbxPath =
            "Assets/_OperationOutbreak/Art/Animations/Enemies/Mixamo/zombie attack.fbx";

        public const string DeathFbxPath =
            "Assets/_OperationOutbreak/Art/Animations/Enemies/Mixamo/zombie death.fbx";

        // State names shared with the validation utility and the EditMode tests.
        public const string IdleState = "Idle";
        public const string WalkState = "Walk";
        public const string AttackState = "Attack";
        // QA fix #2 - the Death state name is shared with EnemyAnimationBridge so the
        // bridge's direct death crossfade always targets the real state.
        public const string DeathState = EnemyAnimationBridge.DeathStateName;

        /// <summary>Locomotion threshold: Speed at or above this selects Walk.</summary>
        public const float WalkSpeedThreshold = 0.1f;

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

        [MenuItem("Tools/Operation Outbreak/Rebuild Basic Infected Animator Controller")]
        public static bool RebuildController()
        {
            AnimationClip idle = ResolveClip(IdleFbxPath);
            AnimationClip walk = ResolveClip(WalkFbxPath);
            AnimationClip attack = ResolveClip(AttackFbxPath);
            AnimationClip death = ResolveClip(DeathFbxPath);

            if (idle == null || walk == null || attack == null || death == null)
            {
                EditorUtility.DisplayDialog(
                    "Basic Infected Animator",
                    "Could not resolve the Mixamo zombie clips.\nCheck that these assets exist and contain clips:\n" +
                    IdleFbxPath + "\n" + WalkFbxPath + "\n" + AttackFbxPath + "\n" + DeathFbxPath,
                    "OK");
                return false;
            }

            // Rebuild IN PLACE so the asset GUID stays stable (the enemy visual setup
            // tool assigns the controller by reference).
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }
            else
            {
                ClearController(controller);
            }

            // Parameter set: the exact EnemyAnimationBridge contract.
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            controller.AddParameter(
                EnemyAnimationBridge.LocomotionSpeedMultiplierParameter,
                AnimatorControllerParameterType.Float);

            AnimatorStateMachine root = controller.layers[0].stateMachine;

            // QA fix #4 - the base state machine's name is part of the full state
            // path the bridge uses for Animator.Play ("Base Layer.Death"). Pin it to
            // the shared constant so the full-path hash always resolves.
            root.name = EnemyAnimationBridge.BaseLayerName;

            // IDLE - zombie idle (loops in its FBX import settings).
            AnimatorState idleState = root.AddState(IdleState, new Vector3(290f, 60f, 0f));
            idleState.motion = idle;
            root.defaultState = idleState;

            // WALK - zombie walk (loops). Basic Infected walks by design; the run clip
            // is reserved for future Runner variants.
            AnimatorState walkState = root.AddState(WalkState, new Vector3(290f, 180f, 0f));
            walkState.motion = walk;

            // Milestone 1Q Bug 4 - cadence sync: ONLY the Walk state's playback speed
            // is driven by LocomotionSpeedMultiplier, computed by the bridge from the
            // actual code-driven planar speed. Idle/Attack/Death keep their authored
            // fixed speed, so attack and death timing are untouched, and the future
            // Runner can reuse the same mechanism at higher speeds.
            walkState.speedParameterActive = true;
            walkState.speedParameter = EnemyAnimationBridge.LocomotionSpeedMultiplierParameter;

            AnimatorStateTransition idleToWalk = idleState.AddTransition(walkState);
            idleToWalk.hasExitTime = false;
            idleToWalk.duration = 0.15f;
            idleToWalk.AddCondition(AnimatorConditionMode.Greater, WalkSpeedThreshold, "Speed");

            AnimatorStateTransition walkToIdle = walkState.AddTransition(idleState);
            walkToIdle.hasExitTime = false;
            walkToIdle.duration = 0.15f;
            walkToIdle.AddCondition(AnimatorConditionMode.Less, WalkSpeedThreshold, "Speed");

            // ATTACK - zombie attack (non-looping), AnyState trigger with
            // self-transition allowed so consecutive gameplay attacks can re-trigger,
            // exit-time return to Idle/Walk by Speed.
            AnimatorState attackState = root.AddState(AttackState, new Vector3(610f, 60f, 0f));
            attackState.motion = attack;

            AnimatorStateTransition anyToAttack = root.AddAnyStateTransition(attackState);
            anyToAttack.hasExitTime = false;
            anyToAttack.duration = 0.05f;
            anyToAttack.canTransitionToSelf = true;
            anyToAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            anyToAttack.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");

            AnimatorStateTransition attackToWalk = attackState.AddTransition(walkState);
            attackToWalk.hasExitTime = true;
            attackToWalk.exitTime = 0.85f;
            attackToWalk.duration = 0.15f;
            attackToWalk.AddCondition(AnimatorConditionMode.Greater, WalkSpeedThreshold, "Speed");

            AnimatorStateTransition attackToIdle = attackState.AddTransition(idleState);
            attackToIdle.hasExitTime = true;
            attackToIdle.exitTime = 0.85f;
            attackToIdle.duration = 0.15f;
            attackToIdle.AddCondition(AnimatorConditionMode.Less, WalkSpeedThreshold, "Speed");

            // DEATH - zombie death (non-looping). AnyState on Dead, no exits: once
            // dead the enemy can never animate out of the death state.
            AnimatorState deathState = root.AddState(DeathState, new Vector3(610f, 300f, 0f));
            deathState.motion = death;

            AnimatorStateTransition anyToDeath = root.AddAnyStateTransition(deathState);
            anyToDeath.hasExitTime = false;
            anyToDeath.duration = 0.1f;
            anyToDeath.AddCondition(AnimatorConditionMode.If, 0f, "Dead");

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[1Q] Basic Infected controller rebuilt with resolved clips: " +
                $"idle='{idle.name}', walk='{walk.name}', attack='{attack.name}', death='{death.name}'. " +
                "Save and commit the regenerated controller asset.", controller);
            return true;
        }

        [MenuItem("Tools/Operation Outbreak/Validate Basic Infected Animator")]
        public static void ValidateController()
        {
            List<string> problems = CollectValidationProblems();

            if (problems.Count == 0)
            {
                Debug.Log("[1Q] Basic Infected animator validation PASSED: idle/walk/attack/death " +
                          "states carry resolved Mixamo clips, bridge parameters intact.");
                return;
            }

            foreach (string problem in problems)
            {
                Debug.LogWarning("[1Q] " + problem);
            }

            Debug.LogWarning("[1Q] Validation failed - run " +
                             "Tools > Operation Outbreak > Rebuild Basic Infected Animator Controller.");
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
            AnimationClip walk = ResolveClip(WalkFbxPath);
            AnimationClip run = ResolveClip(RunFbxPath);
            AnimationClip attack = ResolveClip(AttackFbxPath);
            AnimationClip death = ResolveClip(DeathFbxPath);

            if (idle == null) problems.Add("No AnimationClip found in " + IdleFbxPath);
            if (walk == null) problems.Add("No AnimationClip found in " + WalkFbxPath);
            if (attack == null) problems.Add("No AnimationClip found in " + AttackFbxPath);
            if (death == null) problems.Add("No AnimationClip found in " + DeathFbxPath);

            CheckParameter(problems, controller, "Speed", AnimatorControllerParameterType.Float);
            CheckParameter(problems, controller, "Attack", AnimatorControllerParameterType.Trigger);
            CheckParameter(problems, controller, "Dead", AnimatorControllerParameterType.Bool);
            CheckParameter(problems, controller,
                EnemyAnimationBridge.LocomotionSpeedMultiplierParameter,
                AnimatorControllerParameterType.Float);

            if (controller.layers.Length == 0)
            {
                problems.Add("Controller has no layers.");
                return problems;
            }

            AnimatorStateMachine root = controller.layers[0].stateMachine;

            // QA fix #4 - the full death path depends on the base machine's name.
            if (root.name != EnemyAnimationBridge.BaseLayerName)
            {
                problems.Add("The base layer's state machine must be named '" +
                             EnemyAnimationBridge.BaseLayerName + "' (is '" + root.name +
                             "'), otherwise the bridge's full-path death hash '" +
                             EnemyAnimationBridge.DeathStateFullPath + "' cannot resolve.");
            }

            if (root.defaultState == null)
            {
                problems.Add("No default state set.");
            }
            else if (root.defaultState.name != IdleState)
            {
                problems.Add($"Default state is '{root.defaultState.name}', expected {IdleState}.");
            }

            AnimatorState idleState = FindState(root, IdleState);
            if (idleState == null)
            {
                problems.Add(IdleState + " state missing.");
            }
            else if (idleState.motion != idle)
            {
                problems.Add(IdleState + " motion does not resolve to the " + IdleFbxPath + " clip.");
            }

            AnimatorState walkState = FindState(root, WalkState);
            if (walkState == null)
            {
                problems.Add(WalkState + " state missing.");
            }
            else
            {
                if (walkState.motion != walk)
                {
                    problems.Add(WalkState + " motion does not resolve to the " + WalkFbxPath + " clip.");
                }

                // Bug 4: cadence sync must be wired on the Walk state only.
                if (!walkState.speedParameterActive)
                {
                    problems.Add(WalkState + " must be driven by the locomotion speed multiplier " +
                                 "(speedParameterActive = true) or the feet slide against gameplay speed.");
                }
                else if (walkState.speedParameter != EnemyAnimationBridge.LocomotionSpeedMultiplierParameter)
                {
                    problems.Add(WalkState + " speed parameter should be '" +
                                 EnemyAnimationBridge.LocomotionSpeedMultiplierParameter +
                                 "', is '" + walkState.speedParameter + "'.");
                }
            }

            // The run clip must stay reserved for future Runner variants.
            if (run != null && root.states != null)
            {
                foreach (ChildAnimatorState child in root.states)
                {
                    if (child.state.motion == run)
                    {
                        problems.Add("The zombie run clip must NOT be part of Basic Infected " +
                                     "locomotion - it is reserved for future Runner variants.");
                    }
                }
            }

            AnimatorState attackState = FindState(root, AttackState);
            if (attackState == null)
            {
                problems.Add(AttackState + " state missing.");
            }
            else if (attackState.motion != attack)
            {
                problems.Add(AttackState + " motion does not resolve to the " + AttackFbxPath + " clip.");
            }

            AnimatorState deathState = FindState(root, DeathState);
            if (deathState == null)
            {
                problems.Add(DeathState + " state missing.");
            }
            else
            {
                if (deathState.motion != death)
                {
                    problems.Add(DeathState + " motion does not resolve to the " + DeathFbxPath + " clip.");
                }

                if (deathState.transitions != null && deathState.transitions.Length > 0)
                {
                    problems.Add(DeathState + " must have no outgoing transitions - a dead enemy " +
                                 "must never animate back into locomotion or attack.");
                }

                // Bug 4: only the Walk state may be driven by the locomotion multiplier.
                if (deathState.speedParameterActive)
                {
                    problems.Add(DeathState + " must NOT be driven by the locomotion speed " +
                                 "multiplier - death timing is authored.");
                }
            }

            AnimatorState idleCheck = FindState(root, IdleState);
            if (idleCheck != null && idleCheck.speedParameterActive)
            {
                problems.Add(IdleState + " must NOT be driven by the locomotion speed multiplier.");
            }

            AnimatorState attackCheck = FindState(root, AttackState);
            if (attackCheck != null && attackCheck.speedParameterActive)
            {
                problems.Add(AttackState + " must NOT be driven by the locomotion speed " +
                             "multiplier - attack timing is authored.");
            }

            return problems;
        }

        // ------------------------------------------------------------------ helpers

        private static void ClearController(AnimatorController controller)
        {
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

            problems.Add("Enemy bridge parameter '" + parameterName + "' is missing.");
        }
    }
}
#endif
