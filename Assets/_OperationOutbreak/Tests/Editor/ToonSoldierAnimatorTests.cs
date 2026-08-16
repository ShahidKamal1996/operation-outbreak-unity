using System.Collections.Generic;
using NUnit.Framework;
using OperationOutbreak.EditorTools;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1P.5 QA fix - EditMode regression tests for the Toon Soldier animator
    /// controller. They pin the exact failure manual QA found ("states active but the
    /// character does not animate, Clip Count: 0"): every expected state must carry a
    /// Motion that resolves to a REAL AnimationClip sub-asset of the Toon Soldiers
    /// animation FBXs, and the bridge parameter contract must be intact.
    ///
    /// NOTE: these tests assert the REBUILT controller. If the hand-authored controller
    /// has not been regenerated on the local machine yet, they fail by design - run
    /// Tools > Operation Outbreak > Rebuild Toon Soldier Animator Controller (or the
    /// full Set Up Toon Soldier Player Visual) once, then re-run the suite. After that,
    /// any future regression in clip wiring fails the suite instead of shipping to QA.
    /// </summary>
    public sealed class ToonSoldierAnimatorTests
    {
        [Test]
        public void ToonSoldierControllerPassesAllValidationChecks()
        {
            List<string> problems = ToonSoldierAnimationSetup.CollectValidationProblems();

            Assert.IsEmpty(
                problems,
                "Toon Soldier controller validation failed. If this is a fresh checkout, " +
                "run Tools > Operation Outbreak > Rebuild Toon Soldier Animator Controller first.\n" +
                string.Join("\n", problems));
        }

        [Test]
        public void BridgeParametersArePresentWithExpectedTypes()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                ToonSoldierAnimationSetup.ControllerPath);

            Assert.IsNotNull(controller, "Controller asset missing - run the rebuild tool.");

            AssertParameter(controller, "Speed", AnimatorControllerParameterType.Float);
            AssertParameter(controller, "IsMoving", AnimatorControllerParameterType.Bool);
            AssertParameter(controller, "Gunplay", AnimatorControllerParameterType.Trigger);
            AssertParameter(controller, "HitReaction", AnimatorControllerParameterType.Trigger);
            AssertParameter(controller, "Dead", AnimatorControllerParameterType.Bool);
        }

        [Test]
        public void StateMotionsResolveToToonSoldiersPackageClips()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                ToonSoldierAnimationSetup.ControllerPath);

            Assert.IsNotNull(controller, "Controller asset missing - run the rebuild tool.");

            AnimationClip idle = ToonSoldierAnimationSetup.ResolveClip(
                ToonSoldierAnimationSetup.IdleFbxPath);
            AnimationClip run = ToonSoldierAnimationSetup.ResolveClip(
                ToonSoldierAnimationSetup.RunFbxPath);
            AnimationClip shoot = ToonSoldierAnimationSetup.ResolveClip(
                ToonSoldierAnimationSetup.ShootFbxPath);

            Assert.IsNotNull(idle, "No clip resolved from the idle FBX.");
            Assert.IsNotNull(run, "No clip resolved from the run FBX.");
            Assert.IsNotNull(shoot, "No clip resolved from the shoot FBX.");

            AnimatorStateMachine root = controller.layers[0].stateMachine;

            AnimatorState neutral = FindState(root, ToonSoldierAnimationSetup.NeutralStanceState);
            Assert.IsNotNull(neutral, "NeutralStance state missing.");
            Assert.AreEqual(idle, neutral.motion as AnimationClip,
                "NeutralStance must play the assault_combat_idle clip (the Clip Count: 0 bug).");

            AnimatorState locomotion = FindState(root, ToonSoldierAnimationSetup.LocomotionState);
            Assert.IsNotNull(locomotion, "Locomotion state missing.");
            BlendTree tree = locomotion.motion as BlendTree;
            Assert.IsNotNull(tree, "Locomotion motion must be a BlendTree.");
            Assert.AreEqual(2, tree.children.Length, "Locomotion blend tree needs idle + run.");
            Assert.AreEqual(idle, tree.children[0].motion as AnimationClip,
                "Blend child 0 must be the idle clip.");
            Assert.AreEqual(run, tree.children[1].motion as AnimationClip,
                "Blend child 1 must be the run clip.");

            AnimatorState gunplay = FindState(root, ToonSoldierAnimationSetup.GunplayState);
            Assert.IsNotNull(gunplay, "Gunplay state missing.");
            Assert.AreEqual(shoot, gunplay.motion as AnimationClip,
                "Gunplay must play the assault_combat_shoot clip.");

            Assert.IsNotNull(root.defaultState, "The controller needs a default state.");
            Assert.AreEqual(
                ToonSoldierAnimationSetup.NeutralStanceState, root.defaultState.name,
                "The soldier must start in NeutralStance.");
        }

        private static void AssertParameter(
            AnimatorController controller,
            string parameterName,
            AnimatorControllerParameterType expectedType)
        {
            for (int i = 0; i < controller.parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = controller.parameters[i];
                if (parameter.name == parameterName)
                {
                    Assert.AreEqual(
                        expectedType, parameter.type,
                        $"Parameter '{parameterName}' has the wrong type.");
                    return;
                }
            }

            Assert.Fail(
                $"Bridge parameter '{parameterName}' is missing - the PlayerAnimationBridge " +
                "contract is Speed/IsMoving/Gunplay/HitReaction/Dead.");
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
    }
}
