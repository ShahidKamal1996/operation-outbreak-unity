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
    /// QA fix #12 - the tests now also pin the LAYERED SHOOTING architecture that fixed
    /// the frozen-locomotion-while-firing bug: locomotion stays on the Base Layer, the
    /// shoot animation lives on a dedicated upper-body masked layer, and the mask keeps
    /// pelvis/hips and both legs on the Base Layer.
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

            Assert.IsNotNull(root.defaultState, "The controller needs a default state.");
            Assert.AreEqual(
                ToonSoldierAnimationSetup.NeutralStanceState, root.defaultState.name,
                "The soldier must start in NeutralStance.");
        }

        // ------------------------------------------------------------ QA fix #12 layering

        [Test]
        public void BaseLayerCarriesNoGunplayState_ShootLayerDoes()
        {
            // The full-body shoot clip used to live on the base layer, which froze the
            // locomotion legs while firing. It must now live only on the shoot layer.
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                ToonSoldierAnimationSetup.ControllerPath);

            Assert.IsNotNull(controller, "Controller asset missing - run the rebuild tool.");
            Assert.GreaterOrEqual(controller.layers.Length, 2,
                "The controller needs its dedicated shoot layer (QA fix #12).");

            AnimatorStateMachine baseRoot = controller.layers[0].stateMachine;
            Assert.IsNull(
                FindState(baseRoot, ToonSoldierAnimationSetup.GunplayState),
                "Gunplay must NOT be on the base layer anymore - a full-body shoot state " +
                "there is exactly what froze locomotion while firing.");

            AnimationClip shoot = ToonSoldierAnimationSetup.ResolveClip(
                ToonSoldierAnimationSetup.ShootFbxPath);
            Assert.IsNotNull(shoot, "No clip resolved from the shoot FBX.");

            AnimatorState shootGunplay = FindState(
                controller.layers[1].stateMachine, ToonSoldierAnimationSetup.GunplayState);
            Assert.IsNotNull(shootGunplay, "Gunplay state missing from the shoot layer.");
            Assert.AreEqual(shoot, shootGunplay.motion as AnimationClip,
                "The shoot layer Gunplay must play the assault_combat_shoot clip.");
        }

        [Test]
        public void ShootLayerIsUpperBodyMaskedWithEmptyDefaultState()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                ToonSoldierAnimationSetup.ControllerPath);

            Assert.IsNotNull(controller, "Controller asset missing - run the rebuild tool.");
            Assert.GreaterOrEqual(controller.layers.Length, 2,
                "The controller needs its dedicated shoot layer (QA fix #12).");

            AnimatorControllerLayer shootLayer = controller.layers[1];
            Assert.AreEqual(
                ToonSoldierAnimationSetup.ShootLayerName, shootLayer.name,
                "Layer 1 must be the Shoot Layer.");
            Assert.AreEqual(
                AnimatorLayerBlendingMode.Override, shootLayer.blendingMode,
                "The shoot layer must override with blending.");
            Assert.GreaterOrEqual(shootLayer.defaultWeight, 0.99f,
                "The shoot layer must be fully weighted so firing always shows.");

            Assert.IsNotNull(shootLayer.avatarMask,
                "The shoot layer needs its upper-body AvatarMask - without it the full-body " +
                "shoot clip overrides locomotion again.");

            AnimatorStateMachine shootMachine = shootLayer.stateMachine;
            Assert.IsNotNull(shootMachine.defaultState, "The shoot layer needs a default state.");
            Assert.IsNull(shootMachine.defaultState.motion,
                "The shoot layer's default state must be Empty (no motion) so the base-layer " +
                "pose shows through when not firing.");
        }

        [Test]
        public void ShootMaskIncludesUpperBodyButExcludesHipsAndLegs()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                ToonSoldierAnimationSetup.ControllerPath);

            Assert.IsNotNull(controller, "Controller asset missing - run the rebuild tool.");
            Assert.GreaterOrEqual(controller.layers.Length, 2,
                "The controller needs its dedicated shoot layer (QA fix #12).");

            AvatarMask mask = controller.layers[1].avatarMask;
            Assert.IsNotNull(mask, "Shoot layer mask missing.");

            Assert.IsTrue(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body),
                "The mask must include the torso so the shoot pose reaches the chest.");
            Assert.IsTrue(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Head),
                "The mask must include the head.");
            Assert.IsTrue(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm),
                "The mask must include the left arm.");
            Assert.IsTrue(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm),
                "The mask must include the right arm.");

            Assert.IsFalse(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg),
                "The mask must exclude the left leg - legs stay on the base layer.");
            Assert.IsFalse(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg),
                "The mask must exclude the right leg - legs stay on the base layer.");

            // QA fix #12A - Unity's AvatarMask.GetTransformActive takes a mask INDEX,
            // not a bone-name string. Resolve the "Hips" path to its mask index; when
            // the mask carries that path it must be inactive, and when it does not
            // carry the path there is nothing to exclude.
            int hipsIndex = FindMaskTransformIndex(mask, "Hips");

            if (hipsIndex >= 0)
            {
                Assert.IsFalse(mask.GetTransformActive(hipsIndex),
                    "The mask must exclude the hips so the pelvis keeps the base-layer pose.");
            }
        }

        [Test]
        public void ShootLayerReturnsToEmptyAfterFiring()
        {
            // The Gunplay state must have an exit-time transition back to Empty so the
            // upper body blends back to the base-layer pose when firing stops.
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                ToonSoldierAnimationSetup.ControllerPath);

            Assert.IsNotNull(controller, "Controller asset missing - run the rebuild tool.");
            Assert.GreaterOrEqual(controller.layers.Length, 2,
                "The controller needs its dedicated shoot layer (QA fix #12).");

            AnimatorStateMachine shootMachine = controller.layers[1].stateMachine;
            AnimatorState gunplay = FindState(
                shootMachine, ToonSoldierAnimationSetup.GunplayState);
            Assert.IsNotNull(gunplay, "Gunplay state missing from the shoot layer.");

            AnimatorState empty = FindState(
                shootMachine, ToonSoldierAnimationSetup.EmptyStateName);
            Assert.IsNotNull(empty, "Empty state missing from the shoot layer.");

            bool hasExitToEmpty = false;
            foreach (AnimatorStateTransition transition in gunplay.transitions)
            {
                if (transition.destinationState == empty && transition.hasExitTime)
                {
                    hasExitToEmpty = true;
                    break;
                }
            }

            Assert.IsTrue(hasExitToEmpty,
                "Gunplay needs an exit-time transition back to Empty so the upper body " +
                "blends back to locomotion when firing stops.");
        }

        [Test]
        public void RebuiltShootLayerStateMachinePersistsAcrossAssetReimport()
        {
            // QA fix #12B - persistence regression: the Shoot Layer's state machine is
            // a separate Unity object and must be serialized as a CONTROLLER SUB-ASSET.
            // The pre-fix tool left it in memory only, so the serialized layer kept
            // m_StateMachine: {fileID: 0} and Unity logged "Statemachine for layer
            // 'Shoot Layer' is missing" after every editor/domain reload or scene
            // restore. This test rebuilds, saves, FORCES A REIMPORT (the asset is
            // re-read from disk, not the in-memory object), reacquires it and asserts
            // the layer, its state machine, its states and its mask all survive.
            Assert.IsTrue(ToonSoldierAnimationSetup.RebuildController(),
                "The rebuild must succeed.");

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(
                ToonSoldierAnimationSetup.ControllerPath, ImportAssetOptions.ForceUpdate);

            AnimatorController reloaded = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                ToonSoldierAnimationSetup.ControllerPath);

            Assert.IsNotNull(reloaded, "The controller must reacquire after reimport.");
            Assert.GreaterOrEqual(reloaded.layers.Length, 2,
                "Both layers must survive the reimport.");

            AnimatorControllerLayer shootLayer = reloaded.layers[1];
            Assert.IsNotNull(shootLayer.stateMachine,
                "The Shoot Layer's state machine must survive reimport - it must be " +
                "persisted as a controller sub-asset, not an in-memory object.");
            Assert.IsNotNull(shootLayer.avatarMask,
                "The Shoot Layer's upper-body mask must survive reimport.");

            AnimatorStateMachine shootMachine = shootLayer.stateMachine;
            Assert.IsNotNull(
                FindState(shootMachine, ToonSoldierAnimationSetup.EmptyStateName),
                "The Empty default state must survive reimport.");
            Assert.IsNotNull(
                FindState(shootMachine, ToonSoldierAnimationSetup.GunplayState),
                "The Gunplay state must survive reimport.");
            Assert.IsNotNull(shootMachine.defaultState,
                "The shoot layer needs its default state after reimport.");
            Assert.AreEqual(
                ToonSoldierAnimationSetup.EmptyStateName,
                shootMachine.defaultState.name,
                "The shoot layer's default state must remain Empty after reimport.");

            // The persisted nested machine must be listed among the asset's sub-objects.
            bool nestedPersisted = false;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(
                         ToonSoldierAnimationSetup.ControllerPath))
            {
                if (asset is AnimatorStateMachine machine &&
                    machine.name == ToonSoldierAnimationSetup.ShootLayerName)
                {
                    nestedPersisted = true;
                    break;
                }
            }

            Assert.IsTrue(nestedPersisted,
                "The Shoot Layer state machine must be a persisted sub-asset of the " +
                "controller (AssetDatabase.AddObjectToAsset), otherwise the serialized " +
                "asset cannot restore it after a reload.");
        }

        [Test]
        public void CommittedControllerCarriesValidPersistedShootLayerWithoutRebuild()
        {
            // QA fix #5 - regression guard for the COMMITTED asset. The 1P.5 failure
            // ("Statemachine for layer 'Shoot Layer' is missing") came from a
            // committed controller whose Shoot Layer serialized m_StateMachine:
            // {fileID: 0}. The 12B rebuild test above masks that class of bug because
            // it REGENERATES the controller in memory before asserting - a broken
            // committed asset would be silently rebuilt and the suite would pass.
            // This test deliberately never rebuilds: it loads the committed asset as
            // a fresh clone would, proves every structural requirement against it,
            // then forces a save/reimport/reload round-trip and proves the Shoot
            // Layer state machine survives on disk. A future commit that reintroduces
            // the {fileID: 0} defect fails here instead of shipping to QA.
            const string path = ToonSoldierAnimationSetup.ControllerPath;

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            Assert.IsNotNull(controller, "The committed ToonSoldier_Player controller must exist.");

            // 1. Both layers and the Base Layer state machine must resolve.
            Assert.GreaterOrEqual(controller.layers.Length, 2,
                "The committed controller must carry its two layers (Base + Shoot).");
            AnimatorControllerLayer baseLayer = controller.layers[0];
            Assert.AreEqual("Base Layer", baseLayer.name,
                "Layer 0 must be the Base Layer.");
            Assert.IsNotNull(baseLayer.stateMachine,
                "The committed Base Layer state machine must resolve.");

            // 2. The Shoot Layer state machine must resolve on the committed asset.
            AnimatorControllerLayer shootLayer = controller.layers[1];
            Assert.AreEqual(ToonSoldierAnimationSetup.ShootLayerName, shootLayer.name,
                "Layer 1 must be the Shoot Layer.");
            Assert.IsNotNull(shootLayer.stateMachine,
                "The committed Shoot Layer state machine must resolve - a missing " +
                "m_StateMachine reference here is the exact 1P.5 regression.");

            // 3. The Shoot Layer state machine must be a PERSISTED controller
            //    sub-asset (listed in the asset's sub-objects), not an in-memory
            //    object that only exists for the lifetime of the editor session.
            bool persistedSubAsset = false;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is AnimatorStateMachine machine &&
                    machine.name == ToonSoldierAnimationSetup.ShootLayerName)
                {
                    persistedSubAsset = true;
                    break;
                }
            }

            Assert.IsTrue(persistedSubAsset,
                "The committed controller must list the Shoot Layer state machine as a " +
                "persisted sub-asset (a missing listing means a fresh clone reloads a " +
                "null state machine).");

            // 4. Upper-body AvatarMask on the Shoot Layer.
            Assert.IsNotNull(shootLayer.avatarMask,
                "The committed Shoot Layer must carry its upper-body AvatarMask.");
            AvatarMask mask = shootLayer.avatarMask;
            Assert.IsTrue(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body),
                "The committed mask must include the torso.");
            Assert.IsTrue(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm),
                "The committed mask must include the right arm.");
            Assert.IsTrue(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm),
                "The committed mask must include the left arm.");
            Assert.IsFalse(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg),
                "The committed mask must exclude the right leg.");
            Assert.IsFalse(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg),
                "The committed mask must exclude the left leg.");

            // 5. Shoot state + motion + Empty default state.
            AnimatorStateMachine shootMachine = shootLayer.stateMachine;
            AnimatorState gunplay = FindState(
                shootMachine, ToonSoldierAnimationSetup.GunplayState);
            Assert.IsNotNull(gunplay,
                "The committed Shoot Layer needs its Gunplay state.");

            AnimationClip shootClip = ToonSoldierAnimationSetup.ResolveClip(
                ToonSoldierAnimationSetup.ShootFbxPath);
            Assert.IsNotNull(shootClip, "The shoot clip must resolve.");
            Assert.AreEqual(shootClip, gunplay.motion as AnimationClip,
                "The committed Gunplay state must play the assault_combat_shoot clip.");

            Assert.IsNotNull(shootMachine.defaultState,
                "The committed Shoot Layer needs a default state.");
            Assert.AreEqual(ToonSoldierAnimationSetup.EmptyStateName,
                shootMachine.defaultState.name,
                "The committed Shoot Layer default state must be Empty.");

            // 6. Locomotion state + BlendTree remain valid on the Base Layer.
            AnimatorState locomotion = FindState(
                baseLayer.stateMachine, ToonSoldierAnimationSetup.LocomotionState);
            Assert.IsNotNull(locomotion,
                "The committed Base Layer needs its Locomotion state.");
            BlendTree tree = locomotion.motion as BlendTree;
            Assert.IsNotNull(tree,
                "The committed Locomotion motion must be a BlendTree.");
            Assert.AreEqual(2, tree.children.Length,
                "The committed locomotion blend tree needs its idle + run children.");

            // 7. Save/reimport/reload round-trip WITHOUT a rebuild: the asset is
            //    re-read from disk (what Unity does on scene/project restoration)
            //    and the Shoot Layer state machine must still resolve.
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            AnimatorController reloaded = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            Assert.IsNotNull(reloaded,
                "The committed controller must reacquire after a forced reimport.");
            Assert.GreaterOrEqual(reloaded.layers.Length, 2,
                "Both layers must survive the reimport.");
            Assert.IsNotNull(reloaded.layers[1].stateMachine,
                "The Shoot Layer state machine must survive save/reimport/reload without " +
                "a rebuild - if this fails the committed asset cannot restore it.");
            Assert.IsNotNull(reloaded.layers[1].avatarMask,
                "The Shoot Layer's upper-body mask must survive the reimport.");
            Assert.IsNotNull(reloaded.layers[0].stateMachine,
                "The Base Layer state machine must survive the reimport.");
        }

        [Test]
        public void CommittedShootLayerStateMachineIsAPersistedSubAsset()
        {
            // QA fix #11C — replaced the QA fix #11B numeric-fileID test (< 2^31) which was proven
            // unsafe: hand-editing local file IDs corrupts Unity's PPtr ownership. The real
            // invariant is that the Shoot Layer stateMachine is a GENUINE PERSISTED sub-asset of the
            // controller — non-null AND listed among the controller's sub-assets — surviving a cold
            // load with no rebuild required.
            const string path = ToonSoldierAnimationSetup.ControllerPath;
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            Assert.IsNotNull(controller, "Committed controller must exist.");
            Assert.GreaterOrEqual(controller.layers.Length, 2, "Controller must have 2 layers.");

            AnimatorStateMachine shootSM = controller.layers[1].stateMachine;
            Assert.IsNotNull(shootSM,
                "Shoot Layer stateMachine must resolve on cold load (the recurring 1P.5 regression is a null here).");

            bool found = false;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is AnimatorStateMachine sm && sm.GetInstanceID() == shootSM.GetInstanceID())
                {
                    found = true;
                    break;
                }
            }
            Assert.IsTrue(found,
                "Shoot Layer stateMachine must be a genuine persisted sub-asset of the controller " +
                "(not an in-memory-only object that vanishes on reload).");
        }

        /// <summary>
        /// QA fix #12A - resolves a mask transform path to its index (the index is the
        /// only addressing form Unity's AvatarMask transform APIs accept).
        /// </summary>
        private static int FindMaskTransformIndex(AvatarMask mask, string path)
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
