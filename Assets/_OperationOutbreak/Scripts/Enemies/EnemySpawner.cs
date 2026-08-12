using System;
using System.Collections;
using System.Collections.Generic;
using OperationOutbreak.Player;
using UnityEngine;

namespace OperationOutbreak.Enemies
{
    /// <summary>
    /// Small, finite prototype encounter. No endless spawning or director logic.
    ///
    /// Milestone 1M - the same spawning code now serves the mission section flow. When
    /// "missionDriven" is set the spawner no longer runs all three waves back-to-back at
    /// Start; instead MissionSectionController calls BeginSection once per section and the
    /// spawner reports SectionCleared when that section's zombies are all dead. There is
    /// still exactly ONE spawning framework and ONE encounter-completion signal.
    ///
    /// Milestone 1N - the same framework now spawns more than one enemy archetype. A
    /// section supplies a composition ("3 BASIC + 1 RUNNER") and the spawner resolves each
    /// id to a prefab through the archetype library below. Every archetype still runs the
    /// existing ZombieController, is still tracked in the single _activeEnemies set, and
    /// therefore still counts toward the same section total, the same clear signal and the
    /// same auto-aim candidate list. No parallel enemy system was introduced.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemySpawner : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Basic zombie prefab. Also the fallback when an archetype id is unknown.")]
        [SerializeField] private ZombieController zombiePrefab;
        [SerializeField] private Transform playerTarget;
        [SerializeField] private PlayerHealth playerHealth;

        [Header("Enemy Archetypes (Milestone 1N)")]
        [Tooltip("Maps an archetype id to its prefab. Add an entry here to introduce a new " +
                 "enemy type - no spawner or mission-controller code change is required.")]
        [SerializeField] private List<EnemyArchetype> archetypes = new List<EnemyArchetype>();

        [Header("Controlled Waves")]
        [Min(1)] [SerializeField] private int waveOneCount = 3;
        [Min(1)] [SerializeField] private int waveTwoCount = 4;
        [Min(1)] [SerializeField] private int waveThreeCount = 5;
        [Min(0f)] [SerializeField] private float initialDelay = 1f;
        [Min(0.01f)] [SerializeField] private float spawnInterval = 0.6f;
        [Min(0f)] [SerializeField] private float betweenWaveDelay = 2f;

        [Header("Mission Sections (Milestone 1M)")]
        [Tooltip("When enabled the spawner waits for MissionSectionController to call " +
                 "BeginSection instead of running the legacy three waves at Start.")]
        [SerializeField] private bool missionDriven = true;

        [Header("Lane Spawn Positions")]
        [SerializeField] private Vector3 leftSpawnPosition = new Vector3(-2.5f, 1f, 16f);
        [SerializeField] private Vector3 centreSpawnPosition = new Vector3(0f, 1f, 19f);
        [SerializeField] private Vector3 rightSpawnPosition = new Vector3(2.5f, 1f, 16f);

        private readonly HashSet<ZombieController> _activeEnemies = new HashSet<ZombieController>();
        private bool _cancelled;
        private bool _encounterComplete;

        // Milestone 1M section state. Instance-only, so a scene reload is a full reset.
        private int _activeSectionIndex = -1;
        private bool _sectionRunning;

        /// <summary>
        /// Milestone 1K - raised exactly once when the final wave has been cleared.
        /// This reuses the single existing completion point below; no second
        /// wave-completion system exists. Never raised when the encounter was
        /// cancelled by player death, which keeps victory and Game Over exclusive.
        /// </summary>
        public event Action EncounterCompleted;

        /// <summary>
        /// Milestone 1M - raised when the zombies of one mission section have all been
        /// defeated. Carries the zero-based section index so a late callback for an
        /// out-of-date section can be ignored by the listener.
        /// </summary>
        public event Action<int> SectionCleared;

        /// <summary>True once the final wave has been cleared during this scene run.</summary>
        public bool IsEncounterComplete => _encounterComplete;

        /// <summary>True while a mission section's zombies are still being fought.</summary>
        public bool IsSectionRunning => _sectionRunning;

        /// <summary>Zero-based index of the section currently spawned, -1 before the first.</summary>
        public int ActiveSectionIndex => _activeSectionIndex;

