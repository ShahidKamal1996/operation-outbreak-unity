using System.Collections.Generic;
using OperationOutbreak.Enemies;
using OperationOutbreak.Mission;
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

        [Tooltip("Milestone 1M - optional. When present, pickups are constrained to the " +
                 "section the player has actually unlocked.")]
        [SerializeField] private MissionSectionController missionSections;

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

        [Header("Random Placement")]
        [Tooltip("Keeps pickups away from the left/right and forward/back limits of the lane.")]
        [Min(0f)] [SerializeField] private float edgeMargin = 0.6f;

        [Tooltip("A pickup never spawns closer than this to the player.")]
        [Min(0f)] [SerializeField] private float minimumDistanceFromPlayer = 3f;

        [Tooltip("A pickup never spawns this close to where the previous one appeared.")]
        [Min(0f)] [SerializeField] private float minimumDistanceFromPreviousPickup = 4f;

        [Tooltip("How many random points to try before falling back to a safe deterministic one.")]
        [Range(1, 64)] [SerializeField] private int maxPlacementAttempts = 24;

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

        // Shuffled per run. Holds indices into "opportunities" - a permutation, so every
        // configured upgrade is offered exactly once and none can repeat.
        private readonly List<int> _runOrder = new List<int>();
        private Vector3 _previousSpawn;
        private bool _hasPreviousSpawn;
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
            if (missionSections == null) missionSections = FindAnyObjectByType<MissionSectionController>();

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
            _hasPreviousSpawn = false;
            _previousSpawn = Vector3.zero;

            BuildShuffledRunOrder();

            if (playerHealth != null)
            {
                playerHealth.Died += HandlePlayerDied;
            }

            // Milestone 1M - the upgrade run now ends with the MISSION, not with the
            // first cleared section. EncounterCompleted is only raised after the final
            // section, but subscribing to the mission event keeps the intent explicit.
            if (missionSections != null)
            {
                missionSections.MissionCompleted += HandleEncounterCompleted;
            }
            else if (enemySpawner != null)
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

            if (missionSections != null)
            {
                missionSections.MissionCompleted -= HandleEncounterCompleted;
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
                    delayBeforeSpawn = 2f,
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
                    delayBeforeSpawn = 2f,
                    lifetime = 5f
                }
            };
        }

        /// <summary>
        /// Fisher-Yates shuffle of a runtime index list. The serialized "opportunities"
        /// list is never reordered or written to, so the asset/scene config is untouched
        /// and nothing persists between runs. A permutation guarantees each upgrade is
        /// offered exactly once with no duplicates.
        /// </summary>
        private void BuildShuffledRunOrder()
        {
            _runOrder.Clear();

            if (opportunities == null)
            {
                return;
            }

            for (int i = 0; i < opportunities.Count; i++)
            {
                if (opportunities[i] != null && opportunities[i].upgrade != null)
                {
                    _runOrder.Add(i);
                }
            }

            for (int i = _runOrder.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (_runOrder[i], _runOrder[j]) = (_runOrder[j], _runOrder[i]);
            }

            if (verboseLogging && _runOrder.Count > 0)
            {
                string order = string.Empty;
                for (int i = 0; i < _runOrder.Count; i++)
                {
                    order += (i > 0 ? " -> " : string.Empty)
                        + opportunities[_runOrder[i]].upgrade.displayName;
                }

                Debug.Log($"Upgrade order for this run: {order}", this);
            }
        }

        /// <summary>
        /// Picks a fresh random point inside the approved playable rectangle. Upgrade type
        /// and position are independent - this is called per spawn and knows nothing about
        /// which upgrade is being placed. PlayerLaneBounds stays the single source of truth;
        /// no second set of world bounds is introduced here.
        /// </summary>
        private Vector3 GenerateSpawnPosition()
        {
            float minX, maxX, minZ, maxZ;

            if (laneBounds != null)
            {
                minX = laneBounds.MinX;
                maxX = laneBounds.MaxX;
                minZ = laneBounds.MinZ;
                maxZ = laneBounds.MaxZ;
            }
            else
            {
                // Defensive only: without bounds, hug the player's own position.
                Vector3 fallbackCentre = playerController != null
                    ? playerController.transform.position
                    : Vector3.zero;
                minX = fallbackCentre.x - 1f;
                maxX = fallbackCentre.x + 1f;
                minZ = fallbackCentre.z + 2f;
                maxZ = fallbackCentre.z + 4f;
            }

            // Milestone 1M - clamp to the section the player has actually unlocked.
            // PlayerLaneBounds already reports the current forward limit, so maxZ is
            // never inside a locked section; this additionally lifts minZ to the current
            // section's rear so a pickup is not stranded deep in cleared ground.
            if (missionSections != null)
            {
                float sectionMinZ = missionSections.CurrentSectionMinZ;

                if (!float.IsNegativeInfinity(sectionMinZ))
                {
                    minZ = Mathf.Max(minZ, Mathf.Min(sectionMinZ, maxZ));
                }
            }

            // Inset so a pickup never sits on the boundary line itself. If the lane is
            // narrower than two margins, collapse to the centre rather than inverting.
            minX = ShrinkMin(minX, maxX, edgeMargin);
            maxX = ShrinkMax(minX, maxX, edgeMargin);
            minZ = ShrinkMin(minZ, maxZ, edgeMargin);
            maxZ = ShrinkMax(minZ, maxZ, edgeMargin);

            Vector3 player = playerController != null
                ? playerController.transform.position
                : Vector3.zero;

            float playerSqr = minimumDistanceFromPlayer * minimumDistanceFromPlayer;
            float previousSqr = minimumDistanceFromPreviousPickup * minimumDistanceFromPreviousPickup;

            Vector3 best = Vector3.zero;
            float bestScore = float.NegativeInfinity;

            for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
            {
                Vector3 candidate = new Vector3(
                    Random.Range(minX, maxX),
                    0f,
                    Random.Range(minZ, maxZ));

                float playerDistSqr = HorizontalSqrDistance(candidate, player);
                float previousDistSqr = _hasPreviousSpawn
                    ? HorizontalSqrDistance(candidate, _previousSpawn)
                    : float.MaxValue;

                if (playerDistSqr >= playerSqr && previousDistSqr >= previousSqr)
                {
                    return candidate;
                }

                // Track the least-bad candidate so a cramped lane still degrades gracefully.
                float score = Mathf.Min(
                    playerDistSqr - playerSqr,
                    previousDistSqr == float.MaxValue ? 0f : previousDistSqr - previousSqr);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            // Deterministic safe fallback: the in-bounds corner furthest from the player.
            // Always inside the lane and reachable, so a failed roll can never produce an
            // invalid pickup.
            Vector3 deterministic = FurthestCornerFromPlayer(minX, maxX, minZ, maxZ, player);

            if (bestScore > float.NegativeInfinity
                && HorizontalSqrDistance(best, player) > HorizontalSqrDistance(deterministic, player))
            {
                return best;
            }

            if (verboseLogging)
            {
                Debug.Log(
                    $"Upgrade placement fell back to a deterministic safe point after "
                    + $"{maxPlacementAttempts} attempts.",
                    this);
            }

            return deterministic;
        }

        private static float ShrinkMin(float min, float max, float margin)
        {
            return (max - min) <= (margin * 2f) ? (min + max) * 0.5f : min + margin;
        }

        private static float ShrinkMax(float min, float max, float margin)
        {
            return (max - min) <= (margin * 2f) ? (min + max) * 0.5f : max - margin;
        }

        private static float HorizontalSqrDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return (dx * dx) + (dz * dz);
        }

        private static Vector3 FurthestCornerFromPlayer(
            float minX, float maxX, float minZ, float maxZ, Vector3 player)
        {
            float x = Mathf.Abs(maxX - player.x) >= Mathf.Abs(player.x - minX) ? maxX : minX;
            float z = Mathf.Abs(maxZ - player.z) >= Mathf.Abs(player.z - minZ) ? maxZ : minZ;
            return new Vector3(x, 0f, z);
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

        /// <summary>Resolves a sequence slot through this run's shuffled order.</summary>
        private UpgradeOpportunity GetOpportunity(int index)
        {
            int slot = Mathf.Clamp(index, 0, _runOrder.Count - 1);
            return opportunities[_runOrder[slot]];
        }

        /// <summary>How many opportunities this run will offer.</summary>
        private int OpportunityCount => _runOrder.Count;

        private void SpawnNext()
        {
            if (_nextIndex >= OpportunityCount)
            {
                _sequenceFinished = true;
                _waiting = false;
                return;
            }

            UpgradeOpportunity opportunity = GetOpportunity(_nextIndex);
            _waiting = false;

            // Fresh position every time a pickup is created - never tied to upgrade type.
            Vector3 position = ResolveSpawnPosition(GenerateSpawnPosition());
            _previousSpawn = position;
            _hasPreviousSpawn = true;

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
                    $"Upgrade opportunity {_nextIndex + 1}/{OpportunityCount} available: " +
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

            if (_nextIndex >= OpportunityCount)
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
