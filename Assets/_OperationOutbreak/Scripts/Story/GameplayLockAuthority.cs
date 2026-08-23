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
    /// Lock state is instance-only (not static): a scene reload / Retry naturally clears it.
    /// If the runner is destroyed mid-lock, OnDestroy releases the lock so no stale lock
    /// survives.
    /// </summary>
    public sealed class GameplayLockAuthority : MonoBehaviour
    {
        public static GameplayLockAuthority Instance { get; private set; }

        [SerializeField] private PlayerController playerController;
        [SerializeField] private WeaponController weaponController;

        private int _lockCount;

        /// <summary>True while at least one lock is active (gameplay suspended).</summary>
        public bool IsLocked => _lockCount > 0;

        private void Awake()
        {
            Instance = this;
            if (playerController == null) playerController = FindAnyObjectByType<PlayerController>();
            if (weaponController == null) weaponController = FindAnyObjectByType<WeaponController>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            // Safety: release any lingering lock so a destroyed runner can't strand the player.
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
            if (playerController != null) playerController.SuspendMovement();
            if (weaponController != null) weaponController.SuspendFiring();
        }

        private void RestoreGameplay()
        {
            // Re-enable movement and firing. The suspend methods set internal flags; re-enabling
            // is simply setting enabled = true (the controllers' OnEnable re-seeds their state).
            if (playerController != null) playerController.enabled = true;
            if (weaponController != null) weaponController.enabled = true;
        }
    }
}