        private void OnEnable()
        {
            if (playerHealth != null) playerHealth.Died += CancelEncounter;
        }
        private void OnDisable()
        {
            if (playerHealth != null) playerHealth.Died -= CancelEncounter;
            UnsubscribeAll();
        }
        private void Start()
        {
            if (zombiePrefab == null || playerTarget == null || playerHealth == null || playerHealth.IsDead) { _cancelled = true; return; }

            // Milestone 1M: the mission controller decides when each section spawns, so
            // the legacy "everything at once" encounter must not auto-start.
            if (missionDriven) return;

            StartCoroutine(RunEncounter());
        }
        private IEnumerator RunEncounter()
        {
            yield return new WaitForSeconds(initialDelay);
            int[] waves = { waveOneCount, waveTwoCount, waveThreeCount };
            for (int wave = 0; wave < waves.Length; wave++)
            {
                if (_cancelled) yield break;
                yield return StartCoroutine(SpawnWave(waves[wave], wave));
                while (!_cancelled && _activeEnemies.Count > 0) yield return null;
                if (_cancelled) yield break;
                if (wave < waves.Length - 1) yield return new WaitForSeconds(betweenWaveDelay);
            }
            if (!_cancelled && !_encounterComplete)
            {
                _encounterComplete = true;
                Debug.Log("Encounter complete", this);

                // Last statement of the encounter: listeners may safely stop this
                // coroutine while handling the event.
                EncounterCompleted?.Invoke();
            }
        }
        private IEnumerator SpawnWave(int count, int waveIndex)
        {
            for (int i = 0; i < count; i++)
            {
                if (_cancelled) yield break;
                Vector3 position = GetSpawnPosition((waveIndex + i) % 3);
                ZombieController zombie = Instantiate(zombiePrefab, position, Quaternion.identity);
                zombie.SetTarget(playerTarget, playerHealth);
                zombie.Died += HandleEnemyDied;
                _activeEnemies.Add(zombie);
                if (i < count - 1) yield return new WaitForSeconds(spawnInterval);
            }
        }
        private Vector3 GetSpawnPosition(int index)
        {
            if (index == 0) return leftSpawnPosition;
            return index == 1 ? centreSpawnPosition : rightSpawnPosition;
        }

        /// <summary>
        /// Milestone 1N - resolves an archetype id to its prefab. Unknown or empty ids fall
        /// back to the basic zombie so a typo in authoring data degrades to the original
        /// enemy rather than silently spawning nothing and stalling the section.
        /// </summary>
        private ZombieController ResolveArchetypePrefab(string archetypeId)
        {
            if (!string.IsNullOrEmpty(archetypeId) && archetypes != null)
            {
                for (int i = 0; i < archetypes.Count; i++)
                {
                    EnemyArchetype archetype = archetypes[i];

                    if (archetype != null
                        && archetype.prefab != null
                        && string.Equals(archetype.id, archetypeId, StringComparison.OrdinalIgnoreCase))
                    {
                        return archetype.prefab;
                    }
                }

                Debug.LogWarning(
                    $"Unknown enemy archetype '{archetypeId}'; falling back to the basic zombie.",
                    this);
            }

            return zombiePrefab;
        }

        /// <summary>
        /// Milestone 1N - flattens a composition into the exact spawn order used by the
        /// section. Entries are interleaved round-robin rather than spawned type-by-type so
        /// a Runner does not always arrive last; the total is unchanged either way.
        /// </summary>
        private static List<string> BuildSpawnOrder(IList<EnemySpawnEntry> composition, int fallbackCount)
        {
            List<string> order = new List<string>();

            if (composition != null && composition.Count > 0)
            {
                // Remaining counts per entry, consumed round-robin.
                int[] remaining = new int[composition.Count];
                int total = 0;

                for (int i = 0; i < composition.Count; i++)
                {
                    EnemySpawnEntry entry = composition[i];
                    remaining[i] = entry != null ? Mathf.Max(0, entry.count) : 0;
                    total += remaining[i];
                }

                while (order.Count < total)
                {
                    for (int i = 0; i < composition.Count; i++)
                    {
                        if (remaining[i] <= 0) continue;

                        remaining[i]--;
                        order.Add(composition[i].archetypeId);
                    }
                }
            }

            // No composition authored: preserve the pre-1N behaviour exactly.
            if (order.Count == 0)
            {
                for (int i = 0; i < Mathf.Max(1, fallbackCount); i++)
                {
                    order.Add(EnemyArchetypeId.Basic);
                }
            }

            return order;
        }

        /// <summary>
        /// Milestone 1M - spawns one mission section's zombies. Reuses the existing wave
        /// coroutine, spawn triangle and target wiring; only the forward offset changes so
        /// later sections appear ahead of the player in their own combat space.
        /// </summary>
        /// <param name="sectionIndex">Zero-based section index, echoed back on completion.</param>
        /// <param name="count">Fallback total when no composition is supplied.</param>
        /// <param name="spawnLineZ">World Z the section's spawn triangle is centred on.</param>
        /// <param name="composition">
        /// Milestone 1N - per-archetype make-up of this section. When null or empty the
        /// spawner falls back to <paramref name="count"/> basic zombies, which is exactly
        /// the Milestone 1M behaviour.
        /// </param>
        public void BeginSection(
            int sectionIndex, int count, float spawnLineZ, IList<EnemySpawnEntry> composition = null)
        {
            if (_cancelled || _encounterComplete || _sectionRunning) return;
            if (zombiePrefab == null || playerTarget == null || playerHealth == null) return;
            if (playerHealth.IsDead) { _cancelled = true; return; }

            _activeSectionIndex = sectionIndex;
            _sectionRunning = true;

            // Flattened here, not in the coroutine, so the authored list cannot be mutated
            // mid-spawn and the section's total is fixed the moment it begins.
            List<string> spawnOrder = BuildSpawnOrder(composition, count);

            StartCoroutine(RunSection(sectionIndex, spawnOrder, spawnLineZ));
        }

