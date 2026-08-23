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

        [Header("Archetype Spawn Safety (Milestone 1N.1)")]
        [Tooltip("DEFAULT closest any archetype may spawn to the player, in world units. " +
                 "An archetype may override this with its own minimumSpawnStandoffOverride; " +
                 "this value is used whenever it does not.")]
        [Min(1f)] [SerializeField] private float minimumSpawnStandoff = 12f;

        [Tooltip("Enemies spawned closer than this to a live enemy are nudged back toward " +
                 "the spawn band, preventing overlapping spawns.")]
        [Min(0.1f)] [SerializeField] private float spawnClearanceRadius = 1.4f;

        [Tooltip("Safety cap on how many times one spawn may be nudged clear.")]
        [Min(1)] [SerializeField] private int maximumSpawnNudges = 6;

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

        /// <summary>
        /// Milestone 1O - raised immediately after an enemy has been instantiated and
        /// targeted, carrying the enemy, the archetype id that produced it and the section
        /// it belongs to (-1 for the legacy non-mission waves).
        ///
        /// Diagnostics needs a per-enemy hook that also knows WHICH archetype was resolved,
        /// which no existing event carried. It is raised at the end of the spawn step, so a
        /// listener cannot affect placement, the nudge pass or spawn timing.
        ///
        /// Milestone 1O-R - now carries an <see cref="EnemySpawnReport"/> so diagnostics can
        /// also see the authored band position and the offset that was REQUESTED, not just
        /// the final position. Without that, a spawn offset silently removed by the standoff
        /// clamp is indistinguishable from one that was never configured.
        /// </summary>
        public event Action<ZombieController, EnemySpawnReport> EnemySpawned;

        /// <summary>True once the final wave has been cleared during this scene run.</summary>
        public bool IsEncounterComplete => _encounterComplete;

        /// <summary>
        /// 1X.5 QA fix #1 - true once StopEncounter/CancelEncounter has frozen all combat (success
        /// or death). No further spawning or enemy chasing/attacking can occur.
        /// </summary>
        public bool IsCombatStopped => _cancelled;

        /// <summary>
        /// Milestone 1O - read-only view of the authored spawn clearance radius, so the
        /// diagnostics overlap check measures against the same number the spawner used
        /// rather than a hard-coded copy.
        /// </summary>
        public float SpawnClearanceRadius => spawnClearanceRadius;

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
                RegisterEnemy(zombie);

                // Milestone 1O - same observation hook on the legacy wave path.
                EnemySpawned?.Invoke(zombie, new EnemySpawnReport(
                    EnemyArchetypeId.Basic, -1, position, position, 0f, minimumSpawnStandoff));

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
            EnemyArchetype archetype = ResolveArchetype(archetypeId);

            return archetype != null && archetype.prefab != null ? archetype.prefab : zombiePrefab;
        }

        // ------------------------------------------------------------------
        // Milestone 1S - data-driven archetype spawn seam.
        //
        // The existing mission composition path above (1N EnemySpawnEntry +
        // prefab library) is deliberately UNTOUCHED: the current mission keeps
        // spawning exactly what it always spawned. These overloads are the seam
        // future mission definitions (1T+) use to request an enemy BY ITS 1S
        // ARCHETYPE DEFINITION. Both paths converge on the same single spawn
        // bookkeeping (SetTarget, Died subscription, _activeEnemies tracking,
        // EnemySpawned report), so completion counting, auto-aim and death
        // handling stay type-agnostic.
        // ------------------------------------------------------------------

        /// <summary>
        /// Milestone 1S - spawns an enemy by ARCHETYPE DEFINITION ID through the
        /// shared gameplay prefab. Unknown/empty ids resolve to the default
        /// (verified Basic Infected), mirroring the 1N fallback rule.
        /// </summary>
        public ZombieController SpawnEnemy(string archetypeId)
        {
            EnemyArchetypeDefinition definition =
                EnemyArchetypeRegistry.ResolveRequestedArchetype(archetypeId);

            return SpawnEnemy(definition);
        }

        /// <summary>
        /// Milestone 1S - spawns an enemy configured by the given archetype
        /// definition, at the spawner's centre lane slot. A null definition is
        /// the verified default (no tuning applied).
        /// </summary>
        public ZombieController SpawnEnemy(EnemyArchetypeDefinition definition)
        {
            return SpawnEnemyWithDefinition(definition, GetSpawnPosition(1));
        }

        /// <summary>
        /// Milestone 1S - the core seam: instantiate the SHARED gameplay prefab,
        /// apply the definition (gameplay values + presentation profile), then
        /// run the exact bookkeeping every other spawn runs. This is the single
        /// entry point 1T mission definitions will call per spawn request.
        /// </summary>
        public ZombieController SpawnEnemyWithDefinition(
            EnemyArchetypeDefinition definition, Vector3 position)
        {
            // 1X.5 QA fix #1 + 1Z QA fix #2 - refuse to spawn after the encounter has ended
            // (success or death) or during a temporary cinematic spawn-pause.
            if (_cancelled || _encounterComplete || _spawnPaused)
            {
                return null;
            }

            if (zombiePrefab == null)
            {
                Debug.LogError("[1S] No shared enemy prefab assigned to the spawner.", this);
                return null;
            }

            ZombieController zombie = Instantiate(zombiePrefab, position, Quaternion.identity);

            // Apply BEFORE SetTarget/tracking so the enemy's first combat frame
            // already runs with the archetype's tuning (and its health re-seed).
            EnemyArchetypeApplication.Apply(zombie.gameObject, definition);

            zombie.SetTarget(playerTarget, playerHealth);
            zombie.Died += HandleEnemyDied;
            RegisterEnemy(zombie);

            EnemySpawned?.Invoke(zombie, new EnemySpawnReport(
                definition != null ? definition.ArchetypeId : EnemyArchetypeId.Basic,
                -1, position, position, 0f, minimumSpawnStandoff));

            return zombie;
        }

        /// <summary>
        /// Milestone 1N.1 - moves a spawn point <paramref name="offset"/> units closer to the
        /// player along the lane, then clamps the result so the shortcut can never produce an
        /// unfair or invalid position.
        ///
        /// Guarantees, in order of application:
        ///  * never closer to the player than <paramref name="standoff"/> - the archetype's own
        ///    minimum if it declares one, otherwise the spawner's global
        ///    <see cref="minimumSpawnStandoff"/> - so the enemy always appears ahead of the
        ///    player with a real reaction window, never beside or on top of them;
        ///  * never behind the player, because the standoff is measured forward from the
        ///    player's own z;
        ///  * never further out than the authored band (the offset only ever pulls inward);
        ///  * never on top of an enemy already on the field - the point is nudged back toward
        ///    the band until it clears <see cref="spawnClearanceRadius"/>, which preserves the
        ///    crowd separation the chase code then maintains.
        /// The lane's x is untouched, so the enemy stays inside the playable lane and clear of
        /// the boundary geometry by construction. The result always remains well inside the
        /// weapon's target range, since it is strictly nearer than the band it came from.
        /// </summary>
        private Vector3 ApplyForwardSpawnOffset(Vector3 bandPosition, float offset, float standoff)
        {
            // Milestone 1N.2 - the clamp itself lives in EnemySpawnMath so the safety rules
            // have a single definition that the EditMode tests can call directly.
            float clampedZ = playerTarget != null
                ? EnemySpawnMath.ClampForwardOffset(
                    bandPosition.z, playerTarget.position.z, offset, standoff)
                : bandPosition.z;

            // Push back out of anyone already standing there. Stepping outward (away from the
            // player) can only ever make the position safer, never closer than the standoff.
            Vector3 candidate = new Vector3(bandPosition.x, bandPosition.y, clampedZ);

            for (int attempt = 0; attempt < maximumSpawnNudges; attempt++)
            {
                if (!IsSpawnBlocked(candidate)) break;

                candidate.z += spawnClearanceRadius;

                // Never pushed beyond the archetype's own band: at that point the position is
                // exactly the basic spawn point, which is where it would have been anyway.
                if (candidate.z >= bandPosition.z)
                {
                    candidate.z = bandPosition.z;
                    break;
                }
            }

            return candidate;
        }

        /// <summary>
        /// Milestone 1N.1 - true when a live enemy is already occupying this point. Uses the
        /// spawner's own active set rather than a physics query, so it costs nothing per frame
        /// and cannot be confused by the player, pickups or scenery.
        /// </summary>
        private bool IsSpawnBlocked(Vector3 position)
        {
            float threshold = spawnClearanceRadius * spawnClearanceRadius;

            foreach (ZombieController zombie in _activeEnemies)
            {
                if (zombie == null) continue;

                if ((zombie.transform.position - position).sqrMagnitude < threshold) return true;
            }

            return false;
        }

        /// <summary>
        /// Milestone 1N.1 - resolves the whole archetype entry rather than just its prefab,
        /// so per-archetype spawn tuning travels with the type instead of being re-derived
        /// from its id at the call site. Returns null when the id is unknown; the caller
        /// then falls back to the basic prefab with a zero offset.
        /// </summary>
        private EnemyArchetype ResolveArchetype(string archetypeId)
        {
            if (!string.IsNullOrEmpty(archetypeId) && archetypes != null)
            {
                for (int i = 0; i < archetypes.Count; i++)
                {
                    EnemyArchetype archetype = archetypes[i];

                    if (archetype != null
                        && archetype.prefab != null
                        && (string.Equals(archetype.id, archetypeId, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(archetype.stableId, archetypeId, StringComparison.Ordinal)))
                    {
                        return archetype;
                    }
                }

                Debug.LogWarning(
                    $"Unknown enemy archetype '{archetypeId}'; falling back to the basic zombie.",
                    this);
            }

            return null;
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
            if (_spawnPaused) return; // 1Z QA fix #4 - don't start a new section during cinematic
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
                while (_spawnPaused) yield return null; // 1Z QA fix #4 - pause spawning during cinematic

                Vector3 position = GetSpawnPosition(i % 3);
                position.z += zShift;

                // Milestone 1N - the ONLY type-dependent step in the whole spawn path.
                // Everything after it is identical for every archetype, which is what keeps
                // completion counting, auto-aim and death handling type-agnostic.
                EnemyArchetype archetype = ResolveArchetype(spawnOrder[i]);
                ZombieController prefab = archetype != null && archetype.prefab != null
                    ? archetype.prefab
                    : zombiePrefab;

                if (prefab == null) continue;

                // Milestone 1N.1 - the archetype may enter the fight closer than the band.
                // An offset of 0 leaves 'position' bit-for-bit untouched, so the basic
                // zombie still spawns exactly where it always did.
                float offset = archetype != null ? archetype.spawnDistanceOffset : 0f;

                // Milestone 1N.2 - the standoff is archetype data too, not a type check. An
                // archetype that declares no override resolves to the global default, so BASIC
                // keeps the exact 12 unit corridor it has always had.
                float standoff = archetype != null
                    ? archetype.ResolveMinimumStandoff(minimumSpawnStandoff)
                    : minimumSpawnStandoff;

                // Milestone 1O-R - remember the pre-offset band position purely so the
                // diagnostics event can report requested-vs-applied offset. Observation only.
                Vector3 bandPosition = position;

                if (offset > 0f) position = ApplyForwardSpawnOffset(position, offset, standoff);

                ZombieController zombie = Instantiate(prefab, position, Quaternion.identity);
                zombie.SetTarget(playerTarget, playerHealth);
                zombie.Died += HandleEnemyDied;
                RegisterEnemy(zombie);

                // Milestone 1O - observation hook. Raised after the enemy is fully set up
                // and tracked, so nothing about the spawn can be changed by a listener.
                EnemySpawned?.Invoke(zombie, new EnemySpawnReport(
                    archetype != null ? archetype.id : EnemyArchetypeId.Basic,
                    sectionIndex, position, bandPosition, offset, standoff));

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

            // 1X.5 QA fix #1 - AUTHORITATIVE combat shutdown on success. StopEncounter freezes
            // every living enemy (SuspendCombat), stops all spawn coroutines, and sets _cancelled so
            // no further spawning (sections OR reinforcements) can occur - BEFORE the event fires,
            // so every EncounterCompleted observer (Mission Complete UI, reward service, etc.) sees
            // a fully inert encounter. Previously this was delegated to MissionCompleteController via
            // the event chain; now the encounter authority owns it. MissionCompleteController's own
            // StopEncounter call is now a harmless repeat.
            StopEncounter();

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
        /// 1Z QA fix #4 - registers a newly created enemy in the active set and, if a cinematic
        /// pause is currently active, immediately suspends its combat so it cannot attack the
        /// player before the cinematic unlock. This is the safety net that catches ANY spawn
        /// path (section coroutine, reinforcement seam, legacy waves) — no new hostile can
        /// damage the player during a cinematic lock, regardless of which code created it.
        /// </summary>
        private void RegisterEnemy(ZombieController zombie)
        {
            if (zombie == null) return;
            _activeEnemies.Add(zombie);

            if (_spawnPaused)
            {
                zombie.SuspendCombat();
            }
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

            // 1X.5 QA fix #1 - freeze all living enemies on Game Over too (consistent encounter
            // termination): they stop chasing/attacking the dead player. They remain visible.
            foreach (ZombieController zombie in _activeEnemies)
            {
                if (zombie != null)
                {
                    zombie.SuspendCombat();
                }
            }
        }

        // ------------------------------------------------------------------
        // Milestone 1Z QA fix #2 - TEMPORARY cinematic combat suspend/resume.
        // These are distinct from the permanent encounter-end suspension: they are reversible
        // and do NOT cancel/complete the encounter. The GameplayLockAuthority calls these on
        // cinematic lock/unlock so enemies freeze during a full cinematic and resume after.
        // ------------------------------------------------------------------

        /// <summary>Temporary spawn pause flag for cinematic locks.</summary>
        private bool _spawnPaused;

        /// <summary>True while a temporary cinematic spawn-pause is active.</summary>
        public bool IsSpawnPaused => _spawnPaused;

        /// <summary>
        /// TEMPORARY cinematic suspend: freezes all active enemies (they stop chasing/attacking)
        /// and pauses spawning, WITHOUT ending the encounter. Called by GameplayLockAuthority.
        /// </summary>
        public void SuspendActiveEnemiesForCinematic()
        {
            _spawnPaused = true;

            foreach (ZombieController zombie in _activeEnemies)
            {
                if (zombie != null)
                {
                    zombie.SuspendCombat();
                }
            }
        }

        /// <summary>
        /// TEMPORARY cinematic resume: resumes all active enemies and unpauses spawning, but
        /// ONLY if the encounter has not permanently ended (success/death). After encounter end
        /// enemies stay frozen. Called by GameplayLockAuthority.
        /// </summary>
        public void ResumeActiveEnemiesAfterCinematic()
        {
            _spawnPaused = false;

            // Do NOT resume if the encounter has permanently ended — enemies stay frozen.
            if (_cancelled || _encounterComplete) return;

            foreach (ZombieController zombie in _activeEnemies)
            {
                if (zombie != null && zombie.IsAlive)
                {
                    zombie.ResumeCombat();
                }
            }
        }
        private void UnsubscribeAll()
        {
            foreach (ZombieController zombie in _activeEnemies) if (zombie != null) zombie.Died -= HandleEnemyDied;
            _activeEnemies.Clear();
        }
    }
}
