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
            AssertParameter(
                controller,
                EnemyAnimationBridge.LocomotionSpeedMultiplierParameter,
                AnimatorControllerParameterType.Float);
        }

        [Test]
        public void WalkStateIsDrivenByTheLocomotionMultiplier_OtherStatesAreNot()
        {
            // Bug 4 regression: only the Walk state's playback speed may be driven by
            // the locomotion multiplier. Attack and Death must keep their authored
            // fixed speed, otherwise their timing changes with movement speed.
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                EnemyAnimationSetup.ControllerPath);

            Assert.IsNotNull(controller, "Controller asset missing - run the rebuild tool.");

            AnimatorStateMachine root = controller.layers[0].stateMachine;

            AnimatorState walkState = FindState(root, EnemyAnimationSetup.WalkState);
            Assert.IsNotNull(walkState, "Walk state missing.");
            Assert.IsTrue(walkState.speedParameterActive,
                "The Walk state must be driven by the locomotion speed multiplier " +
                "(the Bug 4 foot-sliding fix).");
            Assert.AreEqual(
                EnemyAnimationBridge.LocomotionSpeedMultiplierParameter,
                walkState.speedParameter,
                "The Walk state's speed parameter must be LocomotionSpeedMultiplier.");

            AnimatorState idleState = FindState(root, EnemyAnimationSetup.IdleState);
            Assert.IsNotNull(idleState, "Idle state missing.");
            Assert.IsFalse(idleState.speedParameterActive,
                "Idle must not be driven by the locomotion multiplier.");

            AnimatorState attackState = FindState(root, EnemyAnimationSetup.AttackState);
            Assert.IsNotNull(attackState, "Attack state missing.");
            Assert.IsFalse(attackState.speedParameterActive,
                "Attack must not be driven by the locomotion multiplier - its timing is authored.");

            AnimatorState deathState = FindState(root, EnemyAnimationSetup.DeathState);
            Assert.IsNotNull(deathState, "Death state missing.");
            Assert.IsFalse(deathState.speedParameterActive,
                "Death must not be driven by the locomotion multiplier - its timing is authored.");
        }

        [Test]
        public void ComputeLocomotionSpeedMultiplier_SynchronizesCadenceAndClamps()
        {
            // Pure bridge math: multiplier = planarSpeed / walkReference, clamped.
            Assert.AreEqual(
                2f,
                EnemyAnimationBridge.ComputeLocomotionSpeedMultiplier(2.5f, 1.25f, 0.5f, 2.5f),
                0.0001f,
                "Gameplay 2.5 u/s against a 1.25 u/s walk reference must play the walk at 2x.");

            Assert.AreEqual(
                0.5f,
                EnemyAnimationBridge.ComputeLocomotionSpeedMultiplier(0f, 1.25f, 0.5f, 2.5f),
                0.0001f,
                "Zero speed must clamp to the minimum multiplier (Walk is inactive anyway).");

            Assert.AreEqual(
                2.5f,
                EnemyAnimationBridge.ComputeLocomotionSpeedMultiplier(10f, 1.25f, 0.5f, 2.5f),
                0.0001f,
                "Extreme speeds must clamp to the maximum multiplier.");

            Assert.AreEqual(
                0.5f,
                EnemyAnimationBridge.ComputeLocomotionSpeedMultiplier(2.5f, 1.25f, 0.5f, 0.4f),
                0.0001f,
                "A misconfigured maximum below the minimum must clamp to the minimum.");

            Assert.Greater(
                EnemyAnimationBridge.ComputeLocomotionSpeedMultiplier(2.5f, 0f, 0.5f, 2.5f),
                0f,
                "A zero reference must be guarded - never divide by zero.");
        }

        [Test]
        public void BasicInfectedAuthoredGameplaySpeedRemainsUnchanged()
        {
            // Bug 4 fix must NOT change gameplay movement speed - the multiplier is
            // presentation only. The approved Basic value stays 2.5.
            GameObject prototype = AssetDatabase.LoadAssetAtPath<GameObject>(
                EnemyVisualSetup.ZombiePrefabPath);

            Assert.IsNotNull(prototype, "Zombie_Prototype.prefab is missing.");

            ZombieController zombie = prototype.GetComponent<ZombieController>();
            Assert.IsNotNull(zombie, "ZombieController missing from the prefab.");
            Assert.AreEqual(2.5f, zombie.MoveSpeed, 0.0001f,
                "The approved Basic Infected gameplay speed (2.5) must not change.");
        }

        // ============================================ QA fix #1B (Bugs 1/2/3) regression

        [Test]
        public void ProductionGroundingOffset_GroundsTheLowestMeshPoint()
        {
            // Bug 1 regression: the deterministic grounding derivation must place the
            // zombie's lowest mesh point on the lane (enemy root local Y = -1).
            GameObject zombie = new GameObject("StylizedZombie_01");
            GameObject meshObject = new GameObject("MESH");
            meshObject.transform.SetParent(zombie.transform, false);

            MeshFilter filter = meshObject.AddComponent<MeshFilter>();
            meshObject.AddComponent<MeshRenderer>();

            Mesh mesh = new Mesh
            {
                // Feet exactly at the instance-root origin: min Y = 0.
                bounds = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(1f, 2f, 1f)),
            };
            filter.sharedMesh = mesh;

            try
            {
                bool computed = EnemyVisualSetup.TryComputeProductionGroundingOffsetY(
                    zombie, out float offsetY);

                Assert.IsTrue(computed, "A zombie instance with renderers must yield a grounding offset.");
                Assert.AreEqual(-1f, offsetY, 0.01f,
                    "Feet at the instance origin must be lowered one unit to the lane " +
                    "under the enemy root's y=1 convention.");
            }
            finally
            {
                Object.DestroyImmediate(zombie);
            }
        }

        [Test]
        public void LegacyTransformPunchAppliesOnlyWithoutTheProductionVisual()
        {
            // Bug 2 regression: the Animator-driven production zombie must not receive
            // legacy transform feedback (scale punch) that fights its skeleton. The
            // prototype fallback keeps the legacy behavior.
            Assert.IsFalse(
                ZombieController.ShouldApplyLegacyTransformPunch(true),
                "With the production visual active, the legacy scale punch must be off " +
                "(transform feedback vibrates an Animator-driven skeleton).");
            Assert.IsTrue(
                ZombieController.ShouldApplyLegacyTransformPunch(false),
                "The prototype fallback keeps its legacy hit punch.");
        }

        [Test]
        public void AttackAnimationIsBlockedAfterDeathLatch()
        {
            // Bug 3 regression: a dead enemy must never generate attack presentation.
            Assert.IsFalse(
                EnemyAnimationBridge.ShouldPlayAttackAnimation(true, true),
                "A death-latched enemy must not trigger the attack animation.");
            Assert.IsFalse(
                EnemyAnimationBridge.ShouldPlayAttackAnimation(false, false),
                "Without an Animator there is nothing to trigger.");
            Assert.IsTrue(
                EnemyAnimationBridge.ShouldPlayAttackAnimation(false, true),
                "A live enemy with an Animator must be able to trigger the attack animation.");
        }

        [Test]
        public void DeathPresentationDurationCoversTheDeathClipWithMargin()
        {
            // Bug 3 regression: the old 1.15 s constant truncated the ~2.8-3.0 s death
            // clip. The presentation window must be clip length + margin.
            Assert.AreEqual(
                3.1f,
                EnemyVisualSetup.ComputeDeathPresentationDuration(2.8f, 0.3f),
                0.0001f,
                "The window must be the clip length plus the margin.");

            Assert.AreEqual(
                2.9f,
                EnemyVisualSetup.ComputeDeathPresentationDuration(2.8f, 0f),
                0.0001f,
                "A zero margin must clamp to the safe minimum (0.1).");

            AnimationClip death = EnemyAnimationSetup.ResolveClip(EnemyAnimationSetup.DeathFbxPath);

            if (death != null)
            {
                float window = EnemyVisualSetup.ComputeDeathPresentationDuration(
                    death.length, EnemyVisualSetup.DeathPresentationMarginSeconds);

                Assert.GreaterOrEqual(window, death.length + 0.1f,
                    "The configured death window must outlast the imported death clip " +
                    $"({death.length:0.00} s) so the animation visibly completes.");
            }
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
