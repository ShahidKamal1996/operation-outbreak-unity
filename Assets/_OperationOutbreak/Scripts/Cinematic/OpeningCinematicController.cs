using OperationOutbreak.Story;
using UnityEngine;

namespace OperationOutbreak.Cinematic
{
    /// <summary>
    /// Milestone 1Z.1B — the opening exterior helicopter flyover cinematic controller.
    /// Owns ONLY cinematic sequence state, flight progression, camera activation, and a clean
    /// transition hook. Does NOT own gameplay, enemies, objectives, or environment construction.
    ///
    /// QA fix #8 architecture: Instead of suppressing MissionStoryDirector (which broke the
    /// Space-to-start flow and the Mission 01 lifecycle), this controller sets a start GATE
    /// on the director in Awake(). The director initializes normally (Awake, OnEnable,
    /// subscriptions) but does NOT auto-load the opening sequence while the gate is held.
    /// The gate is released on OnDestroy so the original flow is fully recoverable.
    ///
    /// DEVELOPMENT BYPASS: Set autoStartOnPlay = false in the Inspector. The gate is NOT held,
    /// the exterior flyover does NOT run, and MissionStoryDirector auto-starts the Mission 01
    /// opening exactly as before (including Space-to-start skip behavior).
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

        [Header("Auto")]
        [Tooltip("If true, the exterior flyover starts automatically and holds the Mission 01 opening gate. Set to FALSE to bypass the exterior cinematic and restore the original Mission 01 flow.")]
        [SerializeField] private bool autoStartOnPlay = true;

        [Header("Diagnostics")]
        [Tooltip("Log viewport projection of the helicopter every N seconds during the flyover.")]
        [SerializeField] private float diagnosticInterval = 2f;

        public Phase CurrentPhase { get; private set; } = Phase.Inactive;

        /// <summary>Raised when the exterior flyover reaches its end and is awaiting the interior transition.</summary>
        public event System.Action OnExteriorComplete;

        private float _elapsed;
        private float _diagTimer;
        private Vector3 _cameraPos;
        private Quaternion _cameraRot;
        private Camera _disabledMainCamera;
        private bool _mainCameraWasEnabled;
        private MissionStoryDirector _heldDirector;

        /// <summary>True when the exterior camera component is enabled and rendering.</summary>
        public bool IsExteriorCameraEnabled => exteriorCamera != null && exteriorCamera.enabled;

        /// <summary>True when this controller is holding the MissionStoryDirector's opening gate.</summary>
        public bool IsHoldingDirectorGate => _heldDirector != null && _heldDirector.HoldOpeningSequence;

        private void Awake()
        {
            // QA fix #8: Set the opening gate (NOT component disabling). This runs before any
            // OnEnable, so MissionStoryDirector.OnEnable will see the gate and defer its
            // auto-start. The director still initializes normally (Awake, subscriptions, refs).
            if (autoStartOnPlay && Application.isPlaying)
            {
                _heldDirector = Object.FindAnyObjectByType<MissionStoryDirector>();
                if (_heldDirector != null)
                {
                    _heldDirector.HoldOpeningSequence = true;
                    Debug.Log("[OPENING CINEMATIC] Holding MissionStoryDirector opening gate (exterior cinematic active).");
                }
            }
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

            if (!ValidateCinematicSetup())
            {
                Debug.LogError("[OPENING CINEMATIC] Setup validation failed — aborting. Gameplay camera preserved.");
                ReleaseGate();
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
            _diagTimer = 0f;

            // Disable the gameplay Main Camera (exterior camera is confirmed rendering).
            var main = Camera.main;
            if (main != null && main != exteriorCamera)
            {
                _disabledMainCamera = main;
                _mainCameraWasEnabled = main.enabled;
                main.enabled = false;
                Debug.Log("[OPENING CINEMATIC] Disabled gameplay Main Camera: " + main.name);
            }

            LogHelicopterDiagnostics();
            Debug.Log("[OPENING CINEMATIC] Exterior flyover started.");
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

        private void ReleaseGate()
        {
            if (_heldDirector != null)
            {
                _heldDirector.HoldOpeningSequence = false;
                _heldDirector = null;
                Debug.Log("[OPENING CINEMATIC] Released MissionStoryDirector opening gate.");
            }
        }

        private void OnDestroy()
        {
            // Safety: restore the gameplay Main Camera and release the gate.
            if (_disabledMainCamera != null && _mainCameraWasEnabled)
                _disabledMainCamera.enabled = true;
            ReleaseGate();
        }

        private void Update()
        {
            if (CurrentPhase != Phase.ExteriorFlyover) return;

            _elapsed += Time.unscaledDeltaTime;
            float raw = Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, duration));
            float eased = raw * raw * (3f - 2f * raw);

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

            // --- viewport projection diagnostics (periodic) ---
            _diagTimer += Time.unscaledDeltaTime;
            if (_diagTimer >= diagnosticInterval)
            {
                _diagTimer = 0f;
                LogViewportDiagnostics();
            }

            // --- completion ---
            if (raw >= 1f)
            {
                CurrentPhase = Phase.AwaitingInteriorTransition;
                OnExteriorComplete?.Invoke();
                Debug.Log("[OPENING CINEMATIC] Exterior flyover complete — awaiting interior transition. " +
                          "(Gate is still held; MissionStoryDirector will NOT auto-start until released by 1Z.1C.)");
            }
        }

        private void LogHelicopterDiagnostics()
        {
            if (helicopterVisual == null) return;
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
            Debug.Log($"[OPENING CINEMATIC] Helicopter renderers={count}, bounds center={bounds.center}, size={bounds.size}");
        }

        private void LogViewportDiagnostics()
        {
            if (exteriorCamera == null || helicopterVisual == null) return;

            // Compute combined helicopter bounds center.
            Bounds bounds = new Bounds();
            bool first = true;
            foreach (var r in helicopterVisual.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            if (first) return; // no renderers

            Vector3 center = bounds.center;
            Vector3 vp = exteriorCamera.WorldToViewportPoint(center);
            float dist = Vector3.Distance(exteriorCamera.transform.position, center);
            bool inFront = vp.z > 0f;
            bool inFrame = inFront && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;

            Debug.Log($"[OPENING CINEMATIC] Viewport: heliCenter={center}, vp=({vp.x:F2},{vp.y:F2},{vp.z:F2}), " +
                      $"dist={dist:F1}m, inFront={inFront}, inFrame={inFrame}, " +
                      $"camPos={exteriorCamera.transform.position}, camFwd={exteriorCamera.transform.forward}");

            if (!inFront)
                Debug.LogWarning("[OPENING CINEMATIC] WARNING: Helicopter is BEHIND the camera!");
            else if (!inFrame)
                Debug.LogWarning("[OPENING CINEMATIC] WARNING: Helicopter is outside the viewport frame!");
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
