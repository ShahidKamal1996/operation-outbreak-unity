using OperationOutbreak.Enemies;
using OperationOutbreak.Player;
using OperationOutbreak.Weapons;
using UnityEngine;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z — the ONE authority for suspending gameplay during cinematics. Uses a
    /// reference-COUNT so nested lock calls don't accidentally resume gameplay early. Reuses
    /// the EXISTING suspend methods (PlayerController.SuspendMovement, WeaponController.
    /// SuspendFiring) and restores them on the final unlock.
    ///
    /// 1Z QA fix #2 — a FULL CINEMATIC lock now ALSO temporarily suspends all active enemy
    /// combat (via EnemySpawner.SuspendActiveEnemiesForCinematic) and pauses spawning. On the
    /// final unlock, enemies resume and spawning unpauses — but ONLY if the encounter hasn't
    /// permanently ended (the EnemySpawner guards against resuming after success/death).
    /// This prevents enemies from attacking the locked player during a cinematic.
    ///
    /// Lock state is instance-only (not static): a scene reload / Retry naturally clears it.
    /// If the runner is destroyed mid-lock, OnDestroy releases the lock so no stale lock survives.
    /// </summary>
    public sealed class GameplayLockAuthority : MonoBehaviour
    {
        public static GameplayLockAuthority Instance { get; private set; }

        [SerializeField] private PlayerController playerController;
        [SerializeField] private WeaponController weaponController;
        [SerializeField] private EnemySpawner enemySpawner;

        private int _lockCount;

        /// <summary>True while at least one lock is active (gameplay + combat suspended).</summary>
        public bool IsLocked => _lockCount > 0;

        private void Awake()
        {
            Instance = this;
            if (playerController == null) playerController = FindAnyObjectByType<PlayerController>();
            if (weaponController == null) weaponController = FindAnyObjectByType<WeaponController>();
            if (enemySpawner == null) enemySpawner = FindAnyObjectByType<EnemySpawner>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_lockCount > 0)
            {
                _lockCount = 0;
                RestoreGameplay();
            }
        }

        /// <summary>Acquire one gameplay lock. Idempotent per caller via the count.</summary>
        public void Lock()
        {
            _lockCount++;
            if (_lockCount == 1)
            {
                ApplyLock();
            }
        }

        /// <summary>Release one gameplay lock. Gameplay resumes only when the count reaches 0.</summary>
        public void Unlock()
        {
            if (_lockCount <= 0) return;
            _lockCount--;
            if (_lockCount == 0)
            {
                RestoreGameplay();
            }
        }

        /// <summary>Force-release all locks (skip, interruption, destroy).</summary>
        public void ForceUnlock()
        {
            if (_lockCount > 0)
            {
                _lockCount = 0;
                RestoreGameplay();
            }
        }

        private void ApplyLock()
        {
            // 1Z QA fix #3 - use TEMPORARY cinematic flags (separate from the permanent
            // SuspendMovement / SuspendFiring used by Mission Complete / Game Over).
            if (playerController != null) playerController.SetCinematicMovementLock(true);
            if (weaponController != null) weaponController.SetCinematicFiringLock(true);

            // 1Z QA fix #2 - also freeze enemies + pause spawning so they can't attack during
            // the cinematic. This is TEMPORARY (the encounter is NOT ended/cancelled).
            if (enemySpawner != null) enemySpawner.SuspendActiveEnemiesForCinematic();
        }

        private void RestoreGameplay()
        {
            // 1Z QA fix #3 - release the TEMPORARY cinematic flags. The permanent flags
            // (_movementSuspended / _firingSuspended from Mission Complete, _isDead /
            // _isOwnerDead from Game Over) are NOT touched here — if the encounter ended
            // during the cinematic, the permanent flags stay set and gameplay stays stopped.
            if (playerController != null) playerController.SetCinematicMovementLock(false);
            if (weaponController != null) weaponController.SetCinematicFiringLock(false);

            // 1Z QA fix #2 - resume enemies + unpause spawning, but ONLY if the encounter is
            // still active. The EnemySpawner guards against resuming after encounter end.
            if (enemySpawner != null) enemySpawner.ResumeActiveEnemiesAfterCinematic();
        }
    }
}
