using System.Collections.Generic;
using System.Reflection;
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
    ///   - the production prefab itself still resolves in the project;
    ///   - QA fix #10: the corpse grounding is driven by the Death clip's
    ///     normalized time (smoothstep blend from the standing Y to the stable
    ///     serialized final grounded Y between 0.25 and 0.85), never moves upward,
    ///     stops changing after the target is reached, and the obsolete
    ///     measurement/refinement/MoveTowards settle system is removed entirely.
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
        public void ProductionGroundingOffsetIsTheDeterministicFbxDerivedValue()
        {
            // QA fix #2 regression: the grounding offset must be the DETERMINISTIC,
            // FBX-derived value (-1.005), never a runtime bounds measurement. The
            // bounds-based approach read the vendor prefab's editor/reference pose and
            // produced a wrong offset (-0.628 in the QA run), leaving the animated
            // feet floating. The FBX truth: lowest mesh vertex at +0.536 cm above the
            // model root, enemy root at y=1, lane at y=0 -> offset = -(1 + 0.00536).
            Assert.AreEqual(
                -1.005f,
                EnemyVisualSetup.ProductionVisualGroundingOffsetY,
                0.001f,
                "The production grounding offset must stay the deterministic FBX-derived " +
                "value. Changing it must come with a re-measured FBX rationale.");
        }

        [Test]
        public void HitFlashCooldown_GatesTheFlashRate()
        {
            // QA fix #2 regression: the flash strobe under auto-fire (restart per hit)
            // read as body vibration. The pure gate must allow the first flash, block
            // flashes inside the cooldown window, and allow again after it.
            Assert.IsTrue(
                ZombieController.ShouldStartHitFlash(10f, 10f),
                "At exactly the allowed time the flash may start.");

            float nextAllowed = 10f + 0.35f;

            Assert.IsFalse(
                ZombieController.ShouldStartHitFlash(10.3f, nextAllowed),
                "Inside the cooldown window the flash must be suppressed.");
            Assert.IsTrue(
                ZombieController.ShouldStartHitFlash(10.4f, nextAllowed),
                "After the cooldown the next flash may start.");
        }

        [Test]
        public void DeathStateNameIsSharedBetweenBridgeAndController()
        {
            // QA fix #2 regression: the bridge's direct death crossfade targets the
            // state NAME hash, so the tool's Death state and the bridge constant must
            // never drift apart.
            Assert.AreEqual(
                "Death",
                EnemyAnimationBridge.DeathStateName,
                "The shared Death state name must remain 'Death'.");

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                EnemyAnimationSetup.ControllerPath);

            Assert.IsNotNull(controller, "Controller asset missing - run the rebuild tool.");

            AnimatorState deathState = FindState(
                controller.layers[0].stateMachine, EnemyAnimationBridge.DeathStateName);

            Assert.IsNotNull(deathState,
                "The controller's Death state must use the shared bridge name so the " +
                "direct crossfade targets it.");
        }

        // ============================================ QA fix #3 (death entry + materials)

        [Test]
        public void DirectDeathEntryTargetsTheLayerZeroDeathState()
        {
            // QA fix #3 regression: the bridge's deterministic death entry uses
            // Animator.Play(DeathStateHash, layer 0, time 0). The hash must resolve to
            // the shared Death state name and the Death state must live on the base
            // layer (layer 0), with the death clip assigned.
            Assert.AreEqual(
                Animator.StringToHash(EnemyAnimationBridge.DeathStateName),
                Animator.StringToHash("Death"),
                "The Death state hash must resolve from the shared state name.");

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                EnemyAnimationSetup.ControllerPath);

            Assert.IsNotNull(controller, "Controller asset missing - run the rebuild tool.");
            Assert.GreaterOrEqual(controller.layers.Length, 1,
                "The controller needs its base layer.");

            AnimatorStateMachine baseRoot = controller.layers[0].stateMachine;
            AnimatorState deathState = FindState(baseRoot, EnemyAnimationBridge.DeathStateName);
            Assert.IsNotNull(deathState,
                "The Death state must live on the BASE layer (layer 0) where the " +
                "bridge's Animator.Play targets it.");

            AnimationClip deathClip = EnemyAnimationSetup.ResolveClip(EnemyAnimationSetup.DeathFbxPath);
            Assert.IsNotNull(deathClip, "The zombie death clip must resolve.");
            Assert.AreEqual(deathClip, deathState.motion as AnimationClip,
                "The Death state must play the zombie death clip.");
        }

        // ============================================ QA fix #4 (full-path death entry)

        [Test]
        public void DeathStateFullPathHashIsSharedAndTargetsLayerZero()
        {
            // QA fix #4 regression: Animator.Play must target the FULL state path
            // ("Base Layer.Death"), not the bare short state name, and the base layer
            // is layer 0.
            Assert.AreEqual(
                "Base Layer.Death",
                EnemyAnimationBridge.DeathStateFullPath,
                "The shared death full path must remain 'Base Layer.Death'.");

            Assert.AreNotEqual(
                Animator.StringToHash(EnemyAnimationBridge.DeathStateName),
                Animator.StringToHash(EnemyAnimationBridge.DeathStateFullPath),
                "The short-name hash and the full-path hash must be DIFFERENT values - " +
                "using the short name with Animator.Play is exactly what could fail to " +
                "resolve the state.");

            Assert.AreEqual(0, EnemyAnimationBridge.DeathPlayLayer,
                "The deterministic death entry must target the base layer (layer 0).");
        }

        [Test]
        public void GeneratedControllerContainsTheExactDeathStatePath()
        {
            // The full path "Base Layer.Death" is well-formed only when the base
            // state machine is named 'Base Layer' and the Death state exists in it.
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                EnemyAnimationSetup.ControllerPath);

            Assert.IsNotNull(controller, "Controller asset missing - run the rebuild tool.");
            Assert.GreaterOrEqual(controller.layers.Length, 1,
                "The controller needs its base layer.");

            Assert.AreEqual(
                EnemyAnimationBridge.BaseLayerName,
                controller.layers[0].stateMachine.name,
                "The base layer's state machine must carry the shared 'Base Layer' name, " +
                "otherwise the full-path death hash cannot resolve.");

            Assert.IsNotNull(
                FindState(controller.layers[0].stateMachine, EnemyAnimationBridge.DeathStateName),
                "The Death state must exist on the base layer for the full path to resolve.");
        }

        [Test]
        public void DeathStatePathSurvivesControllerRebuild()
        {
            // The full path must survive a rebuild + forced reimport so the bridge's
            // Animator.Play hash keeps resolving after every setup run.
            Assert.IsTrue(EnemyAnimationSetup.RebuildController(),
                "The enemy controller rebuild must succeed.");

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(
                EnemyAnimationSetup.ControllerPath, ImportAssetOptions.ForceUpdate);

            AnimatorController reloaded = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                EnemyAnimationSetup.ControllerPath);

            Assert.IsNotNull(reloaded, "The controller must reacquire after reimport.");
            Assert.GreaterOrEqual(reloaded.layers.Length, 1,
                "The base layer must survive the rebuild.");

            Assert.AreEqual(
                EnemyAnimationBridge.BaseLayerName,
                reloaded.layers[0].stateMachine.name,
                "The base layer name must survive the rebuild so the full death path stays valid.");

            Assert.IsNotNull(
                FindState(reloaded.layers[0].stateMachine, EnemyAnimationBridge.DeathStateName),
                "The Death state must survive the rebuild.");
        }

        // ============================================ QA fix #5 (one-shot death)

        [Test]
        public void ShouldStartDeathPresentation_IsOneShot()
        {
            // The gate must allow exactly one presentation start per death: latched +
            // not-yet-started allows it; any other combination refuses. A repeated
            // Animator.Play would restart the death clip at its first frames - the
            // observed jerking symptom.
            Assert.IsTrue(
                EnemyAnimationBridge.ShouldStartDeathPresentation(true, false),
                "A latched enemy whose death presentation has not started yet must start it.");

            Assert.IsFalse(
                EnemyAnimationBridge.ShouldStartDeathPresentation(true, true),
                "A latched enemy whose death presentation has already started must NOT " +
                "restart it (this is the one-shot guarantee).");

            Assert.IsFalse(
                EnemyAnimationBridge.ShouldStartDeathPresentation(false, false),
                "An enemy that is not death-latched must never start the death presentation.");
        }

        [Test]
        public void DeathClipIsNonLooping()
        {
            // The imported zombie death clip must NOT loop - a looping clip replays
            // its first frames, which reads as the reported jerking/restart symptom.
            AnimationClip death = EnemyAnimationSetup.ResolveClip(EnemyAnimationSetup.DeathFbxPath);

            Assert.IsNotNull(death, "The zombie death clip must resolve.");

            Assert.IsFalse(death.isLooping,
                "The zombie death clip must be non-looping. If this fails, uncheck " +
                "Loop Time on the death clip's import settings and re-save.");

            Assert.Greater(death.length, 1f,
                "The death clip must be the full ~2.8-3.0 s take, not a truncated fragment.");
        }

        [Test]
        public void DeathStateIsUnitSpeedAndNeverSelfReEntrant()
        {
            // The Death state must play once at exactly 1x, with no exits and no
            // self-re-entrant AnyState transition - any of those would restart the
            // clip at its first frames.
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                EnemyAnimationSetup.ControllerPath);

            Assert.IsNotNull(controller, "Controller asset missing - run the rebuild tool.");

            AnimatorStateMachine baseRoot = controller.layers[0].stateMachine;
            AnimatorState deathState = FindState(baseRoot, EnemyAnimationBridge.DeathStateName);

            Assert.IsNotNull(deathState, "Death state missing.");

            Assert.AreEqual(1f, deathState.speed, 0.0001f,
                "The Death state must play at exactly 1x speed.");
            Assert.IsFalse(deathState.speedParameterActive,
                "The Death state must not be driven by any speed parameter.");
            Assert.IsEmpty(deathState.transitions,
                "The Death state must have no exits.");

            foreach (AnimatorStateTransition anyStateTransition in baseRoot.anyStateTransitions)
            {
                if (anyStateTransition.destinationState == deathState)
                {
                    Assert.IsFalse(anyStateTransition.canTransitionToSelf,
                        "The AnyState->Death transition must never re-enter the Death " +
                        "state - that would restart the death clip.");
                }
            }
        }

        // ============================================ QA fix #6 (death grounding)

        [Test]
        public void DeathGroundingAppliesOnlyAfterTheDeathLatch()
        {
            // The death-only grounding correction must never touch the production
            // visual while the enemy lives - the standing offset stays authoritative
            // for Idle/Walk/Attack.
            Assert.IsTrue(
                EnemyAnimationBridge.ShouldApplyDeathGrounding(true),
                "After the death latch the grounding correction must be allowed.");
            Assert.IsFalse(
                EnemyAnimationBridge.ShouldApplyDeathGrounding(false),
                "While the enemy lives, no death grounding may be applied - the " +
                "standing ProductionVisual offset is untouched.");
        }

        [Test]
        public void DeathGroundingBlendStartsOnlyAfterTheConfiguredStartTime()
        {
            // QA fix #10: the lowering must NOT begin while the corpse is still
            // standing - before the configured start point (normalized 0.25) the
            // standing Y is retained unchanged.
            Assert.IsFalse(
                EnemyAnimationBridge.ShouldStartDeathGroundingBlend(0.24f, 0.25f),
                "Before the configured start point the grounding blend must not run.");
            Assert.IsTrue(
                EnemyAnimationBridge.ShouldStartDeathGroundingBlend(0.25f, 0.25f),
                "At the configured start point the blend must begin.");
            Assert.AreEqual(
                0f,
                EnemyAnimationBridge.ComputeDeathGroundingProgress(0.24f, 0.25f, 0.85f),
                0.0001f,
                "Progress must be exactly 0 before the window opens (standing Y retained).");
            Assert.AreEqual(
                0f,
                EnemyAnimationBridge.ComputeDeathGroundingProgress(0.25f, 0.25f, 0.85f),
                0.0001f,
                "Progress must be exactly 0 at the window start.");
        }

        [Test]
        public void DeathGroundingTargetIsAWorldSpaceDeltaAppliedToVisualLocalY()
        {
            // QA fix #7 (kept by QA fix #10 as SETUP-TIME-ONLY math): the stable
            // final grounded Y is computed in ONE consistent space - world. The
            // world-space delta (groundWorldY - lowestCorpseWorldY) is added to the
            // visual's current local Y (valid because the parent chain is identity).
            Assert.AreEqual(
                -1.355f,
                EnemyAnimationBridge.ComputeDeathGroundedTargetLocalY(-1.005f, 0.35f, 0f),
                0.0001f,
                "A corpse whose lowest point sits at world y=0.35 must lower the visual " +
                "by 0.35 (from -1.005 to -1.355) to rest on the lane at world y=0.");

            // No correction needed when the corpse is already on the lane.
            Assert.AreEqual(
                -1.005f,
                EnemyAnimationBridge.ComputeDeathGroundedTargetLocalY(-1.005f, 0f, 0f),
                0.0001f,
                "A corpse already resting on the lane must keep the current visual Y.");
        }

        [Test]
        public void DeathGroundingCorrectionPlacesTheCorpseOnTheLaneWithinTolerance()
        {
            // Invariant (QA fix #7, kept by QA fix #10 for the SETUP-TIME
            // measurement): after applying the computed target, the measured lowest
            // world point must reach the ground. Since moving the visual by a
            // local-Y delta moves the corpse by the same world delta (identity
            // parent chain):
            //   lowestAfter = lowestBefore + (target - currentVisualY)
            // and that must equal groundWorldY within a tiny tolerance.
            float[,] cases =
            {
                { -1.005f, 0.62f, 0f },   // corpse floating 0.62 above the lane
                { -1.005f, 0.35f, 0f },   // corpse floating 0.35 above the lane
                { -1.005f, -0.12f, 0f },  // corpse sunk 0.12 below the lane
                { -0.8f, 0.4f, -0.2f },   // different root height/ground convention
            };

            for (int i = 0; i < cases.GetLength(0); i++)
            {
                float currentVisualY = cases[i, 0];
                float lowestWorldY = cases[i, 1];
                float groundWorldY = cases[i, 2];

                float target = EnemyAnimationBridge.ComputeDeathGroundedTargetLocalY(
                    currentVisualY, lowestWorldY, groundWorldY);

                float lowestAfter = lowestWorldY + (target - currentVisualY);

                Assert.AreEqual(
                    groundWorldY, lowestAfter, 1e-4f,
                    $"Case {i}: the corrected corpse's lowest point must reach the ground. " +
                    $"lowestBefore={lowestWorldY}, target={target}, lowestAfter={lowestAfter}.");
            }
        }

        [Test]
        public void FinalGroundedYIsReachedByTheConfiguredEndTime()
        {
            // QA fix #10: the blend must reach the stable final grounded Y at (or
            // before) the configured end point (0.85) - well before the clip-finish
            // gate at 0.999 - so the corpse already rests on the road when the
            // death animation ends. No correction may be needed after that.
            const float StandingY = -1.005f;
            const float FinalY = -1.5f;

            float progressAtEnd = EnemyAnimationBridge.ComputeDeathGroundingProgress(0.85f, 0.25f, 0.85f);
            Assert.AreEqual(1f, progressAtEnd, 0.0001f,
                "At the configured end point the grounding progress must be exactly 1.");

            Assert.IsTrue(
                EnemyAnimationBridge.IsDeathGroundingBlendComplete(progressAtEnd),
                "At the end point the blend must be complete.");

            Assert.AreEqual(
                FinalY,
                EnemyAnimationBridge.ComputeDeathGroundedVisualY(StandingY, FinalY, progressAtEnd),
                0.0001f,
                "At the end point the visual Y must equal the final grounded Y exactly.");

            // The grounding end point must sit BEFORE the clip-finish gate, so the
            // corpse is fully grounded while the animation is still playing.
            Assert.Less(
                0.85f, 0.999f,
                "The grounding must always complete inside the death animation, never after it.");
        }

        [Test]
        public void StandingProductionVisualOffsetRemainsUnchanged()
        {
            // QA fix #6/#7 must not change the standing grounding for Idle/Walk/Attack -
            // only a death-only additional correction may exist.
            Assert.AreEqual(
                -1.005f,
                EnemyVisualSetup.ProductionVisualGroundingOffsetY,
                0.001f,
                "The deterministic standing grounding offset must stay -1.005.");
        }

        // ============================================ QA fix #7 (collider lifecycle)

        [Test]
        public void ColliderStateCaptureAndApplyRoundTrips()
        {
            // The capture must record every collider's authored enabled state and the
            // apply must restore it exactly (death disables; reuse restores).
            GameObject holder = new GameObject("EnemyRoot");
            Collider first = holder.AddComponent<CapsuleCollider>();
            Collider second = holder.AddComponent<CapsuleCollider>();
            first.enabled = true;
            second.enabled = false;

            try
            {
                Collider[] colliders = holder.GetComponents<Collider>();
                bool[] states = EnemyAnimationBridge.CaptureColliderEnabledStates(colliders);

                Assert.AreEqual(2, states.Length, "Every collider must be captured.");
                Assert.IsTrue(states[0], "The enabled collider must be captured as enabled.");
                Assert.IsFalse(states[1], "The disabled collider must be captured as disabled.");

                // Death: disable everything.
                EnemyAnimationBridge.ApplyColliderEnabledStates(colliders, new[] { false, false });
                Assert.IsFalse(first.enabled, "Death must disable the enabled collider.");
                Assert.IsFalse(second.enabled, "Death must keep the disabled collider disabled.");

                // Reuse: restore the authored snapshot.
                EnemyAnimationBridge.ApplyColliderEnabledStates(colliders, states);
                Assert.IsTrue(first.enabled, "Reuse must restore the authored enabled state.");
                Assert.IsFalse(second.enabled, "Reuse must restore the authored disabled state.");
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void ColliderStateApplyGuardsMismatchedSizes()
        {
            // A length mismatch must be a safe no-op, never an out-of-range write.
            GameObject holder = new GameObject("EnemyRoot");
            Collider first = holder.AddComponent<CapsuleCollider>();
            first.enabled = true;

            try
            {
                Collider[] colliders = holder.GetComponents<Collider>();
                EnemyAnimationBridge.ApplyColliderEnabledStates(colliders, new[] { false, false });

                Assert.IsTrue(first.enabled,
                    "A mismatched state array must not modify any collider.");
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        // ============================================ QA fix #10 (death-time-driven grounding)

        [Test]
        public void DeathGroundingIsDrivenByTheDeathNormalizedTime()
        {
            // QA fix #10: the grounded Y is a pure function of the Death clip's
            // normalized time - a smoothstep remap over the [0.25, 0.85] window,
            // lerped between the standing Y (-1.005) and the stable final grounded
            // Y. No measurement, no target chasing, no time-based settle.
            const float StandingY = -1.005f;
            const float FinalY = -1.5f;

            var expectations = new[]
            {
                new { DeathT = 0.00f, Progress = 0.0f },
                new { DeathT = 0.25f, Progress = 0.0f },
                new { DeathT = 0.55f, Progress = 0.5f },   // window midpoint
                new { DeathT = 0.85f, Progress = 1.0f },
                new { DeathT = 0.95f, Progress = 1.0f },
                new { DeathT = 1.00f, Progress = 1.0f },
            };

            foreach (var expectation in expectations)
            {
                float progress = EnemyAnimationBridge.ComputeDeathGroundingProgress(
                    expectation.DeathT, 0.25f, 0.85f);
                float y = EnemyAnimationBridge.ComputeDeathGroundedVisualY(
                    StandingY, FinalY, progress);

                Assert.AreEqual(
                    expectation.Progress, progress, 1e-4f,
                    $"deathT={expectation.DeathT}: progress must be the smoothstep remap.");
                Assert.AreEqual(
                    Mathf.Lerp(StandingY, FinalY, expectation.Progress), y, 1e-4f,
                    $"deathT={expectation.DeathT}: the visual Y must lerp by the progress.");
            }

            // The computed Y is monotonic non-increasing across the whole clip.
            float previous = StandingY;
            for (int step = 0; step <= 100; step++)
            {
                float deathT = step / 100f;
                float progress = EnemyAnimationBridge.ComputeDeathGroundingProgress(
                    deathT, 0.25f, 0.85f);
                float y = EnemyAnimationBridge.ComputeDeathGroundedVisualY(StandingY, FinalY, progress);
                Assert.LessOrEqual(
                    y, previous + 1e-5f,
                    "The grounded Y must never rise as deathT advances.");
                previous = y;
            }
        }

        [Test]
        public void DeathGroundingSmoothstepEasesTheLoweringInAndOut()
        {
            // QA fix #10: smoothstep (3t^2 - 2t^3) eases the lowering in and out so
            // it merges with the fall animation instead of reading as a separate
            // linear/robotic correction.
            const float WindowStart = 0.25f;
            const float WindowEnd = 0.85f;

            float deathTEaseIn = WindowStart + (WindowEnd - WindowStart) * 0.25f;
            float deathTEaseOut = WindowStart + (WindowEnd - WindowStart) * 0.75f;

            float progressEaseIn = EnemyAnimationBridge.ComputeDeathGroundingProgress(
                deathTEaseIn, WindowStart, WindowEnd);
            float progressEaseOut = EnemyAnimationBridge.ComputeDeathGroundingProgress(
                deathTEaseOut, WindowStart, WindowEnd);

            Assert.Less(progressEaseIn, 0.25f,
                "Early in the window the smoothstep must lag the linear remap (ease-in).");
            Assert.Greater(progressEaseOut, 0.75f,
                "Late in the window the smoothstep must lead the linear remap (ease-out).");

            Assert.AreEqual(
                0.5f,
                EnemyAnimationBridge.ComputeDeathGroundingProgress(
                    WindowStart + (WindowEnd - WindowStart) * 0.5f, WindowStart, WindowEnd),
                1e-5f,
                "The window midpoint must map to smoothstep(0.5) = 0.5.");
        }

        [Test]
        public void DeathGroundingBlendNeverMovesTheVisualUpward()
        {
            // QA fix #10: the per-frame downward-only clamp discards ANY upward
            // motion - even for a misconfigured final Y above the standing Y, or an
            // animator restart that resets the normalized time after the corpse
            // already grounded.
            const float StandingY = -1.005f;
            const float FinalY = -1.5f;

            // Frame-by-frame simulation across the full clip.
            float currentY = StandingY;
            for (int step = 0; step <= 100; step++)
            {
                float deathT = step / 100f;
                float progress = EnemyAnimationBridge.ComputeDeathGroundingProgress(
                    deathT, 0.25f, 0.85f);
                float blended = EnemyAnimationBridge.ComputeDeathGroundedVisualY(
                    StandingY, FinalY, progress);
                float nextY = EnemyAnimationBridge.ClampDeathGroundingDownwardOnly(
                    currentY, blended);

                Assert.LessOrEqual(
                    nextY, currentY + 1e-5f,
                    $"Frame {step}: the visual Y must never move upward.");
                currentY = nextY;
            }

            Assert.AreEqual(FinalY, currentY, 0.0001f,
                "The simulated corpse must end exactly on the final grounded Y.");

            // Misconfigured final Y ABOVE the standing Y -> the clamp holds the
            // standing Y (no upward pop; the deactivation safety timeout still ends
            // the presentation).
            float misconfigured = EnemyAnimationBridge.ComputeDeathGroundedVisualY(
                StandingY, -0.6f, 1f);
            Assert.AreEqual(
                StandingY,
                EnemyAnimationBridge.ClampDeathGroundingDownwardOnly(StandingY, misconfigured),
                0.0001f,
                "An upward blended Y must be discarded - the visual stays at the standing Y.");

            // Restart (normalized time resets to 0 after the corpse already
            // grounded) -> the blend wants the standing Y again; the clamp keeps
            // the corpse down.
            float restartBlend = EnemyAnimationBridge.ComputeDeathGroundedVisualY(
                StandingY, FinalY, 0f);
            Assert.AreEqual(
                FinalY,
                EnemyAnimationBridge.ClampDeathGroundingDownwardOnly(FinalY, restartBlend),
                0.0001f,
                "A restart must never lift the already-grounded corpse.");
        }

        [Test]
        public void DeathGroundedVisualYStopsChangingAfterTheTargetIsReached()
        {
            // QA fix #10: after the blend completes, the computed Y equals the
            // stable final grounded Y on every subsequent frame - the corpse is
            // completely stationary for the tail of the clip, and the completion
            // tolerance is satisfied throughout (no snap, no settle, no drift).
            const float StandingY = -1.005f;
            const float FinalY = -1.5f;

            for (int step = 85; step <= 200; step++)
            {
                float deathT = step / 100f;
                float progress = EnemyAnimationBridge.ComputeDeathGroundingProgress(
                    deathT, 0.25f, 0.85f);
                float y = EnemyAnimationBridge.ComputeDeathGroundedVisualY(
                    StandingY, FinalY, progress);

                Assert.AreEqual(
                    FinalY, y, 1e-5f,
                    $"deathT={deathT}: after the end point the Y must stay at the final grounded Y.");
                Assert.IsTrue(
                    EnemyAnimationBridge.IsDeathGroundingComplete(y, FinalY, 0.015f),
                    "The corpse must count as grounded for the whole tail of the clip.");
            }
        }

        [Test]
        public void StandingYIsRetainedUntilTheGroundingWindowOpens()
        {
            // QA fix #10: during the early death clip (before normalized 0.25) the
            // standing visual Y (-1.005) is retained byte-for-byte - the fall is
            // still upright and no lowering may have started.
            const float StandingY = -1.005f;
            const float FinalY = -1.5f;

            for (int step = 0; step <= 25; step++)
            {
                float deathT = step / 100f;
                float progress = EnemyAnimationBridge.ComputeDeathGroundingProgress(
                    deathT, 0.25f, 0.85f);
                float y = EnemyAnimationBridge.ComputeDeathGroundedVisualY(
                    StandingY, FinalY, progress);

                Assert.AreEqual(
                    StandingY, y, 1e-5f,
                    $"deathT={deathT}: before the window opens the standing Y must be untouched.");
            }
        }

        [Test]
        public void BlendEndsExactlyOnTheFinalGroundedY()
        {
            // QA fix #10: the blend terminates EXACTLY on the stable final grounded
            // Y (lerp at progress 1) - no asymptotic approach, no snap step and no
            // post-animation settle is needed to finish the job.
            const float StandingY = -1.005f;
            const float FinalY = -1.5f;

            float yAtEnd = EnemyAnimationBridge.ComputeDeathGroundedVisualY(
                StandingY, FinalY, 1f);
            Assert.AreEqual(
                FinalY, yAtEnd, 0.0001f,
                "At progress 1 the Y must be exactly the final grounded Y.");
            Assert.IsTrue(
                EnemyAnimationBridge.IsDeathGroundingComplete(yAtEnd, FinalY, 0.015f),
                "The end-of-blend Y must satisfy the completion tolerance immediately.");
        }

        // ============================================ QA fix #9 (presentation completion)

        [Test]
        public void GroundingCompletionUsesTolerance()
        {
            // QA fix #10: "final grounded Y reached" is a tolerance check against
            // the STABLE serialized final grounded Y (-1.5 in these cases).
            Assert.IsTrue(
                EnemyAnimationBridge.IsDeathGroundingComplete(-1.49f, -1.5f, 0.015f),
                "Within tolerance the grounded pose must count as reached.");
            Assert.IsFalse(
                EnemyAnimationBridge.IsDeathGroundingComplete(-1.2f, -1.5f, 0.015f),
                "Outside tolerance the grounded pose must still be pending.");
            Assert.IsTrue(
                EnemyAnimationBridge.IsDeathGroundingComplete(-1.5f, -1.5f, 0.015f),
                "Exactly on the target the grounded pose is reached.");
        }

        [Test]
        public void DeathPresentationCompletesOnlyWhenAnimationAndGroundingAreBothDone()
        {
            Assert.IsTrue(
                EnemyAnimationBridge.ShouldCompleteDeathPresentation(true, true),
                "Animation finished AND grounding settled -> presentation complete.");
            Assert.IsFalse(
                EnemyAnimationBridge.ShouldCompleteDeathPresentation(true, false),
                "The presentation must NOT complete while the corpse is still settling " +
                "(the QA fix #9 disappearing-before-settle symptom).");
            Assert.IsFalse(
                EnemyAnimationBridge.ShouldCompleteDeathPresentation(false, true),
                "The presentation must NOT complete while the death clip is still playing.");
        }

        [Test]
        public void DeathPresentationHasNoPostAnimationSettlePath()
        {
            // QA fix #10: the obsolete measurement/refinement/MoveTowards settle
            // system must be GONE, not bypassed - only the death-time-driven blend
            // may remain. Reflection-pins the removed methods and fields so a
            // regression can never silently reintroduce the sinking.
            System.Type bridge = typeof(EnemyAnimationBridge);
            const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.Static | BindingFlags.Instance;

            string[] removedMethods =
            {
                "UpdateDeathGrounding",
                "RecomputeDeathGroundingTarget",
                "TryMeasureDeathPoseLowestWorldY",
                "ClampDeathGroundingTargetDownwardOnly",
                "ShouldMeasureDeathGrounding",
                "ShouldRefineDeathGrounding",
            };

            foreach (string methodName in removedMethods)
            {
                Assert.IsNull(
                    bridge.GetMethod(methodName, Any),
                    $"The obsolete settle API '{methodName}' must not exist anymore.");
            }

            string[] removedFields =
            {
                "_deathGroundingTargetY",
                "_deathGroundingMeasured",
                "_deathGroundingRefined",
                "_deathPoseBakeMesh",
                "useMeasuredDeathGrounding",
                "deathGroundingSampleNormalizedTime",
                "deathGroundingRefineNormalizedTime",
                "deathGroundingBlendDuration",
                "deathGroundingOffsetY",
            };

            foreach (string fieldName in removedFields)
            {
                Assert.IsNull(
                    bridge.GetField(fieldName, Any),
                    $"The obsolete settle field '{fieldName}' must not exist anymore.");
            }

            Assert.IsNotNull(
                bridge.GetField("deathGroundedVisualY", Any),
                "The stable serialized final grounded Y must exist on the bridge.");
        }

        [Test]
        public void DeathGroundedVisualYDefaultIsTheDocumentedFallback()
        {
            // QA fix #10: the serialized final grounded Y defaults to the documented
            // fallback constant (used only when the setup measurement is
            // unavailable) and must sit BELOW the standing offset so a default
            // value can never leave the corpse floating.
            GameObject holder = new GameObject("BridgeDefaultCheck");
            EnemyAnimationBridge bridge = holder.AddComponent<EnemyAnimationBridge>();

            try
            {
                System.Reflection.FieldInfo field = typeof(EnemyAnimationBridge).GetField(
                    "deathGroundedVisualY", BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.IsNotNull(field, "deathGroundedVisualY must exist on the bridge.");
                Assert.AreEqual(
                    EnemyAnimationBridge.FallbackDeathGroundedVisualY,
                    (float)field.GetValue(bridge),
                    0.0001f,
                    "The field default must be the documented fallback constant.");
                Assert.Less(
                    EnemyAnimationBridge.FallbackDeathGroundedVisualY,
                    EnemyVisualSetup.ProductionVisualGroundingOffsetY,
                    "The fallback final Y must sit below the standing offset (-1.005).");
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void DeathGroundingWindowCompletesBeforeTheClipFinishGate()
        {
            // QA fix #10: the grounding must ALWAYS finish inside the death
            // animation. The configured window (start 0.25 -> end 0.85) must be
            // ordered, non-zero and end below the clip-finish gate (0.999).
            Assert.Greater(
                EnemyVisualSetup.DeathGroundingStartNormalizedTime,
                0f,
                "The blend must not start at the very first frame (the fall is upright).");
            Assert.Less(
                EnemyVisualSetup.DeathGroundingStartNormalizedTime,
                EnemyVisualSetup.DeathGroundingEndNormalizedTime,
                "The grounding window must be ordered (start before end).");
            Assert.Less(
                EnemyVisualSetup.DeathGroundingEndNormalizedTime,
                0.999f,
                "The grounding end point must precede the clip-finish gate.");
        }

        [Test]
        public void DeactivationWaitsForThePresentationBridge()
        {
            // The deactivation wait decision: with a bridge present, the enemy stays
            // alive until the bridge completes (or the safety timeout), never on the
            // clip timer alone.
            Assert.IsFalse(
                ZombieController.ShouldEndDeathPresentationWait(true, false, 3.2f, 3.0f, 4f),
                "A bridge that has not reported completion must keep the corpse alive " +
                "past the clip timer - the settle must finish first.");

            Assert.IsTrue(
                ZombieController.ShouldEndDeathPresentationWait(true, true, 3.2f, 3.0f, 4f),
                "Once the bridge reports completion (after the clip timer), the wait ends.");

            Assert.IsTrue(
                ZombieController.ShouldEndDeathPresentationWait(true, false, 8f, 3.0f, 4f),
                "The safety timeout must end the wait even if the bridge never completes.");

            Assert.IsFalse(
                ZombieController.ShouldEndDeathPresentationWait(false, false, 0.1f, 0.38f, 4f),
                "Prototype fallback (no bridge) keeps the pre-1Q clip-timer behavior.");
            Assert.IsTrue(
                ZombieController.ShouldEndDeathPresentationWait(false, false, 0.4f, 0.38f, 4f),
                "Prototype fallback ends exactly on the clip timer.");
        }

        [Test]
        public void ProductionZombieMaterialsUseTheUrpLitShader()
        {
            // QA fix #3 (Bug 2) regression: the production zombie must render with
            // Operation Outbreak-owned URP materials. The vendor materials use the
            // built-in Standard shader, which renders magenta under URP and was only
            // "fixed" by uncommitted local conversions on the old PC.
            Material material01 = AssetDatabase.LoadAssetAtPath<Material>(
                EnemyVisualSetup.OoZombieMaterial01Path);
            Material material02 = AssetDatabase.LoadAssetAtPath<Material>(
                EnemyVisualSetup.OoZombieMaterial02Path);

            Assert.IsNotNull(material01,
                "OO_Zombie_01 URP material is missing - a clean clone would render magenta.");
            Assert.IsNotNull(material02,
                "OO_Zombie_02 URP material is missing - a clean clone would render magenta.");

            Assert.AreEqual("Universal Render Pipeline/Lit", material01.shader.name,
                "OO_Zombie_01 must use the URP/Lit shader.");
            Assert.AreEqual("Universal Render Pipeline/Lit", material02.shader.name,
                "OO_Zombie_02 must use the URP/Lit shader.");
        }

        [Test]
        public void ProductionZombieMaterialsReferenceTheVendorTextures()
        {
            // QA fix #3 (Bug 2) regression: the OO URP materials must carry the vendor
            // base-color textures so the zombie looks correct, not just non-magenta.
            Material material01 = AssetDatabase.LoadAssetAtPath<Material>(
                EnemyVisualSetup.OoZombieMaterial01Path);
            Material material02 = AssetDatabase.LoadAssetAtPath<Material>(
                EnemyVisualSetup.OoZombieMaterial02Path);

            Assert.IsNotNull(material01, "OO_Zombie_01 is missing.");
            Assert.IsNotNull(material02, "OO_Zombie_02 is missing.");

            Assert.IsNotNull(material01.GetTexture("_MainTex"),
                "OO_Zombie_01 must reference the vendor base-color texture.");
            Assert.IsNotNull(material02.GetTexture("_MainTex"),
                "OO_Zombie_02 must reference the vendor base-color texture.");
        }

        [Test]
        public void SelectProductionMaterialForRenderer_IsDeterministic()
        {
            // The material selection rule: current vendor material names containing
            // "02" map to OO_Zombie_02 (the second vendor material), everything else
            // falls back to OO_Zombie_01.
            Assert.AreEqual(
                EnemyVisualSetup.OoZombieMaterial02Path,
                EnemyVisualSetup.SelectProductionMaterialForRenderer("StylizedZombie_02_Mat"),
                "The vendor '02' material must map to OO_Zombie_02.");
            Assert.AreEqual(
                EnemyVisualSetup.OoZombieMaterial01Path,
                EnemyVisualSetup.SelectProductionMaterialForRenderer("StylizedZombie_01_Mat"),
                "The vendor '01' material must map to OO_Zombie_01.");
            Assert.AreEqual(
                EnemyVisualSetup.OoZombieMaterial01Path,
                EnemyVisualSetup.SelectProductionMaterialForRenderer(""),
                "An unknown/empty material name must fall back to OO_Zombie_01.");
            Assert.AreEqual(
                EnemyVisualSetup.OoZombieMaterial01Path,
                EnemyVisualSetup.SelectProductionMaterialForRenderer(null),
                "A null material name must fall back to OO_Zombie_01.");
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
