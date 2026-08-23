using System.Collections;
using OperationOutbreak.CameraRig;
using UnityEngine;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z.1 — lightweight story camera that overrides the gameplay camera during
    /// cinematics. Subscribes to StoryCueEvents.CameraCue (in Awake, not OnEnable, so it is
    /// ready before any beat can fire). On cinematic cues it saves the gameplay camera state,
    /// disables PlayerFollowCamera, and smoothly interpolates the camera to a cinematic shot.
    /// On "gameplay_handoff" it restores the saved gameplay state.
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

        private void Awake()
        {
            _camera = FindAnyObjectByType<Camera>();
            _follow = FindAnyObjectByType<PlayerFollowCamera>();

            // Subscribe in Awake (not OnEnable) so we're ready before the first beat fires.
            StoryCueEvents.CameraCue += OnCameraCue;
        }

        private void OnDestroy()
        {
            StoryCueEvents.CameraCue -= OnCameraCue;
            ReturnToGameplay();
        }

        private void OnDisable()
        {
            ReturnToGameplay();
        }

        private void OnCameraCue(string cueId)
        {
            switch (cueId)
            {
                case "establishing_shot":
                    Debug.Log("[STORY CAMERA] Cue received: establishing_shot — taking cinematic ownership.");
                    EnterCinematic(new Vector3(0f, 16f, -16f), new Vector3(42f, 0f, 0f), 50f);
                    break;
                case "checkpoint_view":
                    Debug.Log("[STORY CAMERA] Cue received: checkpoint_view.");
                    EnterCinematic(new Vector3(0f, 12f, 28f), new Vector3(38f, 0f, 0f), 48f);
                    break;
                case "gameplay_handoff":
                    Debug.Log("[STORY CAMERA] Cue received: gameplay_handoff — restoring gameplay camera.");
                    ReturnToGameplay();
                    break;
            }
        }

        private void EnterCinematic(Vector3 targetPos, Vector3 targetEuler, float targetFov)
        {
            if (_follow != null)
            {
                _follow.enabled = false;
            }

            if (!_cinematicActive && _camera != null)
            {
                _gameplayPos = _camera.transform.position;
                _gameplayRot = _camera.transform.rotation;
                _gameplayFov = _camera.fieldOfView;
            }

            _cinematicActive = true;

            if (_transition != null) StopCoroutine(_transition);
            _transition = StartCoroutine(SmoothMove(targetPos, Quaternion.Euler(targetEuler), targetFov, 1.2f));
        }

        private void ReturnToGameplay()
        {
            if (!_cinematicActive) return;
            _cinematicActive = false;

            if (_transition != null) StopCoroutine(_transition);

            if (_camera != null)
            {
                _transition = StartCoroutine(SmoothMove(_gameplayPos, _gameplayRot, _gameplayFov, 0.8f));
            }

            if (_follow != null) _follow.enabled = true;
            Debug.Log("[STORY CAMERA] Gameplay camera ownership restored.");
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
                float smooth = t * t * (3f - 2f * t); // smoothstep

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
