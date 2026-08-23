using System.Collections;
using OperationOutbreak.CameraRig;
using UnityEngine;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z.1 QA fix #8 — cinematic camera for Mission 01's opening.
    ///
    /// KEY FIX (the reported camera clipping + muddy "yellow/brown" opening frame):
    /// the interior rig lives at y=-300 while gameplay is at y≈11. QA fix #7 SMOOTHLY LERPED the
    /// Main Camera between those two worlds for ~1s, so for visible frames the camera swept through
    /// the asphalt road and cabin geometry. This controller now SNAPS the camera for any move that
    /// crosses world space (first interior entry, interior→exterior), and only smooths small moves
    /// that stay inside one space (in-cabin reframes, the same-world gameplay handoff). The
    /// <see cref="StoryFadeController"/> hides every snap behind black, so the player never sees
    /// travel — only BLACK → framed interior.
    ///
    /// Interior anchors are sourced from <see cref="HelicopterInteriorRig"/> (single-sourced,
    /// re-authored around the real character). The camera mirrors the rig's published vibration so
    /// Kane never drifts in frame, and tightens the near clip while inside the cabin so nearby
    /// structure can never clip.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StoryCameraController : MonoBehaviour
    {
        private Camera _camera;
        private PlayerFollowCamera _follow;
        private bool _cinematicActive;
        private bool _interiorMode;

        private Vector3 _gameplayPos;
        private Quaternion _gameplayRot;
        private float _gameplayFov;
        private float _gameplayNearClip;

        // Base interior transform (anchor world pos/rot WITHOUT vibration); vibration applied on top.
        private Vector3 _interiorBasePos;
        private Quaternion _interiorBaseRot;

        private Coroutine _transition;
        private HelicopterInteriorRig _interiorRig;

        // Exterior / insertion anchors (gameplay world space).
        private static readonly Vector3 ExteriorApproachPos = new Vector3(0f, 16f, -16f);
        private static readonly Quaternion ExteriorApproachRot = Quaternion.Euler(42f, 0f, 0f);
        private static readonly Vector3 InsertionPos = new Vector3(-4f, 6f, -8f);
        private static readonly Quaternion InsertionRot = Quaternion.Euler(25f, 25f, 0f);

        private const float InteriorNearClip = 0.05f;

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
                    // First interior entry — SNAP (world jump from gameplay to y=-300). Black hides it.
                    EnterInteriorShot(cueId, snap: true);
                    break;
                case "m01_interior_kane_close":
                case "m01_interior_front":
                    // Small in-cabin reframes — smooth, never crosses world space.
                    EnterInteriorShot(cueId, snap: false);
                    break;
                case "m01_exterior_approach":
                    // Leaving the interior world — SNAP (y=-300 → gameplay). Black hides it.
                    EnterCinematic(ExteriorApproachPos, ExteriorApproachRot, 50f, snap: true);
                    break;
                case "m01_insertion":
                    // Distinct insertion cut in the gameplay world — snap for a clean angle change.
                    EnterCinematic(InsertionPos, InsertionRot, 46f, snap: true);
                    break;
                case "establishing_shot":
                    EnterCinematic(ExteriorApproachPos, ExteriorApproachRot, 50f, snap: false);
                    break;
                case "checkpoint_view":
                    EnterCinematic(new Vector3(0f, 12f, 28f), Quaternion.Euler(38f, 0f, 0f), 48f, snap: false);
                    break;
                case "gameplay_handoff":
                    ReturnToGameplay();
                    break;
            }
        }

        private void EnterInteriorShot(string cueId, bool snap)
        {
            if (_interiorRig == null || !_interiorRig.TryGetCameraAnchor(cueId, out Vector3 pos, out Quaternion rot, out float fov))
            {
                Debug.LogWarning("[STORY CAMERA] Interior anchor '" + cueId + "' not found on rig.");
                return;
            }

            SaveGameplayStateOnce();
            if (_follow != null) _follow.enabled = false;

            _cinematicActive = true;
            _interiorMode = true;
            if (_camera != null) _camera.nearClipPlane = InteriorNearClip;

            _interiorBasePos = pos;
            _interiorBaseRot = rot;

            if (snap)
            {
                if (_transition != null) StopCoroutine(_transition);
                ApplyInteriorBase();
                _transition = null;
            }
            else
            {
                if (_transition != null) StopCoroutine(_transition);
                _transition = StartCoroutine(SmoothInteriorMove(pos, rot, fov));
            }
        }

        private void EnterCinematic(Vector3 targetPos, Quaternion targetRot, float targetFov, bool snap)
        {
            SaveGameplayStateOnce();
            if (_follow != null) _follow.enabled = false;

            _cinematicActive = true;
            _interiorMode = false;
            if (_camera != null) _camera.nearClipPlane = InteriorNearClip;

            if (snap)
            {
                if (_transition != null) StopCoroutine(_transition);
                if (_camera != null)
                {
                    _camera.transform.position = targetPos;
                    _camera.transform.rotation = targetRot;
                    _camera.fieldOfView = targetFov;
                }
                _transition = null;
            }
            else
            {
                if (_transition != null) StopCoroutine(_transition);
                _transition = StartCoroutine(SmoothMove(targetPos, targetRot, targetFov, 1.2f));
            }
        }

        private void ReturnToGameplay()
        {
            if (!_cinematicActive) return;
            _cinematicActive = false;
            _interiorMode = false;

            if (_transition != null) StopCoroutine(_transition);

            if (_camera != null)
            {
                // Restore near clip immediately so gameplay framing is unaffected.
                _camera.nearClipPlane = _gameplayNearClip > 0f ? _gameplayNearClip : 0.3f;

                // If the camera is far from the gameplay position it is in another cinematic world
                // (e.g. the y=-300 interior on a SKIP). A smooth move would visibly sweep through
                // geometry, so SNAP in that case. Small same-world moves (e.g. insertion -> follow)
                // still smooth for a polished handoff.
                float distance = Vector3.Distance(_camera.transform.position, _gameplayPos);
                if (distance > WorldJumpSnapThreshold)
                {
                    _camera.transform.position = _gameplayPos;
                    _camera.transform.rotation = _gameplayRot;
                    _camera.fieldOfView = _gameplayFov;
                    _transition = null;
                }
                else
                {
                    _transition = StartCoroutine(SmoothMove(_gameplayPos, _gameplayRot, _gameplayFov, 0.8f));
                }
            }

            if (_follow != null) _follow.enabled = true;
            Debug.Log("[STORY CAMERA] Gameplay camera ownership restored.");
        }

        // Above this distance a ReturnToGameplay snaps instead of lerps (crosses a cinematic world).
        private const float WorldJumpSnapThreshold = 30f;

        private void LateUpdate()
        {
            // Mirror the cabin's exact vibration so Kane stays pinned in frame (no drift). Only when
            // settled in interior mode (not mid-transition).
            if (_cinematicActive && _interiorMode && _transition == null && _interiorRig != null && _camera != null)
            {
                _camera.transform.position = _interiorBasePos + _interiorRig.VibrationOffset;
                _camera.transform.rotation = _interiorBaseRot * _interiorRig.VibrationRotation;
            }
        }

        private void ApplyInteriorBase()
        {
            if (_camera == null) return;
            _camera.transform.position = _interiorBasePos;
            _camera.transform.rotation = _interiorBaseRot;
        }

        private void SaveGameplayStateOnce()
        {
            if (_cinematicActive || _camera == null) return;
            _gameplayPos = _camera.transform.position;
            _gameplayRot = _camera.transform.rotation;
            _gameplayFov = _camera.fieldOfView;
            _gameplayNearClip = _camera.nearClipPlane;
        }

        private IEnumerator SmoothInteriorMove(Vector3 targetPos, Quaternion targetRot, float targetFov)
        {
            if (_camera == null) yield break;
            Vector3 startPos = _camera.transform.position;
            Quaternion startRot = _camera.transform.rotation;
            float startFov = _camera.fieldOfView;
            float elapsed = 0f;
            const float duration = 1.0f;

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
            _interiorBasePos = targetPos;
            _interiorBaseRot = targetRot;
            _transition = null;
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
