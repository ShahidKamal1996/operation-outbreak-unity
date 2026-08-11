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

        [Header("One Prototype Encounter")]
        [SerializeField] private Vector3 spawnPosition = new Vector3(0f, 1f, 15f);

        private bool _hasSpawned;

        private void Start()
        {
            if (_hasSpawned || zombiePrefab == null || playerTarget == null)
            {
                return;
            }

            ZombieController zombie = Instantiate(zombiePrefab, spawnPosition, Quaternion.identity);
            zombie.SetTarget(playerTarget);
            _hasSpawned = true;
        }
    }
}
