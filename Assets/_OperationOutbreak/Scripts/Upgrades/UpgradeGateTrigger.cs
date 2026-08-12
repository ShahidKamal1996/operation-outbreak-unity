using System;
using OperationOutbreak.Player;
using UnityEngine;

namespace OperationOutbreak.Upgrades
{
    /// <summary>
    /// Milestone 1J.2B - detects the real Player passing through one upgrade gate opening.
    ///
    /// Detection deliberately polls the authored Player reference against this trigger
    /// volume instead of using OnTriggerEnter. Unity only raises trigger messages when at
    /// least one participant carries a Rigidbody, and neither the approved Player
    /// (Transform + scripts only, kinematic movement) nor the gates have one. Giving the
    /// Player a Rigidbody/Collider to enable messages would change approved movement AND
    /// expose PlayerHealth - which implements IDamageable - to the projectile SphereCast,
    /// so the player would be able to shoot itself.
    ///
    /// Polling one known Transform also makes false activation impossible: zombies,
    /// projectiles, hit sparks and PlayerSpawn are never tested at all.
    ///
    /// Milestone 1J.3 - the gate still applies nothing itself. It raises Entered and lets
    /// UpgradeGateChoice decide, so the two gates stay unaware of weapons and of each other.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class UpgradeGateTrigger : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Appended to the verification log line for this gate.")]
        [SerializeField] private string upgradeLabel = "FIRE RATE +25%";

        [Header("Player Detection")]
        [Tooltip("The actual Player. Resolved from the scene at Awake when left empty.")]
        [SerializeField] private PlayerController player;

        /// <summary>Raised the first time the Player enters this gate, unless it is locked.</summary>
        public event Action<UpgradeGateTrigger> Entered;

        /// <summary>True once this gate has been entered during the current scene run.</summary>
        public bool HasBeenEntered { get; private set; }

        /// <summary>
        /// True once this gate can no longer award an upgrade for the current scene run:
        /// either the other gate was chosen, or this gate already paid out.
        /// </summary>
        public bool IsUpgradeLocked { get; private set; }

        /// <summary>Text shown on this gate, used for logging.</summary>
        public string UpgradeLabel => upgradeLabel;

        /// <summary>
        /// Permanently stops this gate awarding an upgrade for the rest of the scene run.
        /// Instance state only - a scene reload rebuilds this component unlocked.
        /// </summary>
        public void LockUpgrade()
        {
            IsUpgradeLocked = true;
        }

        private Collider _zone;
        private Transform _playerTransform;
        private Vector3 _previousPlayerPosition;
        private bool _hasPreviousPosition;

        private void Awake()
        {
            _zone = GetComponent<Collider>();

            if (player == null)
            {
                player = FindAnyObjectByType<PlayerController>();
            }

            _playerTransform = player != null ? player.transform : null;
        }

        private void Update()
        {
            if (HasBeenEntered || _playerTransform == null || _zone == null || !_zone.enabled)
            {
                return;
            }

            Vector3 position = _playerTransform.position;

            if (IsInside(position) || CrossedThisFrame(position))
            {
                HasBeenEntered = true;
                Debug.Log($"Upgrade gate entered: {upgradeLabel}", this);

                // Fires at most once per scene run: HasBeenEntered short-circuits every later
                // frame, so walking back through the chosen gate can never stack the upgrade.
                if (IsUpgradeLocked)
                {
                    Debug.Log($"Upgrade gate ignored (locked): {upgradeLabel}", this);
                }
                else
                {
                    Entered?.Invoke(this);
                }
            }

            _previousPlayerPosition = position;
            _hasPreviousPosition = true;
        }

        private bool IsInside(Vector3 position)
        {
            // Cheap reject first, then an exact test that also holds for a rotated gate.
            return _zone.bounds.Contains(position)
                   && (_zone.ClosestPoint(position) - position).sqrMagnitude <= 0.0001f;
        }

        /// <summary>Catches a low-framerate step that would otherwise skip over the thin volume.</summary>
        private bool CrossedThisFrame(Vector3 position)
        {
            if (!_hasPreviousPosition)
            {
                return false;
            }

            Vector3 step = position - _previousPlayerPosition;
            float distance = step.magnitude;

            return distance > 0.0001f
                   && _zone.bounds.IntersectRay(new Ray(_previousPlayerPosition, step / distance), out float hit)
                   && hit <= distance;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // A gate must never physically block the Player.
            Collider zone = GetComponent<Collider>();
            if (zone != null)
            {
                zone.isTrigger = true;
            }
        }
#endif
    }
}
