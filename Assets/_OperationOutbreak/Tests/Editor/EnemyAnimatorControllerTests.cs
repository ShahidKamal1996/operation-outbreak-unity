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
