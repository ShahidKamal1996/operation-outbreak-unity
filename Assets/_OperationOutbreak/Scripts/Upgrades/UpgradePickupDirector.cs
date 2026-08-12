using System.Collections.Generic;
using OperationOutbreak.Enemies;
using OperationOutbreak.Player;
using OperationOutbreak.UI;
using OperationOutbreak.Weapons;
using UnityEngine;

namespace OperationOutbreak.Upgrades
{
    /// <summary>
    /// Milestone 1L-R - drives the whole timed-pickup progression.
    ///
    /// Responsibilities are deliberately narrow: WHEN and WHERE the next opportunity
    /// appears, and what happens after it resolves. It does not animate anything (that is
    /// UpgradePickup), does not know how an upgrade works (that is UpgradeApplier) and
    /// does not draw the toast (that is UpgradeNotificationHud).
    ///
    /// ONE AT A TIME: a new pickup is only ever created when _active is null, and the
    /// spacing delay is only started from the Collected/Expired callbacks. Two pickups can
    /// therefore never coexist, and the sequence cannot skip ahead.
    ///
    /// PROGRESSION POINTS: the approved lane bounds confine the player to a compact
    /// arena (roughly z -3..15, x +/-3.6), so a "progression point" is a distinct
    /// position within reach - alternating sides and depths - reached after meaningful
    /// combat/travel spacing. Every point is validated against PlayerLaneBounds at spawn
    /// time, so a pickup can never appear somewhere the player is unable to reach.
    ///
    /// RESET: every field is ordinary instance state on a scene component, and Restart
    /// reloads the scene. The director is rebuilt with index 0 and all four upgrades
    /// available again. Nothing static, nothing written to an asset.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UpgradePickupDirector : MonoBehaviour
    {
        [System.Serializable]
        public sealed class UpgradeOpportunity
        {
            [Tooltip("The upgrade awarded by this opportunity.")]
            public UpgradeDefinition upgrade = new UpgradeDefinition();

            [Tooltip("Where this pickup appears, in world space. Clamped into the playable lane.")]
            public Vector3 spawnPosition = new Vector3(0f, 0f, 8f);

            [Tooltip("Seconds to wait after the PREVIOUS opportunity resolved before this one appears.")]
            [Min(0f)] public float delayBeforeSpawn = 2f;

            [Tooltip("Seconds this pickup stays available. 5 is the prototype value.")]
            [Min(0.5f)] public float lifetime = 5f;
        }

        [Header("Targets (resolved at Awake when empty)")]
        [SerializeField] private PlayerController playerController;
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private WeaponController weapon;
        [SerializeField] private PlayerLaneBounds laneBounds;
        [SerializeField] private UpgradeNotificationHud notificationHud;

        [Tooltip("Used only to stop offering upgrades once the encounter is won.")]
        [SerializeField] private EnemySpawner enemySpawner;

        [Header("Prototype Visual")]
        [Tooltip("Opaque prototype material used for the pickup core.")]
        [SerializeField] private Material coreMaterial;

        [Tooltip("Transparent prototype material used for the glow shell.")]
        [SerializeField] private Material glowMaterial;

        [Tooltip("Width of the pickup in world units. Small relative to the player.")]
        [Min(0.1f)] [SerializeField] private float pickupScale = 0.85f;

        [Tooltip("How high above the road the pickup floats.")]
        [SerializeField] private float hoverHeight = 1.15f;

        [Tooltip("How close the player must get to collect. Forgiving for touch input.")]
        [Min(0.1f)] [SerializeField] private float collectRadius = 1.25f;

        [Header("Sequence")]
        [Tooltip("Seconds before the FIRST opportunity appears, so the run starts with combat.")]
        [Min(0f)] [SerializeField] private float initialDelay = 3f;

        [Tooltip("Log each spawn/collect/expire to the console.")]
        [SerializeField] private bool verboseLogging = true;

        [Tooltip("The ordered upgrade opportunities for this run.")]
        [SerializeField]
        private List<UpgradeOpportunity> opportunities = new List<UpgradeOpportunity>();

        private UpgradeApplier _applier;
        private UpgradePickup _active;
        private int _nextIndex;
        private float _timer;
        private bool _waiting;
        private bool _sequenceFinished;
        private bool _halted;

        /// <summary>Index of the next opportunity to appear. Reset by a scene reload.</summary>
        public int NextOpportunityIndex => _nextIndex;

        /// <summary>True while a pickup is on the field.</summary>
        public bool HasActivePickup => _active != null;

        private void Awake()
        {
            if (playerController == null) playerController = FindAnyObjectByType<PlayerController>();
            if (playerHealth == null) playerHealth = FindAnyObjectByType<PlayerHealth>();
            if (weapon == null) weapon = FindAnyObjectByType<WeaponController>();
            if (laneBounds == null) laneBounds = FindAnyObjectByType<PlayerLaneBounds>();
            if (notificationHud == null) notificationHud = FindAnyObjectByType<UpgradeNotificationHud>();
            if (enemySpawner == null) enemySpawner = FindAnyObjectByType<EnemySpawner>();

            _applier = new UpgradeApplier(weapon, playerHealth, playerController);

            if (opportunities == null || opportunities.Count == 0)
            {
                BuildDefaultSequence();
            }
        }

        private void OnEnable()
        {
            // Instance state only - a scene reload restarts the whole sequence.
            _nextIndex = 0;
            _timer = 0f;
            _waiting = true;
            _sequenceFinished = false;
            _halted = false;

            if (playerHealth != null)
            {
                playerHealth.Died += HandlePlayerDied;
            }

            if (enemySpawner != null)
            {
                enemySpawner.EncounterCompleted += HandleEncounterCompleted;
            }
        }

        private void OnDisable()
        {
            if (playerHealth != null)
            {
                playerHealth.Died -= HandlePlayerDied;
            }

            if (enemySpawner != null)
            {
                enemySpawner.EncounterCompleted -= HandleEncounterCompleted;
            }
        }

        /// <summary>
        /// Fallback sequence used when the scene supplies no data. Keeps the prototype
        /// working even on a freshly added component.
        /// </summary>
        private void BuildDefaultSequence()
        {
            opportunities = new List<UpgradeOpportunity>
            {
                new UpgradeOpportunity
                {
                    upgrade = new UpgradeDefinition
                    {
                        displayName = "FIRE RATE", displayValue = "+25%",
                        kind = UpgradeKind.FireRateMultiplier, multiplier = 1.25f,
                        shape = UpgradePickupShape.Capsule,
                        tint = new Color(1f, 0.62f, 0.14f, 1f)
                    },
                    spawnPosition = new Vector3(-2.2f, 0f, 7f),
                    delayBeforeSpawn = 3f,
                    lifetime = 5f
                },
                new UpgradeOpportunity
                {
                    upgrade = new UpgradeDefinition
                    {
                        displayName = "DAMAGE", displayValue = "+1",
                        kind = UpgradeKind.DamageBonus, amount = 1,
                        shape = UpgradePickupShape.Cube,
                        tint = new Color(0.95f, 0.25f, 0.28f, 1f)
                    },
                    spawnPosition = new Vector3(2.4f, 0f, 11f),
                    delayBeforeSpawn = 2f,
                    lifetime = 5f
                },
                new UpgradeOpportunity
                {
                    upgrade = new UpgradeDefinition
                    {
                        displayName = "MAX HEALTH", displayValue = "+2",
                        kind = UpgradeKind.MaxHealthBonus, amount = 2,
                        shape = UpgradePickupShape.Sphere,
                        tint = new Color(0.24f, 0.85f, 0.38f, 1f)
                    },
                    spawnPosition = new Vector3(-2.6f, 0f, 4.5f),
                    delayBeforeSpawn = 2f,
                    lifetime = 5f
                },
                new UpgradeOpportunity
                {
                    upgrade = new UpgradeDefinition
                    {
                        displayName = "MOVE SPEED", displayValue = "+15%",
                        kind = UpgradeKind.MoveSpeedMultiplier, multiplier = 1.15f,
                        shape = UpgradePickupShape.Cylinder,
                        tint = new Color(0.28f, 0.66f, 1f, 1f)
                    },
                    spawnPosition = new Vector3(2.6f, 0f, 13f),
                    delayBeforeSpawn = 2f,
                    lifetime = 5f
                }
            };
        }

        private void Update()
        {
            if (_halted || _sequenceFinished || !_waiting || _active != null)
            {
                return;
            }

            _timer += Time.deltaTime;

            float required = _nextIndex == 0
                ? initialDelay
                : GetOpportunity(_nextIndex).delayBeforeSpawn;

            if (_timer >= required)
            {
                SpawnNext();
            }
        }

        private UpgradeOpportunity GetOpportunity(int index)
        {
            return opportunities[Mathf.Clamp(index, 0, opportunities.Count - 1)];
        }

        private void SpawnNext()
        {
            if (_nextIndex >= opportunities.Count)
            {
                _sequenceFinished = true;
                _waiting = false;
                return;
            }

            UpgradeOpportunity opportunity = opportunities[_nextIndex];
            _waiting = false;

            Vector3 position = ResolveSpawnPosition(opportunity.spawnPosition);

            GameObject pickupObject = new GameObject($"UpgradePickup_{opportunity.upgrade.displayName}");
            pickupObject.transform.SetParent(transform, false);
            pickupObject.transform.position = position;

            UpgradePickup pickup = pickupObject.AddComponent<UpgradePickup>();
            pickup.Collected += HandleCollected;
            pickup.Expired += HandleExpired;
            pickup.Initialise(
                opportunity.upgrade,
                playerController != null ? playerController.transform : null,
                coreMaterial,
                glowMaterial,
                opportunity.lifetime,
                collectRadius,
                hoverHeight,
                pickupScale);

            _active = pickup;

            if (verboseLogging)
            {
                Debug.Log(
                    $"Upgrade opportunity {_nextIndex + 1}/{opportunities.Count} available: " +
                    $"{opportunity.upgrade.DisplayLine} at {position} for {opportunity.lifetime}s.",
                    this);
            }
        }

        /// <summary>
        /// Keeps a spawn point inside the approved playable rectangle. The pickup floats,
        /// so only the ground plane matters here; height is applied by the pickup itself.
        /// </summary>
        private Vector3 ResolveSpawnPosition(Vector3 authored)
        {
            Vector3 position = authored;

            if (laneBounds != null)
            {
                // Inset slightly so a pickup never sits exactly on the boundary line.
                const float inset = 0.35f;
                position.x = Mathf.Clamp(position.x, laneBounds.MinX + inset, laneBounds.MaxX - inset);
                position.z = Mathf.Clamp(position.z, laneBounds.MinZ + inset, laneBounds.MaxZ - inset);
            }

            position.y = 0f;
            return position;
        }

        private void HandleCollected(UpgradePickup pickup)
        {
            if (pickup == null || pickup != _active)
            {
                return;
            }

            UpgradeDefinition definition = pickup.Definition;

            // Applied exactly once: UpgradePickup raises Collected a single time and the
            // reference is cleared immediately below.
            bool applied = _applier.Apply(definition, this);

            if (applied && notificationHud != null)
            {
                notificationHud.Show(definition.DisplayLine, definition.tint);
            }

            if (verboseLogging)
            {
                Debug.Log($"Upgrade collected: {definition.DisplayLine} (applied={applied}).", this);
            }

            AdvanceAfterResolution();
        }

        private void HandleExpired(UpgradePickup pickup)
        {
            if (pickup == null || pickup != _active)
            {
                return;
            }

            if (verboseLogging)
            {
                Debug.Log($"Upgrade missed: {pickup.Definition.DisplayLine} expired, no upgrade applied.", this);
            }

            // Deliberately no upgrade and no failure screen - the run simply continues.
            AdvanceAfterResolution();
        }

        /// <summary>
        /// Shared by collection and expiry: release the slot and start the spacing delay
        /// for the next opportunity. This is the ONLY place _nextIndex advances.
        /// </summary>
        private void AdvanceAfterResolution()
        {
            _active = null;
            _nextIndex++;
            _timer = 0f;

            if (_nextIndex >= opportunities.Count)
            {
                _sequenceFinished = true;
                _waiting = false;

                if (verboseLogging)
                {
                    Debug.Log("Upgrade sequence complete: all opportunities have been offered.", this);
                }

                return;
            }

            _waiting = true;
        }

        /// <summary>
        /// Stops offering upgrades once the run has ended. Also removes a pickup that is
        /// still floating, so nothing can be collected after death.
        /// </summary>
        public void HaltSequence()
        {
            _halted = true;
            _waiting = false;

            if (_active != null)
            {
                Destroy(_active.gameObject);
                _active = null;
            }
        }

        private void HandlePlayerDied()
        {
            HaltSequence();
        }

        private void HandleEncounterCompleted()
        {
            HaltSequence();
        }
    }
}
