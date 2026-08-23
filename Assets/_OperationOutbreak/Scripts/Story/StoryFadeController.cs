using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z.1 QA fix #8 — a lightweight cinematic fade overlay used ONLY to hide
    /// world-space camera jumps during Mission 01's opening (gameplay world y≈11 down to the
    /// interior rig at y=-300, and back). Without it the Main Camera visibly sweeps through
    /// geometry / the asphalt road for ~1s, producing the reported muddy "yellow/brown" opening
    /// frame and camera clipping through cabin walls.
    ///
    /// A single black Image on a ScreenSpaceOverlay canvas sits above every other canvas
    /// (sortingOrder 1000) so nothing peeks through during a transition. raycastTarget=false so
    /// it never eats input. During normal gameplay the overlay alpha is 0 (invisible).
    ///
    /// Usage pattern (see MissionStoryDirector):
    ///   opening  -> SetBlackInstant() ... position camera ... FadeFromBlack(d)
    ///   boundary -> FadeToBlack(d) ... swap worlds ... FadeFromBlack(d)
    ///   skip/end -> ClearInstant()
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StoryFadeController : MonoBehaviour
    {
        private const float HighSortingOrder = 1000f;

        private Canvas _canvas;
        private Image _image;
        private Coroutine _fade;

        /// <summary>True while the overlay is fully opaque (screen reads as black).</summary>
        public bool IsOpaque => _image != null && _image.color.a > 0.999f;

        private void Awake() => Build();

        private void Build()
        {
            if (_image != null) return; // idempotent — never build twice.

            _canvas = new GameObject("StoryFadeCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler))
                .GetComponent<Canvas>();
            _canvas.transform.SetParent(transform, false);
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = (int)HighSortingOrder;
            _canvas.pixelPerfect = false;

            CanvasScaler scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);

            _image = new GameObject("FadeImage", typeof(RectTransform), typeof(Image))
                .GetComponent<Image>();
            _image.transform.SetParent(_canvas.transform, false);
            RectTransform r = _image.rectTransform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
            _image.color = new Color(0f, 0f, 0f, 0f); // transparent by default
            _image.raycastTarget = false;

            // Starts hidden — no overlay during gameplay.
            _canvas.gameObject.SetActive(false);
        }

        /// <summary>
        /// Lifecycle-order-safe initialization. Awake builds the overlay in Play Mode, but a caller
        /// may invoke the public API the instant the component exists (e.g. an Edit Mode test calling
        /// SetBlackInstant right after AddComponent, where Awake has not yet run). Every public op
        /// therefore guarantees the Canvas/Image references exist before touching alpha. Idempotent.
        /// </summary>
        private void EnsureBuilt()
        {
            if (_image == null) Build();
        }

        /// <summary>Snaps the overlay to fully opaque black immediately.</summary>
        public void SetBlackInstant()
        {
            EnsureBuilt();
            StopFade();
            EnsureActive();
            SetAlpha(1f);
        }

        /// <summary>Fades the overlay to fully opaque black over <paramref name="duration"/> seconds.</summary>
        public void FadeToBlack(float duration)
        {
            EnsureBuilt();
            StopFade();
            EnsureActive();
            _fade = StartCoroutine(FadeRoutine(_image.color.a, 1f, duration));
        }

        /// <summary>Fades the overlay to fully transparent over <paramref name="duration"/> seconds.</summary>
        public void FadeFromBlack(float duration)
        {
            EnsureBuilt();
            StopFade();
            EnsureActive();
            _fade = StartCoroutine(FadeRoutine(_image.color.a, 0f, duration));
        }

        /// <summary>Instantly clears the overlay (alpha 0). Use on skip / sequence end.</summary>
        /// <remarks>
        /// Deliberately does NOT lazily build: clearing a never-built overlay is a safe no-op, and
        /// this keeps OnDisable (which routes here) from spawning GameObjects during teardown.
        /// It is fully null-safe.
        /// </remarks>
        public void ClearInstant()
        {
            StopFade();
            SetAlpha(0f);
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        private void EnsureActive()
        {
            if (_canvas != null && !_canvas.gameObject.activeSelf)
                _canvas.gameObject.SetActive(true);
        }

        private void SetAlpha(float a)
        {
            if (_image != null)
                _image.color = new Color(0f, 0f, 0f, Mathf.Clamp01(a));
        }

        private void StopFade()
        {
            if (_fade != null)
            {
                StopCoroutine(_fade);
                _fade = null;
            }
        }

        private IEnumerator FadeRoutine(float from, float to, float duration)
        {
            duration = Mathf.Max(0.01f, duration);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                SetAlpha(Mathf.Lerp(from, to, t * t * (3f - 2f * t))); // smoothstep
                yield return null;
            }

            SetAlpha(to);
            _fade = null;

            // Deactivate the overlay entirely once it is fully clear so it never costs anything
            // (or blocks anything visually) during normal gameplay.
            if (to <= 0.001f && _canvas != null)
                _canvas.gameObject.SetActive(false);
        }

        private void OnDisable() => ClearInstant();
    }
}
