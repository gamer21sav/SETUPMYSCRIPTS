using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Spawns and counts any prefab that can stand on a baked NavMesh.</summary>
[DisallowMultipleComponent]
public sealed class DummySpawner : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private GameObject dummyPrefab;
    [SerializeField] private Transform[] spawnPoints;

    [Header("Spawn settings")]
    [SerializeField, Min(0)] private int maxAlive = 5;
    [SerializeField, Min(0.1f)] private float spawnInterval = 3f;
    [SerializeField, Min(0f)] private float spawnRadius = 15f;
    [SerializeField, Min(0f)] private float navMeshSearchDistance = 10f;
    [SerializeField] private bool spawnOnStart = true;

    private readonly List<GameObject> alive = new List<GameObject>();
    private float nextSpawnTime;

    private void Start()
    {
        nextSpawnTime = spawnOnStart ? Time.time : Time.time + spawnInterval;
    }

    private void Update()
    {
        alive.RemoveAll(item => item == null);
        if (dummyPrefab == null || alive.Count >= maxAlive || Time.time < nextSpawnTime) return;

        nextSpawnTime = Time.time + Mathf.Max(0.1f, spawnInterval);
        if (TryGetSpawnPosition(out Vector3 position))
        {
            GameObject spawned = Instantiate(dummyPrefab, position,
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            alive.Add(spawned);
        }
    }

    private bool TryGetSpawnPosition(out Vector3 position)
    {
        Transform point = GetSpawnPoint();
        Vector3 origin = point != null ? point.position : transform.position;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * spawnRadius;
            Vector3 candidate = origin + new Vector3(offset.x, 2f, offset.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit,
                    Mathf.Max(0.1f, navMeshSearchDistance), NavMesh.AllAreas))
            {
                position = hit.position;
                return true;
            }
        }

        position = default;
        return false;
    }

    private Transform GetSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Length == 0) return null;
        for (int attempt = 0; attempt < spawnPoints.Length; attempt++)
        {
            Transform point = spawnPoints[Random.Range(0, spawnPoints.Length)];
            if (point != null) return point;
        }
        return null;
    }
}