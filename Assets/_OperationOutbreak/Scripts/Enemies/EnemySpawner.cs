using System.Collections;
using System.Collections.Generic;
using OperationOutbreak.Player;
using UnityEngine;

namespace OperationOutbreak.Enemies
{
    /// <summary>Small, finite three-wave prototype encounter. No endless spawning or director logic.</summary>
    [DisallowMultipleComponent]
    public sealed class EnemySpawner : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ZombieController zombiePrefab;
        [SerializeField] private Transform playerTarget;
        [SerializeField] private PlayerHealth playerHealth;

        [Header("Controlled Waves")]
        [Min(1)] [SerializeField] private int waveOneCount = 3;
        [Min(1)] [SerializeField] private int waveTwoCount = 4;
        [Min(1)] [SerializeField] private int waveThreeCount = 5;
        [Min(0f)] [SerializeField] private float initialDelay = 1f;
        [Min(0.01f)] [SerializeField] private float spawnInterval = 0.6f;
        [Min(0f)] [SerializeField] private float betweenWaveDelay = 2f;

        [Header("Lane Spawn Positions")]
        [SerializeField] private Vector3 leftSpawnPosition = new Vector3(-2.5f, 1f, 16f);
        [SerializeField] private Vector3 centreSpawnPosition = new Vector3(0f, 1f, 19f);
        [SerializeField] private Vector3 rightSpawnPosition = new Vector3(2.5f, 1f, 16f);

        private readonly HashSet<ZombieController> _activeEnemies = new HashSet<ZombieController>();
        private bool _cancelled;
        private bool _encounterComplete;

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
        private void HandleEnemyDied(ZombieController zombie)
        {
            if (zombie != null) zombie.Died -= HandleEnemyDied;
            _activeEnemies.Remove(zombie);
        }
        private void CancelEncounter()
        {
            if (_cancelled) return;
            _cancelled = true;
            StopAllCoroutines();
        }
        private void UnsubscribeAll()
        {
            foreach (ZombieController zombie in _activeEnemies) if (zombie != null) zombie.Died -= HandleEnemyDied;
            _activeEnemies.Clear();
        }
    }
}
