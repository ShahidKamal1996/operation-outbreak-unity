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
    /// Milestone 1P QA fix - activation order: <see cref="Play"/> activates the GameObject
    /// BEFORE starting its envelope coroutine. Unity refuses to start a coroutine on an
    /// inactive GameObject ("Coroutine couldn't be started because the game object ...
    /// is inactive"), which was exactly the runtime regression found by manual QA on the
    /// first muzzle flash and hit spark of a session.
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
        private float _baseScale = 1f;

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
        ///
        /// Activation guarantee (Milestone 1P QA fix): the GameObject is activated here,
        /// before StartCoroutine, so a visual that was deactivated on pool release can
        /// never hit Unity's "Coroutine couldn't be started because the game object is
        /// inactive" error. This is belt-and-braces with the pool's acquire-time
        /// activation and the caller's explicit SetActive - all three keep the same
        /// invariant: no coroutine ever starts on an inactive feedback object.
        /// </summary>
        /// <param name="baseScale">Authored uniform scale the pulse is centred on. Callers
        /// configure the visual's localScale to the same value before calling Play, so the
        /// envelope never depends on captured transform state.</param>
        public void Play(float duration, float pulseStrength, float baseScale, Action<GameObject> onFinished)
        {
            Stop();

            // Activation must precede any coroutine start.
            gameObject.SetActive(true);

            _baseScale = Mathf.Max(0.001f, baseScale);
            _onFinished = onFinished;
            _run = StartCoroutine(RunEnvelope(duration, pulseStrength));
        }

        /// <summary>
        /// The single completion point of the envelope: stops the running envelope (if
        /// any), restores the authored base scale and raises the finish callback exactly
        /// once. The envelope coroutine calls this at its end; it is public so lifecycle
        /// tests can drive completion without Play Mode frames. A second call after
        /// completion does nothing, so a visual can never be double-returned to its pool.
        /// </summary>
        public void CompleteNow()
        {
            if (_run != null)
            {
                StopCoroutine(_run);
                _run = null;
            }

            // Leave the visual exactly as it was configured so the pool can reuse it.
            transform.localScale = Vector3.one * _baseScale;

            Action<GameObject> onFinished = _onFinished;
            _onFinished = null;
            onFinished?.Invoke(gameObject);
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
            float elapsed = 0f;

            while (elapsed < safeDuration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / safeDuration);
                transform.localScale = Vector3.one * _baseScale * ComputePulseScale(progress, pulseStrength);
                yield return null;
            }

            CompleteNow();
        }
    }
}
