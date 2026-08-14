using System;
using System.Collections;
using UnityEngine;

namespace OperationOutbreak.Feedback
{
    /// <summary>
    /// Milestone 1P - drives a short "pop" scale envelope on a temporary combat feedback
    /// visual and then hands the GameObject back to its pool (or any completion callback).
    ///
    /// Presentation only, by construction:
    ///   - It only ever writes its own transform's local scale, so it can never move or
    ///     scale anything authoritative (no enemy transform, no player, no collider maths).
    ///   - It is attached to pooled visuals that have NO collider, so it can never touch
    ///     physics or hit detection.
    ///   - The envelope is a sine pulse (starts and ends at exactly the base scale, peaks
    ///     at 1 + pulseStrength at the midpoint), which reads as a quick "pop" from the
    ///     portrait gameplay camera without needing textures, particles or lights.
    ///
    /// This is the replaceable glue between pooled prototype visuals and any future
    /// production VFX: when final muzzle/impact effects arrive, the gameplay code that
    /// calls CombatFeedback stays untouched and only the factory behind each pool changes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FeedbackVisualLifecycle : MonoBehaviour
    {
        private Action<GameObject> _onFinished;
        private Coroutine _run;

        /// <summary>
        /// Pure pulse curve: 1 at progress 0, peaks at 1 + pulseStrength at progress 0.5,
        /// back to 1 at progress 1. Progress is clamped and a negative strength is clamped
        /// to zero so the visual can never shrink below its base scale.
        /// </summary>
        public static float ComputePulseScale(float progress, float pulseStrength)
        {
            float clampedProgress = Mathf.Clamp01(progress);
            float strength = Mathf.Max(0f, pulseStrength);
            return 1f + strength * Mathf.Sin(clampedProgress * Mathf.PI);
        }

        /// <summary>
        /// Starts (or restarts) the envelope. Restarting stops any envelope still running,
        /// so a reused pooled visual can never have two envelopes fighting over its scale.
        /// </summary>
        public void Play(float duration, float pulseStrength, Action<GameObject> onFinished)
        {
            Stop();
            _onFinished = onFinished;
            _run = StartCoroutine(RunEnvelope(duration, pulseStrength));
        }

        private void Stop()
        {
            if (_run != null)
            {
                StopCoroutine(_run);
                _run = null;
            }

            _onFinished = null;
        }

        private IEnumerator RunEnvelope(float duration, float pulseStrength)
        {
            float safeDuration = Mathf.Max(0.02f, duration);
            Vector3 baseScale = transform.localScale;
            float elapsed = 0f;

            while (elapsed < safeDuration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / safeDuration);
                transform.localScale = baseScale * ComputePulseScale(progress, pulseStrength);
                yield return null;
            }

            // Leave the visual exactly as the factory built it so the pool can reuse it.
            transform.localScale = baseScale;

            Action<GameObject> onFinished = _onFinished;
            _onFinished = null;
            onFinished?.Invoke(gameObject);
        }
    }
}
