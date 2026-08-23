using UnityEngine;

namespace OperationOutbreak.Player
{
    /// <summary>
    /// Milestone 1B - drives the player prototype along the forward combat lane.
    ///
    /// Movement is kinematic (direct transform translation, no rigidbody / no physics forces):
    /// the lane is flat, the corridor is a simple rectangle and the player never jumps,
    /// so a solver-based controller would add cost without behavioural benefit.
    /// Smoothing is applied to the velocity, so input changes accelerate rather than teleport.
    ///
    /// Combat, health and enemies are intentionally NOT part of this component.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInputReader))]
    public class PlayerController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private PlayerLaneBounds laneBounds;
        [SerializeField] private PlayerHealth playerHealth;

        [Header("Movement")]
        [Tooltip("Side-to-side (strafe) speed across the lane, in units per second.")]
        [Min(0f)]
        [SerializeField] private float strafeSpeed = 6f;

        [Tooltip("Forward / backward speed along the lane, in units per second.")]
        [Min(0f)]
        [SerializeField] private float forwardSpeed = 5f;

        [Tooltip("Seconds to reach the target velocity. 0 = instant, higher = heavier.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float accelerationSmoothing = 0.08f;

        [Header("Facing")]
        [Tooltip("The player always faces down the lane (+Z) for this milestone.")]
        [SerializeField] private bool lockFacingForward = true;

        [Tooltip("Optional cosmetic lean of the Visual child while strafing, in degrees.")]
        [Range(0f, 25f)]
        [SerializeField] private float strafeLeanAngle = 8f;

        [Tooltip("Child transform that receives the cosmetic strafe lean.")]
        [SerializeField] private Transform visualRoot;

        /// <summary>Current world-space planar velocity, in units per second.</summary>
        public Vector3 CurrentVelocity => _velocity;

        /// <summary>
        /// Milestone 1O.5 - read-only planar (XZ) speed in units per second.
        ///
        /// Exists purely so the visual/animation layer can observe how fast the player is
        /// already moving without diffing world positions per frame and without gaining any
        /// authority over movement. Derived entirely from the velocity this controller
        /// already computes: no new state, no extra field, nothing cached.
        /// </summary>
        public float CurrentPlanarSpeed => new Vector2(_velocity.x, _velocity.z).magnitude;

        private Vector3 _velocity;
        private Vector3 _smoothingVelocity;
        private float _leanVelocity;
        private float _currentLean;
        private float _groundY;
        private bool _isDead;
        private bool _movementSuspended;
        private bool _cinematicMovementLock;

        private void Reset()
        {
            inputReader = GetComponent<PlayerInputReader>();
            laneBounds = GetComponent<PlayerLaneBounds>();
            playerHealth = GetComponent<PlayerHealth>();
            Transform visual = transform.Find("Visual");
            visualRoot = visual != null ? visual : null;
        }

        private void Awake()
        {
            if (inputReader == null)
            {
                inputReader = GetComponent<PlayerInputReader>();
            }

            if (laneBounds == null)
            {
                laneBounds = GetComponent<PlayerLaneBounds>();
            }

            if (playerHealth == null)
            {
                playerHealth = GetComponent<PlayerHealth>();
            }

            if (lockFacingForward)
            {
                transform.rotation = Quaternion.identity;
            }

            _groundY = transform.position.y;

            if (laneBounds != null)
            {
                laneBounds.Recalculate();
                transform.position = laneBounds.Clamp(transform.position);
            }
        }

        private void OnEnable()
        {
            if (playerHealth != null)
            {
                playerHealth.Died += HandlePlayerDied;
                _isDead = playerHealth.IsDead;
            }
        }

        private void OnDisable()
        {
            if (playerHealth != null)
            {
                playerHealth.Died -= HandlePlayerDied;
            }
        }

        /// <summary>
        /// Milestone 1K - safely stops movement after victory without disabling or
        /// destroying the Player. Velocity is zeroed so the prototype comes to rest
        /// instead of drifting, and the object stays in the scene for the camera.
        /// </summary>
        public void SuspendMovement()
        {
            _movementSuspended = true;
            _velocity = Vector3.zero;
            _smoothingVelocity = Vector3.zero;
        }

        /// <summary>
        /// 1Z QA fix #3 - TEMPORARY cinematic movement lock. Separate from the permanent
        /// SuspendMovement used by Mission Complete / Game Over. Both must be clear for
        /// movement to work. Reversible: call with false to release the cinematic lock.
        /// </summary>
        public void SetCinematicMovementLock(bool locked)
        {
            _cinematicMovementLock = locked;
            if (locked)
            {
                _velocity = Vector3.zero;
                _smoothingVelocity = Vector3.zero;
            }
        }

        /// <summary>True while the permanent or cinematic movement lock is active.</summary>
        public bool IsMovementLocked => _movementSuspended || _cinematicMovementLock;

        /// <summary>
        /// Milestone 1L - runtime-only movement speed upgrade (MOVE SPEED +15% gate).
        ///
        /// Scales the two authored speeds this ONE controller already uses, so there is
        /// no second movement controller and no parallel speed value. Direction, input,
        /// acceleration smoothing, lane clamping, facing and the camera rig are all
        /// untouched - they simply operate on a faster target velocity.
        ///
        /// RESET: strafeSpeed/forwardSpeed are serialized fields on a scene component,
        /// so a scene reload restores the authored 6 / 5.
        /// </summary>
        public void ApplyMoveSpeedMultiplier(float multiplier)
        {
            if (multiplier <= 0f)
            {
                return;
            }

            strafeSpeed = Mathf.Max(0f, strafeSpeed * multiplier);
            forwardSpeed = Mathf.Max(0f, forwardSpeed * multiplier);
        }

        private void Update()
        {
            if (_movementSuspended || _isDead || _cinematicMovementLock)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            Vector2 input = inputReader != null ? inputReader.MoveInput : Vector2.zero;

            Vector3 targetVelocity = new Vector3(input.x * strafeSpeed, 0f, input.y * forwardSpeed);

            _velocity = accelerationSmoothing > 0f
                ? Vector3.SmoothDamp(_velocity, targetVelocity, ref _smoothingVelocity, accelerationSmoothing)
                : targetVelocity;

            Vector3 nextPosition = transform.position + (_velocity * deltaTime);
            nextPosition.y = _groundY;

            if (laneBounds != null)
            {
                Vector3 clamped = laneBounds.Clamp(nextPosition);

                // Kill velocity on any axis that hit a limit so the player rests against
                // the boundary instead of accumulating pressure into it.
                if (!Mathf.Approximately(clamped.x, nextPosition.x))
                {
                    _velocity.x = 0f;
                    _smoothingVelocity.x = 0f;
                }

                if (!Mathf.Approximately(clamped.z, nextPosition.z))
                {
                    _velocity.z = 0f;
                    _smoothingVelocity.z = 0f;
                }

                nextPosition = clamped;
            }

            transform.position = nextPosition;

            if (lockFacingForward)
            {
                transform.rotation = Quaternion.identity;
            }

            ApplyStrafeLean(input.x, deltaTime);
        }

        private void HandlePlayerDied()
        {
            _isDead = true;
            _velocity = Vector3.zero;
            _smoothingVelocity = Vector3.zero;
        }

        private void ApplyStrafeLean(float strafeInput, float deltaTime)
        {
            if (visualRoot == null || strafeLeanAngle <= 0f)
            {
                return;
            }

            float targetLean = -Mathf.Clamp(strafeInput, -1f, 1f) * strafeLeanAngle;
            _currentLean = Mathf.SmoothDamp(_currentLean, targetLean, ref _leanVelocity, 0.12f, Mathf.Infinity, deltaTime);
            visualRoot.localRotation = Quaternion.Euler(0f, 0f, _currentLean);
        }
    }
}
