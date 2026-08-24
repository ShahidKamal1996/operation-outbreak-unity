using UnityEngine;

namespace OperationOutbreak.Cinematic
{
    /// <summary>
    /// Milestone 1Z.1B — the opening exterior helicopter flyover cinematic controller.
    /// Owns ONLY cinematic sequence state, flight progression, camera activation, and a clean
    /// transition hook. Does NOT own gameplay, enemies, objectives, or environment construction.
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
        [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 5f, -14f);
        [SerializeField] private float cameraFollowDamp = 3f;
        [SerializeField] private float cameraFov = 45f;
        [SerializeField] private Transform cameraLookTarget;

        [Header("Micro-motion")]
        [SerializeField] private Transform helicopterVisual;
        [SerializeField] private float bobAmplitude = 0.12f;
        [SerializeField] private float bobFrequency = 1.8f;
        [SerializeField] private float tiltDegrees = 1.5f;

        [Header("Auto")]
        [Tooltip("If true, the exterior flyover starts automatically when the scene enters Play mode.")]
        [SerializeField] private bool autoStartOnPlay = true;

        public Phase CurrentPhase { get; private set; } = Phase.Inactive;

        /// <summary>Raised when the exterior flyover reaches its end and is awaiting the interior transition.</summary>
        public event System.Action OnExteriorComplete;

        private float _elapsed;
        private Vector3 _cameraPos;
        private Quaternion _cameraRot;

        private void OnEnable()
        {
            if (autoStartOnPlay && Application.isPlaying && CurrentPhase == Phase.Inactive)
                StartExteriorFlyover();
        }

        /// <summary>Public hook to begin the exterior flyover manually.</summary>
        public void StartExteriorFlyover()
        {
            if (CurrentPhase != Phase.Inactive) return;
            CurrentPhase = Phase.ExteriorFlyover;
            _elapsed = 0f;

            if (exteriorCamera != null)
            {
                exteriorCamera.gameObject.SetActive(true);
                exteriorCamera.fieldOfView = cameraFov;
                _cameraPos = ComputeCameraTarget(0f);
                _cameraRot = ComputeCameraLook(0f);
                exteriorCamera.transform.position = _cameraPos;
                exteriorCamera.transform.rotation = _cameraRot;
            }

            // Temporarily disable the gameplay Main Camera so the exterior camera is the only view.
            var main = Camera.main;
            if (main != null && main != exteriorCamera) main.enabled = false;

            Debug.Log("[OPENING CINEMATIC] Exterior flyover started.");
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

                // Face travel direction (damped).
                Vector3 aheadPos = SamplePath(Mathf.Min(1f, eased + 0.015f));
                Vector3 dir = aheadPos - pos;
                if (dir.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
                    flightRoot.rotation = Quaternion.Slerp(flightRoot.rotation, targetRot,
                        Time.unscaledDeltaTime * 4f);
                }

                // Micro-motion on the visual child (bob + tilt).
                if (helicopterVisual != null)
                {
                    float t = Time.unscaledTime;
                    helicopterVisual.localPosition = new Vector3(0f, Mathf.Sin(t * bobFrequency) * bobAmplitude, 0f);
                    helicopterVisual.localRotation = Quaternion.Euler(
                        Mathf.Sin(t * bobFrequency * 0.7f) * tiltDegrees,
                        0f,
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
            Vector3 lookAt = cameraLookTarget != null ? cameraLookTarget.position : flightRoot.position;
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
