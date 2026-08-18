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
        public void DeathPoseMeasurementWaitsForTheLateClipThreshold()
        {
            // The measurement must sample the LATE death pose (the lying corpse), never
            // the standing pose at the clip start.
            Assert.IsFalse(
                EnemyAnimationBridge.ShouldMeasureDeathGrounding(0.1f, 0.9f),
                "Early in the clip the pose is still standing - no measurement yet.");
            Assert.IsTrue(
                EnemyAnimationBridge.ShouldMeasureDeathGrounding(0.9f, 0.9f),
                "At the sample threshold the near-final pose may be measured.");
            Assert.IsTrue(
                EnemyAnimationBridge.ShouldMeasureDeathGrounding(0.95f, 0.9f),
                "Past the threshold the measurement must still be allowed.");
        }

        [Test]
        public void DeathGroundingTargetIsAWorldSpaceDeltaAppliedToVisualLocalY()
        {
            // QA fix #7: the target is computed in ONE consistent space - world. The
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
            // Invariant: after applying the computed target, the measured lowest world
            // point must reach the ground. Since moving the visual by a local-Y delta
            // moves the corpse by the same world delta (identity parent chain):
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
        public void DeathGroundingRefinementWaitsForClipCompletion()
        {
            // QA fix #7: the final refinement re-measures the true resting pose only
            // at clip completion - the fall still moves slightly between the first
            // sample and the end, which is what left the corpse floating.
            Assert.IsFalse(
                EnemyAnimationBridge.ShouldRefineDeathGrounding(0.95f, 0.99f),
                "Before the clip has completed, no refinement measurement.");
            Assert.IsTrue(
                EnemyAnimationBridge.ShouldRefineDeathGrounding(0.99f, 0.99f),
                "At the completion threshold the refinement must run.");
            Assert.IsTrue(
                EnemyAnimationBridge.ShouldRefineDeathGrounding(1f, 0.99f),
                "At full completion the refinement must run.");
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

        // ============================================ QA fix #8 (downward-only settle)

        [Test]
        public void DeathGroundingTargetNeverRaisesTheVisual()
        {
            // The monotonic rule: the clamped target may never exceed the standing
            // ceiling nor a previous pass's target. A mid-fall measurement that would
            // LIFT the visual is discarded - only downward targets are accepted.
            const float StandingCeiling = -1.005f;

            // Computed target ABOVE the standing ceiling (mid-fall corpse) -> clamped
            // to the ceiling (no upward movement from standing).
            Assert.AreEqual(
                StandingCeiling,
                EnemyAnimationBridge.ClampDeathGroundingTargetDownwardOnly(
                    StandingCeiling, -0.6f, StandingCeiling),
                0.0001f,
                "A mid-fall sample that would lift the visual must be discarded.");

            // Computed target BELOW the ceiling (corpse near the ground) -> accepted.
            Assert.AreEqual(
                -1.355f,
                EnemyAnimationBridge.ClampDeathGroundingTargetDownwardOnly(
                    StandingCeiling, -1.355f, StandingCeiling),
                0.0001f,
                "A downward settle target must be accepted.");

            // Computed target below the ceiling but ABOVE an earlier target -> the
            // earlier (lower) target wins: refinement can never raise the visual.
            Assert.AreEqual(
                -1.355f,
                EnemyAnimationBridge.ClampDeathGroundingTargetDownwardOnly(
                    -1.355f, -1.2f, StandingCeiling),
                0.0001f,
                "A later pass must never raise the target above an earlier one.");
        }

        [Test]
        public void DeathGroundingRefinementCannotIncreaseLocalY()
        {
            // Two-pass simulation: pass 1 settles to t1; the clip-end refinement
            // computes t2 (which may be higher because the fall moved). The clamped
            // sequence must be monotonic non-increasing.
            const float StandingCeiling = -1.005f;

            float targetAfterPass1 = EnemyAnimationBridge.ClampDeathGroundingTargetDownwardOnly(
                StandingCeiling, -1.355f, StandingCeiling);

            float targetAfterRefinement = EnemyAnimationBridge.ClampDeathGroundingTargetDownwardOnly(
                targetAfterPass1, -1.2f, StandingCeiling);

            Assert.GreaterOrEqual(targetAfterPass1, targetAfterRefinement,
                "The refinement must never raise the target above the first pass.");
            Assert.AreEqual(-1.355f, targetAfterRefinement, 0.0001f,
                "The refinement must keep the lower first-pass target.");
        }

        [Test]
        public void StandingYIsUnchangedUntilADownwardTargetIsMeasured()
        {
            // Before any downward target exists, the grounded target must equal the
            // standing ceiling - meaning the settle loop has zero distance to travel
            // and the standing visual Y is untouched.
            const float StandingCeiling = -1.005f;

            float target = EnemyAnimationBridge.ClampDeathGroundingTargetDownwardOnly(
                StandingCeiling, -0.4f, StandingCeiling);

            Assert.AreEqual(StandingCeiling, target, 0.0001f,
                "With only upward measurements available, the target must stay at the " +
                "standing ceiling so no grounding movement happens at all.");
        }

        [Test]
        public void DownwardSettleStillReachesTheGround()
        {
            // The clamp must not prevent a genuine downward correction from reaching
            // the lane: a corpse floating above the road produces a lower target and
            // that target passes through unchanged.
            const float StandingCeiling = -1.005f;
            float currentVisualY = StandingCeiling;
            float computed = EnemyAnimationBridge.ComputeDeathGroundedTargetLocalY(
                currentVisualY, 0.35f, 0f); // corpse 0.35 above the lane

            float clamped = EnemyAnimationBridge.ClampDeathGroundingTargetDownwardOnly(
                StandingCeiling, computed, StandingCeiling);

            Assert.AreEqual(computed, clamped, 0.0001f,
                "The downward correction must pass through the clamp unchanged, so the " +
                "corpse still settles onto the road.");
            Assert.AreEqual(-1.355f, clamped, 0.0001f,
                "The settle must reach the ground (standing -1.005 - 0.35 = -1.355).");
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
