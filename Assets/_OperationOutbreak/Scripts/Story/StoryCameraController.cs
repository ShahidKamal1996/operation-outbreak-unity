using OperationOutbreak.CameraRig;
using UnityEngine;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z.1 — lightweight story camera that overrides the gameplay camera during
    /// cinematics. Subscribes to StoryCueEvents.CameraCue. On cinematic cues it disables
    /// PlayerFollowCamera and positions the main camera for a cinematic shot. On
    /// "gameplay_handoff" it re-enables PlayerFollowCamera which snaps back to gameplay.
    /// No Cinemachine; uses the existing Camera + PlayerFollowCamera architecture.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StoryCameraController : MonoBehaviour
    {
        private Camera _camera;
        private PlayerFollowCamera _follow;
        private bool _cinematicActive;

        private void Awake()
        {
            _camera = FindAnyObjectByType<Camera>();
            _follow = FindAnyObjectByType<PlayerFollowCamera>();
        }

        private void OnEnable() => StoryCueEvents.CameraCue += OnCameraCue;
        private void OnDisable()
        {
            StoryCueEvents.CameraCue -= OnCameraCue;
            ReturnToGameplay();
        }

        private void OnCameraCue(string cueId)
        {
            switch (cueId)
            {
                case "establishing_shot":
                    EnterCinematic(new Vector3(0f, 14f, -18f), new Vector3(38f, 0f, 0f));
                    break;
                case "checkpoint_view":
                    EnterCinematic(new Vector3(0f, 10f, 30f), new Vector3(35f, 0f, 0f));
                    break;
                case "gameplay_handoff":
                    ReturnToGameplay();
                    break;
            }
        }

        private void EnterCinematic(Vector3 position, Vector3 eulerAngles)
        {
            if (_follow != null) _follow.enabled = false;
            _cinematicActive = true;
            if (_camera != null)
            {
                _camera.transform.position = position;
                _camera.transform.eulerAngles = eulerAngles;
            }
        }

        private void ReturnToGameplay()
        {
            if (!_cinematicActive) return;
            _cinematicActive = false;
            if (_follow != null) _follow.enabled = true;
        }
    }
}
