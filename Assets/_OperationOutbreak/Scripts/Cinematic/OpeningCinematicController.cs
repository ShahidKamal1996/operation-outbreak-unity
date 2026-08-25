using OperationOutbreak.Story;
using UnityEngine;

namespace OperationOutbreak.Cinematic
{
    /// <summary>
    /// Milestone 1Z.1B — the opening exterior helicopter flyover cinematic controller.
    /// Owns ONLY cinematic sequence state, flight progression, camera activation, and a clean
    /// transition hook. Does NOT own gameplay, enemies, objectives, or environment construction.
    ///
    /// QA fix #10 architecture: this controller no longer PUSHES a flag onto MissionStoryDirector
    /// in Awake() (that was racy — if the director's OnEnable ran first it had already started the
    /// opening, and RAVEN ORTIZ dialogue played over the exterior flyover). Instead it DECLARES
    /// its intent via <see cref="RequestsStoryHold"/>, which is answered purely from serialized
    /// state and is therefore readable before this component initializes. The director polls that
    /// declaration itself, so the answer no longer depends on who wakes up first.
    ///
    /// It additionally acquires a process-wide OpeningStoryStartPermission token in Awake as a
    /// second, explicit layer, and releases it on teardown so the original flow is recoverable.
    ///
    /// DEVELOPMENT BYPASS: Set autoStartOnPlay = false in the Inspector. RequestsStoryHold is
    /// false, no permission token is acquired, the exterior flyover does NOT run, and
    /// MissionStoryDirector auto-starts the Mission 01 opening exactly as before (including
    /// Space-to-start skip behavior).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OpeningCinematicController : MonoBehaviour, IOpeningStoryHoldSource
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

        /// <summary>Set once the 1Z.1C handoff relinquishes this controller's claim on startup.</summary>
        private bool _storyHandoffReleased;

        /// <summary>True when the exterior camera component is enabled and rendering.</summary>
        public bool IsExteriorCameraEnabled => exteriorCamera != null && exteriorCamera.enabled;

        /// <summary>
        /// QA fix #10 — declares that this controller owns Mission 01 startup, so the opening
        /// story must stay deferred.
        ///
        /// RACE-CRITICAL: every term here is serialized state or Unity-managed activation state.
        /// Nothing is assigned in Awake/OnEnable/Start. Unity restores [SerializeField] values
        /// before any Awake runs, so MissionStoryDirector gets the correct answer no matter which
        /// component initializes first — which is exactly the ordering bug QA fix #8 could not
        /// close. Do NOT introduce a term here that is only initialized at runtime.
        /// </summary>
        public bool RequestsStoryHold =>
            autoStartOnPlay            // serialized intent
            && enabled                 // component not disabled
            && gameObject.activeInHierarchy
            && !_storyHandoffReleased; // 1Z.1C has not handed off yet

        /// <summary>
        /// True when this controller currently holds a permission token. Retained under the QA
        /// fix #8 name so existing gate assertions keep working; it now reports the authoritative
        /// process-wide token rather than a flag pushed onto the director.
        /// </summary>
        public bool IsHoldingDirectorGate => OpeningStoryStartPermission.HoldsToken(this);

        private void Awake()
        {
            // QA fix #10: acquire the authoritative permission token instead of reaching into
            // MissionStoryDirector. This is the explicit second layer — the director is already
            // protected by RequestsStoryHold even if this Awake has not run yet.
            AcquireStoryHoldIfNeeded();
        }

        private void AcquireStoryHoldIfNeeded()
        {
            if (!RequestsStoryHold) return;                          // bypass mode: never hold
            if (OpeningStoryStartPermission.HoldsToken(this)) return; // idempotent

            OpeningStoryStartPermission.Hold(this);
            Debug.Log("[OPENING CINEMATIC] Holding Mission 01 opening story start (exterior cinematic owns startup).");
        }

        private void OnEnable()
        {
            // Re-acquire if this controller was enabled after Awake already ran (or was disabled
            // and re-enabled). Idempotent, and a no-op in bypass mode.
            AcquireStoryHoldIfNeeded();

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

        private void OnDisable()
        {
            // A disabled controller no longer requests a hold (RequestsStoryHold folds in
            // `enabled`), so drop the token too and keep the two layers consistent. This does NOT
            // set _storyHandoffReleased — re-enabling re-acquires via OnEnable.
            if (OpeningStoryStartPermission.HoldsToken(this))
            {
                OpeningStoryStartPermission.Release(this);
                Debug.Log("[OPENING CINEMATIC] Story hold released (cinematic controller disabled).");
            }
        }

        /// <summary>
        /// QA fix #10 — permanently relinquishes this controller's claim on Mission 01 startup and
        /// drops its permission token. Idempotent.
        ///
        /// Setting _storyHandoffReleased (rather than only dropping the token) is essential: it
        /// makes RequestsStoryHold return false, so the director's scene scan also stops deferring.
        /// Dropping the token alone would leave the scan still reporting a hold.
        ///
        /// This is the 1Z.1C handoff entry point. It is NOT called when the 10-second flyover
        /// finishes — only on teardown or aborted startup.
        /// </summary>
        public void ReleaseStoryHandoff()
        {
            bool hadClaim = !_storyHandoffReleased;
            _storyHandoffReleased = true;

            if (OpeningStoryStartPermission.HoldsToken(this))
                OpeningStoryStartPermission.Release(this);

            if (hadClaim)
                Debug.Log("[OPENING CINEMATIC] Released Mission 01 opening story hold.");
        }

        /// <summary>Back-compat alias for the QA fix #8 gate release.</summary>
        private void ReleaseGate() => ReleaseStoryHandoff();

        /// <summary>
        /// Step 2A — THE global opening cinematic's handoff into the RAVEN/Kane interior story.
        ///
        /// This is the seam where 1Z.1C will continue the pipeline:
        ///     ExteriorFlyover -> AwaitingInteriorTransition -> [here] -> interior story -> gameplay
        ///
        /// It relinquishes this controller's hold and then asks MissionStoryDirector to EXECUTE the
        /// existing opening sequence asset. The story content is not duplicated — the director
        /// still owns the interior rig, fades and Kane swap that the sequence's cues drive; this
        /// controller only owns the DECISION to run it.
        ///
        /// NOT called automatically in this step: the 10-second flyover ends in
        /// AwaitingInteriorTransition and deliberately stays there until 1Z.1C wires this up.
        /// Returns true if the interior story actually started.
        /// </summary>
        public bool HandoffToInteriorStory()
        {
            ReleaseStoryHandoff();

            var director = Object.FindAnyObjectByType<MissionStoryDirector>();
            if (director == null)
            {
                Debug.LogWarning("[OPENING CINEMATIC] Handoff requested but no MissionStoryDirector " +
                                 "exists in the scene — cannot run the interior story.");
                return false;
            }

            bool started = director.StartOpeningStorySequence();
            if (started)
            {
                CurrentPhase = Phase.Complete;
                Debug.Log("[OPENING CINEMATIC] Handed off to the interior RAVEN/Kane story.");
            }
            return started;
        }

        private void OnDestroy()
        {
            // Safety: restore the gameplay Main Camera and release the story hold.
            if (_disabledMainCamera != null && _mainCameraWasEnabled)
                _disabledMainCamera.enabled = true;
            ReleaseStoryHandoff();
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
                          "(Story hold is still held by design; MissionStoryDirector will NOT auto-start " +
                          "until 1Z.1C performs the handoff.)");
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
