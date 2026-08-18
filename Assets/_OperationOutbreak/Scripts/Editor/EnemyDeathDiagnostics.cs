#if UNITY_EDITOR
using OperationOutbreak.Enemies;
using UnityEditor;
using UnityEngine;

namespace OperationOutbreak.EditorTools
{
    /// <summary>
    /// Milestone 1Q QA fix #4 - isolation diagnostic: forces the SELECTED production
    /// zombie's Animator directly into the Death state WITHOUT any gameplay
    /// involvement (no ZombieController damage, no hit feedback, no Died event, no
    /// despawn logic). This answers exactly one question: can OO_BasicInfected +
    /// StylizedZombieAvatar visibly play the configured zombie death clip by itself?
    ///
    /// USAGE: in Play Mode, select a spawned production zombie (or its Animator), then
    /// run Tools > Operation Outbreak > Test Force Death On Selected Animator. The
    /// console log reports every precondition (enabled, controller, layers, avatar,
    /// clip) and the state the Animator reports after the forced entry, so a failure
    /// can be attributed to clip/state/avatar setup vs gameplay sequencing.
    /// </summary>
    public static class EnemyDeathDiagnostics
    {
        [MenuItem("Tools/Operation Outbreak/Test Force Death On Selected Animator")]
        public static void ForceDeathOnSelectedAnimator()
        {
            Animator animator = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInChildren<Animator>(true)
                : null;

            if (animator == null)
            {
                Debug.LogWarning(
                    "[1Q QA fix #4] Select a spawned production zombie (or any object " +
                    "under it) first - no Animator found on the selection.");
                return;
            }

            bool enabled = animator.enabled;
            bool hasController = animator.runtimeAnimatorController != null;
            string controllerName = hasController ? animator.runtimeAnimatorController.name : "MISSING";
            int layerCount = hasController && animator.layerCount > 0 ? animator.layerCount : 0;
            bool avatarValid = animator.avatar != null && animator.avatar.isValid;
            bool hasDeathClip = hasController && animator.HasState(
                EnemyAnimationBridge.DeathPlayLayer, Animator.StringToHash(EnemyAnimationBridge.DeathStateFullPath));

            Debug.Log(
                "[1Q QA fix #4] Death isolation diagnostics BEFORE forcing: " +
                $"enabled={enabled}, controller='{controllerName}', layerCount={layerCount}, " +
                $"avatarValid={avatarValid}, death state resolves={hasDeathClip}, " +
                $"full path='{EnemyAnimationBridge.DeathStateFullPath}'.", animator);

            if (!enabled || !hasController || layerCount == 0 || !hasDeathClip)
            {
                Debug.LogWarning(
                    "[1Q QA fix #4] A precondition failed - the death clip CANNOT play: " +
                    $"enabled={enabled}, controller='{controllerName}', layerCount={layerCount}, " +
                    $"deathStateResolves={hasDeathClip}. Fix the setup (re-run " +
                    "Set Up Basic Infected Production Visual) before debugging gameplay sequencing.",
                    animator);
                return;
            }

            EnemyAnimationBridge.ForceDeathPresentation(animator);

            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(EnemyAnimationBridge.DeathPlayLayer);
            string reportedState = stateInfo.IsName(EnemyAnimationBridge.DeathStateName)
                ? EnemyAnimationBridge.DeathStateName
                : "NOT Death";

            Debug.Log(
                "[1Q QA fix #4] Forced death entry issued via Animator.Play('" +
                EnemyAnimationBridge.DeathStateFullPath + "'). State machine reports: '" +
                reportedState + $"', normalizedTime={stateInfo.normalizedTime:0.00}. Watch the " +
                "Scene view: if the zombie now visibly plays the death clip, the " +
                "clip/state/avatar setup is GOOD and any remaining failure is gameplay " +
                "sequencing; if it does not animate, the clip/avatar binding is broken.",
                animator);
        }
    }
}
#endif
