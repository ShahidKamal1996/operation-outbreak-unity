using System.Collections.Generic;
using OperationOutbreak.Story;
using UnityEngine;

namespace OperationOutbreak.Cinematic
{
    /// <summary>
    /// Milestone 1Z.1B — the opening exterior helicopter flyover cinematic controller.
    /// Owns ONLY cinematic sequence state, flight progression, camera activation, and a clean
    /// transition hook. Does NOT own gameplay, enemies, objectives, or environment construction.
    ///
    /// QA Fix #5: Acquires EXCLUSIVE presentation ownership at Awake() by disabling the
    /// MissionStoryDirector (which auto-starts the Mission 01 interior sequence in its OnEnable).
    /// This guarantees the exterior flyover is the ONLY visible presentation during its ~10 s.
    /// The director is restored on OnDestroy for the future interior milestone.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OpeningCinematicController : MonoBehaviour
    {
        public enum Phase { Inactive, ExteriorFlyover, AwaitingInteriorTransition, Complete }

        [Header("Flight")]
        [SerializeField] private Transform flightRoot;
        [SerializeField] private Transform[] flightPathPoints;
        [SerializeField] private float duration = 10f;

        [Header("Camera")]
        [SerializeField] private Camera exteriorCamera;
        [Tooltip("Local-space trailing offset from the helicopter. Behind = -Z, above = +Y, side = +/-X.")]
        [SerializeField] private Vector3 cameraOffset = new Vector3(6f, 4f, -16f);
        [SerializeField] private float cameraFollowDamp = 3f;
        [SerializeField] private float cameraFov = 48f;
        [Tooltip("Moving focus target that the camera looks at. Should be parented to the helicopter.")]
        [SerializeField] private Transform cameraFocusTarget;

        [Header("Micro-motion")]
        [SerializeField] private Transform helicopterVisual;
        [Tooltip("Yaw correction for the imported model (0 = model faces +Z, 180 = corrected).")]
        [SerializeField] private float modelYawOffset = 180f;
        [SerializeField] private float bobAmplitude = 0.12f;
        [SerializeField] private float bobFrequency = 1.8f;
        [SerializeField] private float tiltDegrees = 1.5f;

        [Header("Gameplay Visual Isolation")]
        [Tooltip("Name(s) of gameplay visual roots to hide during the exterior cinematic.")]
        [SerializeField] private string[] gameplayVisualNames = { "Player" };

        [Header("Auto")]
        [Tooltip("If true, the exterior flyover starts automatically when the scene enters Play mode.")]
        [SerializeField] private bool autoStartOnPlay = true;

        public Phase CurrentPhase { get; private set; } = Phase.Inactive;

        /// <summary>Raised when the exterior flyover reaches its end and is awaiting the interior transition.</summary>
        public event System.Action OnExteriorComplete;

        private float _elapsed;
        private Vector3 _cameraPos;
        private Quaternion _cameraRot;
        private Camera _disabledMainCamera;
        private bool _mainCameraWasEnabled;
        private readonly List<GameObject> _hiddenGameplayObjects = new List<GameObject>();

        // Presentation ownership tracking.
        private Behaviour _suppressedDirector;
        private bool _directorWasEnabled;

        /// <summary>True when the exterior camera component is enabled and rendering.</summary>
        public bool IsExteriorCameraEnabled => exteriorCamera != null && exteriorCamera.enabled;

        /// <summary>True when the MissionStoryDirector has been suppressed by this controller.</summary>
        public bool HasSuppressedDirector => _suppressedDirector != null;

        private void Awake()
        {
            // Acquire exclusive presentation ownership BEFORE any OnEnable can fire.
            // MissionStoryDirector.OnEnable auto-starts the Mission 01 interior sequence
            // (fade overlay, interior camera, subtitles) which would cover the exterior flyover.
            AcquirePresentationOwnership();
        }

        /// <summary>
        /// Disables competing story/interior presentation systems so the exterior flyover
        /// has exclusive visual ownership. Safe and reversible.
        /// </summary>
        public void AcquirePresentationOwnership()
        {
            // 1. Suppress MissionStoryDirector so it cannot auto-start the interior sequence.
            var director = Object.FindAnyObjectByType<MissionStoryDirector>();
            if (director != null && director.enabled)
            {
                _suppressedDirector = director;
                _directorWasEnabled = director.enabled;
                director.enabled = false;
                Debug.Log("[OPENING CINEMATIC] Suppressed MissionStoryDirector (prevented interior auto-start).");
            }

            // 2. Stop any already-running story sequence (in case OnEnable fired first).
            var runner = Object.FindAnyObjectByType<StorySequenceRunner>();
            if (runner != null && runner.IsRunning)
            {
                runner.Skip();
                Debug.Log("[OPENING CINEMATIC] Stopped running story sequence runner.");
            }

            // 3. Clear any fade overlay that may have been created.
            var fade = Object.FindAnyObjectByType<StoryFadeController>();
            if (fade != null)
            {
                fade.ClearInstant();
                Debug.Log("[OPENING CINEMATIC] Cleared story fade overlay.");
            }

            // 4. Hide any active subtitle.
            var subtitle = Object.FindAnyObjectByType<SubtitleController>();
            if (subtitle != null)
            {
                subtitle.Hide();
            }

            Debug.Log("[OPENING CINEMATIC] Exterior presentation ownership acquired.");
        }

        /// <summary>Restores any suppressed story systems (for the future interior milestone).</summary>
        public void ReleasePresentationOwnership()
        {
            if (_suppressedDirector != null && _directorWasEnabled)
            {
                _suppressedDirector.enabled = true;
                Debug.Log("[OPENING CINEMATIC] Restored MissionStoryDirector for interior transition.");
            }
            _suppressedDirector = null;
        }

        private void OnEnable()
        {
            if (autoStartOnPlay && Application.isPlaying && CurrentPhase == Phase.Inactive)
                StartExteriorFlyover();
        }

        /// <summary>Public hook to begin the exterior flyover manually.</summary>
        public void StartExteriorFlyover()
        {
            if (CurrentPhase != Phase.Inactive) return;

            Debug.Log("[OPENING CINEMATIC] Exterior flyover requested.");

            // Fail-safe: validate the exterior camera BEFORE touching the gameplay camera.
            if (!ValidateCinematicSetup())
            {
                Debug.LogError("[OPENING CINEMATIC] Setup validation failed — aborting. Gameplay camera preserved.");
                return;
            }

            // Enable the exterior camera COMPONENT first (builder created it disabled).
            exteriorCamera.enabled = true;
            exteriorCamera.fieldOfView = cameraFov;
            _cameraPos = ComputeCameraTarget(0f);
            _cameraRot = ComputeCameraLook(0f);
            exteriorCamera.transform.position = _cameraPos;
            exteriorCamera.transform.rotation = _cameraRot;

            CurrentPhase = Phase.ExteriorFlyover;
            _elapsed = 0f;

            // Only now disable the gameplay Main Camera (exterior camera is confirmed rendering).
            var main = Camera.main;
            if (main != null && main != exteriorCamera)
            {
                _disabledMainCamera = main;
                _mainCameraWasEnabled = main.enabled;
                main.enabled = false;
                Debug.Log("[OPENING CINEMATIC] Disabled gameplay Main Camera: " + main.name);
            }

            // Temporarily hide the gameplay player visual so it does not appear in the cinematic.
            HideGameplayVisuals();

            // Diagnostics: report helicopter renderer state.
            if (helicopterVisual != null)
            {
                int count = 0;
                Bounds bounds = new Bounds();
                bool first = true;
                foreach (var r in helicopterVisual.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || !r.enabled) continue;
                    count++;
                    if (first) { bounds = r.bounds; first = false; }
                    else bounds.Encapsulate(r.bounds);
                }
                Debug.Log($"[OPENING CINEMATIC] Copter_2 renderers: {count}, bounds size: {bounds.size}");
            }

            Debug.Log("[OPENING CINEMATIC] Exterior flyover started. ExteriorCamera enabled="
                      + exteriorCamera.enabled + ", activeInHierarchy=" + exteriorCamera.gameObject.activeInHierarchy);
        }

