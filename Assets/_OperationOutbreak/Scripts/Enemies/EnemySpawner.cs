using OperationOutbreak.Player;
using UnityEngine;

namespace OperationOutbreak.Enemies
{
    /// <summary>One-shot prototype encounter spawner. It intentionally has no waves or respawn loop.</summary>
    [DisallowMultipleComponent]
    public sealed class EnemySpawner : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ZombieController zombiePrefab;
        [Tooltip("The actual Player transform, not PlayerSpawn or a visual child.")]
        [SerializeField] private Transform playerTarget;

        [Tooltip("Health on the actual Player root. Assigned explicitly so the zombie attack path cannot depend on a runtime component lookup.")]
        [SerializeField] private PlayerHealth playerHealth;

        [Header("One Prototype Encounter")]
        [SerializeField] private Vector3 spawnPosition = new Vector3(0f, 1f, 15f);

        private bool _hasSpawned;

        private void Start()
        {
            if (_hasSpawned || zombiePrefab == null || playerTarget == null || playerHealth == null)
            {
                return;
            }

            ZombieController zombie = Instantiate(zombiePrefab, spawnPosition, Quaternion.identity);
            zombie.SetTarget(playerTarget, playerHealth);
            _hasSpawned = true;
        }
    }
}
