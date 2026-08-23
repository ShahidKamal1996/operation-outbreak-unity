using System.Collections;
using OperationOutbreak.CameraRig;
using UnityEngine;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z.1 QA fix #7 — revised camera with interior/exterior cinematic shots,
    /// smooth transitions, subtle vibration during interior, and safe gameplay handoff.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StoryCameraController : MonoBehaviour
    {
        private Camera _camera;
        private PlayerFollowCamera _follow;
        private bool _cinematicActive;
        private Vector3 _gameplayPos;
        private Quaternion _gameplayRot;
        private float _gameplayFov;
        private Coroutine _transition;
        private bool _interiorMode;

        // Interior rig reference (set by MissionStoryDirector)
        private HelicopterInteriorRig _interiorRig;

        // Interior shot anchor positions (relative to the rig)
        private static readonly Vector3 InteriorKaneCam = new Vector3(1.5f, 1.4f, -0.5f);
        private static readonly Vector3 InteriorKaneCamClose = new Vector3(0.9f, 1.3f, -0.3f);
        private static readonly Vector3 InteriorFrontCam = new Vector3(0f, 1.3f, -1.5f);

        private void Awake()
        {
            _camera = FindAnyObjectByType<Camera>();
            _follow = FindAnyObjectByType<PlayerFollowCamera>();
            StoryCueEvents.CameraCue += OnCameraCue;
        }

        private void OnDestroy()
        {
            StoryCueEvents.CameraCue -= OnCameraCue;
            ReturnToGameplay();
        }

        private void OnDisable() => ReturnToGameplay();

        public void SetInteriorRig(HelicopterInteriorRig rig) => _interiorRig = rig;

        private void OnCameraCue(string cueId)
        {
            switch (cueId)
            {
                case "m01_interior_kane":
                    Debug.Log("[STORY CAMERA] Interior Kane establishing shot.");
                    EnterInteriorShot(InteriorKaneCam, 48f);
                    break;
                case "m01_interior_kane_close":
                    Debug.Log("[STORY CAMERA] Interior Kane closer angle.");
                    EnterInteriorShot(InteriorKaneCamClose, 45f);
                    break;
                case "m01_interior_front":
                    Debug.Log("[STORY CAMERA] Interior front/cockpit angle.");
                    EnterInteriorShot(InteriorFrontCam, 50f);
                    break;
                case "m01_exterior_approach":
                    Debug.Log("[STORY CAMERA] Exterior approach establishing.");
                    _interiorMode = false;
                    EnterCinematic(new Vector3(0f, 16f, -16f), Quaternion.Euler(42f, 0f, 0f), 50f);
                    break;
                case "m01_insertion":
                    Debug.Log("[STORY CAMERA] Insertion shot.");
                    EnterCinematic(new Vector3(-4f, 6f, -8f), Quaternion.Euler(25f, 25f, 0f), 46f);
                    break;
                case "establishing_shot":
                    EnterCinematic(new Vector3(0f, 16f, -16f), Quaternion.Euler(42f, 0f, 0f), 50f);
                    break;
                case "checkpoint_view":
                    EnterCinematic(new Vector3(0f, 12f, 28f), Quaternion.Euler(38f, 0f, 0f), 48f);
                    break;
                case "gameplay_handoff":
                    Debug.Log("[STORY CAMERA] Gameplay handoff — restoring camera.");
                    ReturnToGameplay();
                    break;
            }
        }

        private void EnterInteriorShot(Vector3 localOffset, float fov)
        {
            if (_follow != null) _follow.enabled = false;

            if (!_cinematicActive && _camera != null)
            {
                _gameplayPos = _camera.transform.position;
                _gameplayRot = _camera.transform.rotation;
                _gameplayFov = _camera.fieldOfView;
            }

            _cinematicActive = true;
            _interiorMode = true;

            if (_interiorRig != null)
            {
                Vector3 worldPos = _interiorRig.transform.position + localOffset;
                Quaternion worldRot = Quaternion.LookRotation(
                    (_interiorRig.transform.position + new Vector3(-0.9f, 0.8f, 0f)) - worldPos, Vector3.up);

                if (_transition != null) StopCoroutine(_transition);
                _transition = StartCoroutine(SmoothMove(worldPos, worldRot, fov, 1f));
            }
        }

        private void EnterCinematic(Vector3 targetPos, Quaternion targetRot, float targetFov)
        {
            if (_follow != null) _follow.enabled = false;

            if (!_cinematicActive && _camera != null)
            {
                _gameplayPos = _camera.transform.position;
                _gameplayRot = _camera.transform.rotation;
                _gameplayFov = _camera.fieldOfView;
            }

            _cinematicActive = true;
            _interiorMode = false;

            if (_transition != null) StopCoroutine(_transition);
            _transition = StartCoroutine(SmoothMove(targetPos, targetRot, targetFov, 1.2f));
        }

        private void ReturnToGameplay()
        {
            if (!_cinematicActive) return;
            _cinematicActive = false;
            _interiorMode = false;

            if (_transition != null) StopCoroutine(_transition);
            if (_camera != null)
                _transition = StartCoroutine(SmoothMove(_gameplayPos, _gameplayRot, _gameplayFov, 0.8f));

            if (_follow != null) _follow.enabled = true;
            Debug.Log("[STORY CAMERA] Gameplay camera ownership restored.");
        }

        private void LateUpdate()
        {
            // Subtle camera vibration during interior shots (flight feel).
            if (_cinematicActive && _interiorMode && _camera != null && _transition == null)
            {
                float t = Time.time;
                _camera.transform.position += new Vector3(
                    Mathf.Sin(t * 21f) * 0.006f,
                    Mathf.Sin(t * 15f) * 0.004f, 0f);
                _camera.transform.rotation *= Quaternion.Euler(0f, 0f, Mathf.Sin(t * 9f) * 0.15f);
            }
        }

        private IEnumerator SmoothMove(Vector3 targetPos, Quaternion targetRot, float targetFov, float duration)
        {
            if (_camera == null) yield break;

            Vector3 startPos = _camera.transform.position;
            Quaternion startRot = _camera.transform.rotation;
            float startFov = _camera.fieldOfView;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float smooth = t * t * (3f - 2f * t);

                _camera.transform.position = Vector3.Lerp(startPos, targetPos, smooth);
                _camera.transform.rotation = Quaternion.Slerp(startRot, targetRot, smooth);
                _camera.fieldOfView = Mathf.Lerp(startFov, targetFov, smooth);
                yield return null;
            }

            _camera.transform.position = targetPos;
            _camera.transform.rotation = targetRot;
            _camera.fieldOfView = targetFov;
            _transition = null;
        }
    }
}