        private IEnumerator RunSection(int sectionIndex, List<string> spawnOrder, float spawnLineZ)
        {
            yield return new WaitForSeconds(initialDelay);

            if (_cancelled) { _sectionRunning = false; yield break; }

            // The authored triangle keeps its lateral offsets and its internal depth
            // stagger; the whole shape is simply translated to this section's spawn line.
            float authoredBaseZ = Mathf.Min(
                leftSpawnPosition.z, Mathf.Min(centreSpawnPosition.z, rightSpawnPosition.z));
            float zShift = spawnLineZ - authoredBaseZ;

            int count = spawnOrder.Count;

            for (int i = 0; i < count; i++)
            {
                if (_cancelled) { _sectionRunning = false; yield break; }

                Vector3 position = GetSpawnPosition(i % 3);
                position.z += zShift;

                // Milestone 1N - the ONLY type-dependent step in the whole spawn path.
                // Everything after it is identical for every archetype, which is what keeps
                // completion counting, auto-aim and death handling type-agnostic.
                ZombieController prefab = ResolveArchetypePrefab(spawnOrder[i]);

                if (prefab == null) continue;

                ZombieController zombie = Instantiate(prefab, position, Quaternion.identity);
                zombie.SetTarget(playerTarget, playerHealth);
                zombie.Died += HandleEnemyDied;
                _activeEnemies.Add(zombie);

                if (i < count - 1) yield return new WaitForSeconds(spawnInterval);
            }

            while (!_cancelled && _activeEnemies.Count > 0) yield return null;

            if (_cancelled) { _sectionRunning = false; yield break; }

            _sectionRunning = false;

            SectionCleared?.Invoke(sectionIndex);
        }

        /// <summary>
        /// Milestone 1M - raises the single existing encounter-completion signal once the
        /// mission's final section is done. Mission Complete already listens to this, so
        /// no second victory path is introduced.
        /// </summary>
        public void CompleteEncounter()
        {
            if (_cancelled || _encounterComplete) return;

            _encounterComplete = true;
            Debug.Log("Encounter complete", this);
            EncounterCompleted?.Invoke();
        }
        /// <summary>Returns the retained target when valid, otherwise closest living zombie generally ahead.</summary>
        public ZombieController AcquireTarget(Transform origin, float range, ZombieController retained)
        {
            if (origin == null || range <= 0f) return null;
            if (IsValidTarget(retained, origin, range)) return retained;
            ZombieController best = null;
            float bestDistance = float.MaxValue;
            foreach (ZombieController zombie in _activeEnemies)
            {
                if (!IsValidTarget(zombie, origin, range)) continue;
                float distance = (zombie.transform.position - origin.position).sqrMagnitude;
                if (distance < bestDistance) { bestDistance = distance; best = zombie; }
            }
            return best;
        }
        private static bool IsValidTarget(ZombieController zombie, Transform origin, float range)
        {
            if (zombie == null || !zombie.isActiveAndEnabled || !zombie.IsAlive) return false;
            Vector3 toZombie = zombie.transform.position - origin.position;
            toZombie.y = 0f;
            if (toZombie.sqrMagnitude > range * range) return false;
            return Vector3.Dot(origin.forward, toZombie.normalized) >= -0.15f;
        }

        private void HandleEnemyDied(ZombieController zombie)
        {
            if (zombie != null) zombie.Died -= HandleEnemyDied;
            _activeEnemies.Remove(zombie);
        }
        /// <summary>
        /// Milestone 1K - halts all remaining combat activity after victory: no further
        /// waves are scheduled and any zombie still present stops chasing and attacking.
        /// Runtime state only; a scene reload starts a fresh encounter.
        /// </summary>
        public void StopEncounter()
        {
            _cancelled = true;
            _sectionRunning = false;
            StopAllCoroutines();

            foreach (ZombieController zombie in _activeEnemies)
            {
                if (zombie != null)
                {
                    zombie.SuspendCombat();
                }
            }
        }

        private void CancelEncounter()
        {
            if (_cancelled) return;
            _cancelled = true;
            _sectionRunning = false;
            StopAllCoroutines();
        }
        private void UnsubscribeAll()
        {
            foreach (ZombieController zombie in _activeEnemies) if (zombie != null) zombie.Died -= HandleEnemyDied;
            _activeEnemies.Clear();
        }
    }
}
