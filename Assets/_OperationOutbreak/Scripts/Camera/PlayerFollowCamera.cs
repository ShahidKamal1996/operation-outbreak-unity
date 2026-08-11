using UnityEngine;

namespace OperationOutbreak.CameraRig
{
    /// <summary>
    /// Milestone 1B - minimal follow rig that PRESERVES the approved Milestone 1A.1
    /// composition (position (0, 11, -11), rotation (31, 0, 0), vertical FOV 44).
    ///
    /// The offset and rotation are captured from the scene at Awake, so the approved
    /// framing is the authored configuration - this component never invents its own.
    /// It only slides the camera along Z so the player keeps the same screen height in
    /// the lower portion of the portrait frame while advancing up the lane.
    ///
    /// No shake, no combat/cinematic/aiming behaviour, no rotation changes.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class PlayerFollowCamera : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;

        [Header("Follow (approved composition is captured at Awake)")]
        [Tooltip("Follow the player's forward/back movement to keep the approved framing.")]
        [SerializeField] private bool followForward = true;

        [Tooltip("Also follow the player sideways. Off by default to avoid excessive camera motion.")]
        [SerializeField] private bool followLateral = false;

        [Tooltip("Fraction of the player's sideways offset the camera mirrors when lateral follow is on.")]
        [Range(0f, 1f)]
        [SerializeField] private float lateralFollowAmount = 0.25f;

        [Tooltip("Smoothing time in seconds. Higher = lazier camera.")]
        [Range(0f, 1f)]
        [SerializeField] private float smoothTime = 0.18f;

        [Tooltip("The player may drift this far forward/back before the camera reacts.")]
        [Min(0f)]
        [SerializeField] private float forwardDeadZone = 0.35f;

        private Vector3 _offset;
        private Vector3 _followVelocity;
        private float _baseY;

        private void Awake()
        {
            // Capture the approved 1A.1 composition exactly as authored in the scene.
            _baseY = transform.position.y;

            _offset = target != null
                ? transform.position - target.position
                : Vector3.zero;
        }

        private void LateUpdate()
        {
            if (target == null || (!followForward && !followLateral))
            {
                return;
            }

            Vector3 current = transform.position;
            Vector3 desired = current;

            if (followForward)
            {
                float desiredZ = target.position.z + _offset.z;

                if (Mathf.Abs(desiredZ - current.z) > forwardDeadZone)
                {
                    desired.z = desiredZ - (Mathf.Sign(desiredZ - current.z) * forwardDeadZone);
                }
            }

            if (followLateral)
            {
                desired.x = (target.position.x + _offset.x) * lateralFollowAmount;
            }

            desired.y = _baseY;

            transform.position = smoothTime > 0f
                ? Vector3.SmoothDamp(current, desired, ref _followVelocity, smoothTime)
                : desired;

            // Rotation and FOV are deliberately left untouched.
        }
    }
}
