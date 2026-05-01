// PlayerSpawnManager.cs
using System.Collections.Generic;
using UnityEngine;

public class PlayerSpawnManager : MonoBehaviour
{
    public static PlayerSpawnManager Instance { get; private set; }

    [SerializeField] private List<Transform> spawnPoints = new List<Transform>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public Transform GetRandomSpawnPoint()
    {
        if (spawnPoints.Count == 0)
        {
            Debug.LogWarning("[PlayerSpawnManager] No spawn points assigned. Using default position.");
            return null;
        }

        // Lọc các spawn point không có player ở gần
        List<Transform> availablePoints = new List<Transform>();
        foreach (var point in spawnPoints)
        {
            if (point == null) continue;

            // Kiểm tra xem có player nào gần điểm spawn không
            bool isOccupied = false;
            Collider[] colliders = Physics.OverlapSphere(point.position, 2f);
            foreach (var collider in colliders)
            {
                if (collider.CompareTag("Player"))
                {
                    isOccupied = true;
                    break;
                }
            }

            if (!isOccupied)
                availablePoints.Add(point);
        }

        // Nếu không có điểm nào trống, dùng tất cả
        if (availablePoints.Count == 0)
            availablePoints = spawnPoints;

        // Random chọn một điểm
        int randomIndex = Random.Range(0, availablePoints.Count);
        return availablePoints[randomIndex];
    }

    public void AddSpawnPoint(Transform spawnPoint)
    {
        if (!spawnPoints.Contains(spawnPoint))
            spawnPoints.Add(spawnPoint);
    }

    public void RemoveSpawnPoint(Transform spawnPoint)
    {
        spawnPoints.Remove(spawnPoint);
    }

    // Để debug trong Editor
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        foreach (var point in spawnPoints)
        {
            if (point != null)
            {
                Gizmos.DrawWireSphere(point.position, 0.5f);
                Gizmos.DrawIcon(point.position, "SpawnPoint.png", true);
            }
        }
    }
}