        /// <summary>
        /// Validates that the cinematic has all required elements before starting.
        /// </summary>
        public bool ValidateCinematicSetup()
        {
            if (exteriorCamera == null || !exteriorCamera.gameObject.activeInHierarchy)
            {
                Debug.LogError("[OPENING CINEMATIC] Exterior camera is null or inactive.");
                return false;
            }

            if (helicopterVisual == null)
            {
                Debug.LogError("[OPENING CINEMATIC] Helicopter visual is null.");
                return false;
            }

            // Verify the helicopter has at least one enabled renderer with non-zero bounds.
            bool foundRenderer = false;
            foreach (var r in helicopterVisual.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;
                if (r.bounds.size.magnitude > 0.01f) { foundRenderer = true; break; }
            }
            if (!foundRenderer)
            {
                Debug.LogError("[OPENING CINEMATIC] No enabled helicopter renderer with non-zero bounds found.");
                return false;
            }

            return true;
        }

        private void HideGameplayVisuals()
        {
            _hiddenGameplayObjects.Clear();
            foreach (string name in gameplayVisualNames)
            {
                var go = GameObject.Find(name);
                if (go == null) continue;

                // Record and hide all renderers on this object hierarchy.
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.enabled)
                    {
                        r.enabled = false;
                        _hiddenGameplayObjects.Add(go);
                    }
                }
            }
        }

        private void RestoreGameplayVisuals()
        {
            var restored = new HashSet<GameObject>();
            foreach (var go in _hiddenGameplayObjects)
            {
                if (go == null || restored.Contains(go)) continue;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    r.enabled = true;
                restored.Add(go);
            }
            _hiddenGameplayObjects.Clear();
        }

        private void OnDestroy()
        {
            // Safety: restore the gameplay Main Camera + visuals + story director.
            if (_disabledMainCamera != null && _mainCameraWasEnabled)
                _disabledMainCamera.enabled = true;
            RestoreGameplayVisuals();
            ReleasePresentationOwnership();
        }

        private void Update()
        {
            if (CurrentPhase != Phase.ExteriorFlyover) return;

            _elapsed += Time.unscaledDeltaTime;
            float raw = Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, duration));
            float eased = raw * raw * (3f - 2f * raw); // smoothstep for accel/decel

            // --- helicopter flight ---
            if (flightRoot != null && flightPathPoints != null && flightPathPoints.Length >= 2)
            {
                Vector3 pos = SamplePath(eased);
                flightRoot.position = pos;

                Vector3 aheadPos = SamplePath(Mathf.Min(1f, eased + 0.015f));
                Vector3 dir = aheadPos - pos;
                if (dir.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
                    flightRoot.rotation = Quaternion.Slerp(flightRoot.rotation, targetRot,
                        Time.unscaledDeltaTime * 4f);
                }

                if (helicopterVisual != null)
                {
                    float t = Time.unscaledTime;
                    helicopterVisual.localPosition = new Vector3(0f, Mathf.Sin(t * bobFrequency) * bobAmplitude, 0f);
                    helicopterVisual.localRotation = Quaternion.Euler(
                        Mathf.Sin(t * bobFrequency * 0.7f) * tiltDegrees,
                        modelYawOffset,
                        Mathf.Sin(t * bobFrequency * 0.5f) * tiltDegrees);
                }
            }

            // --- camera damped follow ---
            if (exteriorCamera != null)
            {
                Vector3 targetPos = ComputeCameraTarget(eased);
                float lerpFactor = Mathf.Clamp01(cameraFollowDamp * Time.unscaledDeltaTime);
                _cameraPos = Vector3.Lerp(_cameraPos, targetPos, lerpFactor);
                exteriorCamera.transform.position = _cameraPos;

                Quaternion targetLook = ComputeCameraLook(eased);
                _cameraRot = Quaternion.Slerp(_cameraRot, targetLook, lerpFactor);
                exteriorCamera.transform.rotation = _cameraRot;
            }

            // --- completion ---
            if (raw >= 1f)
            {
                CurrentPhase = Phase.AwaitingInteriorTransition;
                OnExteriorComplete?.Invoke();
                Debug.Log("[OPENING CINEMATIC] Exterior flyover complete — awaiting interior transition.");
            }
        }

        private Vector3 ComputeCameraTarget(float t)
        {
            if (flightRoot == null) return transform.position;
            return flightRoot.position + flightRoot.rotation * cameraOffset;
        }

        private Quaternion ComputeCameraLook(float t)
        {
            Vector3 lookAt = cameraFocusTarget != null ? cameraFocusTarget.position : flightRoot.position;
            return Quaternion.LookRotation(lookAt - _cameraPos, Vector3.up);
        }

        // ---- Catmull-Rom path sampler ----

        private Vector3 SamplePath(float t)
        {
            int n = flightPathPoints.Length;
            if (n == 0) return transform.position;
            if (n == 1) return flightPathPoints[0].position;

            float segT = t * (n - 1);
            int seg = Mathf.Clamp(Mathf.FloorToInt(segT), 0, n - 2);
            float localT = segT - seg;

            Vector3 p0 = flightPathPoints[Mathf.Max(0, seg - 1)].position;
            Vector3 p1 = flightPathPoints[seg].position;
            Vector3 p2 = flightPathPoints[seg + 1].position;
            Vector3 p3 = flightPathPoints[Mathf.Min(n - 1, seg + 2)].position;

            return CatmullRom(p0, p1, p2, p3, localT);
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
