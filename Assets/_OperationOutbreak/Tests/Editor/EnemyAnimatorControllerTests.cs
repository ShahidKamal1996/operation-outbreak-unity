using System.Collections.Generic;
using NUnit.Framework;
using OperationOutbreak.EditorTools;
using OperationOutbreak.Enemies;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1Q - EditMode regression tests for the Basic Infected production
    /// animation foundation. They pin:
    ///   - the OO_BasicInfected.controller structure (idle/walk/attack/death states
    ///     resolve to the REAL Mixamo clips; the run clip stays reserved for future
    ///     Runner variants; death has no exits; bridge parameters intact);
    ///   - the prototype fallback (Zombie_Prototype.prefab keeps its ZombieController
    ///     and Visual child regardless of whether the production visual was set up);
    ///   - the production prefab itself still resolves in the project.
    ///
    /// NOTE: the controller tests assert the REBUILT controller. On a fresh checkout,
    /// run Tools > Operation Outbreak > Rebuild Basic Infected Animator Controller
    /// (or the full Set Up Basic Infected Production Visual) once first.
    /// </summary>
    public sealed class EnemyAnimatorControllerTests
    {
        [Test]
        public void BasicInfectedControllerPassesAllValidationChecks()
        {
            List<string> problems = EnemyAnimationSetup.CollectValidationProblems();

            Assert.IsEmpty(
                problems,
                "Basic Infected controller validation failed. If this is a fresh checkout, " +
                "run Tools > Operation Outbreak > Rebuild Basic Infected Animator Controller first.\n" +
                string.Join("\n", problems));
        }

        [Test]
        public void BridgeParametersArePresentWithExpectedTypes()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                EnemyAnimationSetup.ControllerPath);

            Assert.IsNotNull(controller, "Controller asset missing - run the rebuild tool.");

            AssertParameter(controller, "Speed", AnimatorControllerParameterType.Float);
            AssertParameter(controller, "Attack", AnimatorControllerParameterType.Trigger);
            AssertParameter(controller, "Dead", AnimatorControllerParameterType.Bool);
        }

        [Test]
        public void StateMotionsResolveToTheMixamoClips()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                EnemyAnimationSetup.ControllerPath);

            Assert.IsNotNull(controller, "Controller asset missing - run the rebuild tool.");

            AnimationClip idle = EnemyAnimationSetup.ResolveClip(EnemyAnimationSetup.IdleFbxPath);
            AnimationClip walk = EnemyAnimationSetup.ResolveClip(EnemyAnimationSetup.WalkFbxPath);
            AnimationClip run = EnemyAnimationSetup.ResolveClip(EnemyAnimationSetup.RunFbxPath);
            AnimationClip attack = EnemyAnimationSetup.ResolveClip(EnemyAnimationSetup.AttackFbxPath);
            AnimationClip death = EnemyAnimationSetup.ResolveClip(EnemyAnimationSetup.DeathFbxPath);

            Assert.IsNotNull(idle, "No clip resolved from the idle FBX.");
            Assert.IsNotNull(walk, "No clip resolved from the walk FBX.");
            Assert.IsNotNull(run, "No clip resolved from the run FBX.");
            Assert.IsNotNull(attack, "No clip resolved from the attack FBX.");
            Assert.IsNotNull(death, "No clip resolved from the death FBX.");

            AnimatorStateMachine root = controller.layers[0].stateMachine;

            AnimatorState idleState = FindState(root, EnemyAnimationSetup.IdleState);
            Assert.IsNotNull(idleState, "Idle state missing.");
            Assert.AreEqual(idle, idleState.motion as AnimationClip,
                "Idle must play the zombie idle clip.");

            AnimatorState walkState = FindState(root, EnemyAnimationSetup.WalkState);
            Assert.IsNotNull(walkState, "Walk state missing.");
            Assert.AreEqual(walk, walkState.motion as AnimationClip,
                "Walk must play the zombie walk clip.");

            AnimatorState attackState = FindState(root, EnemyAnimationSetup.AttackState);
            Assert.IsNotNull(attackState, "Attack state missing.");
            Assert.AreEqual(attack, attackState.motion as AnimationClip,
                "Attack must play the zombie attack clip.");

            AnimatorState deathState = FindState(root, EnemyAnimationSetup.DeathState);
            Assert.IsNotNull(deathState, "Death state missing.");
            Assert.AreEqual(death, deathState.motion as AnimationClip,
                "Death must play the zombie death clip.");

            Assert.IsNotNull(root.defaultState, "The controller needs a default state.");
            Assert.AreEqual(
                EnemyAnimationSetup.IdleState, root.defaultState.name,
                "The enemy must start in Idle.");

            // The run clip is reserved for future Runner variants.
            foreach (ChildAnimatorState child in root.states)
            {
                Assert.AreNotEqual(run, child.state.motion,
                    "The zombie run clip must not be part of Basic Infected locomotion " +
                    "(reserved for future Runner variants).");
            }
        }

        [Test]
        public void DeathStateHasNoOutgoingTransitions()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                EnemyAnimationSetup.ControllerPath);

            Assert.IsNotNull(controller, "Controller asset missing - run the rebuild tool.");

            AnimatorState deathState = FindState(
                controller.layers[0].stateMachine, EnemyAnimationSetup.DeathState);

            Assert.IsNotNull(deathState, "Death state missing.");
            Assert.IsEmpty(
                deathState.transitions,
                "A dead enemy must never transition back into locomotion or attack.");
        }

        [Test]
        public void PrototypeEnemyFallbackRemainsIntact()
        {
            // The safe fallback: the prototype prefab keeps its gameplay controller and
            // its Visual child, whether or not the production visual tool has run.
            GameObject prototype = AssetDatabase.LoadAssetAtPath<GameObject>(
                EnemyVisualSetup.ZombiePrefabPath);

            Assert.IsNotNull(prototype,
                "Zombie_Prototype.prefab is missing - the spawner depends on it.");

            Assert.IsNotNull(prototype.GetComponent<ZombieController>(),
                "The prototype prefab must keep its ZombieController gameplay authority.");
            Assert.IsNotNull(prototype.transform.Find(EnemyVisualSetup.PrototypeVisualName),
                "The prototype Visual child must remain (safe fallback presentation).");
        }

        [Test]
        public void ProductionPrefabResolvesInTheProject()
        {
            GameObject production = AssetDatabase.LoadAssetAtPath<GameObject>(
                EnemyVisualSetup.ProductionPrefabPath);

            Assert.IsNotNull(production,
                "StylizedZombie_01.prefab is missing - re-import the Stylized Zombie package.");
        }

        [Test]
        public void PrototypeVisualHidesOnlyWhenProductionIsActive()
        {
            Assert.IsTrue(
                EnemyVisualSetup.ShouldHidePrototypeVisual(true),
                "With the production visual active, the prototype mesh must be hidden.");
            Assert.IsFalse(
                EnemyVisualSetup.ShouldHidePrototypeVisual(false),
                "Without the production visual, the prototype mesh must stay visible " +
                "(the safe debugging fallback).");
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
                $"Enemy bridge parameter '{parameterName}' is missing - the contract " +
                "is Speed/Attack/Dead.");
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
