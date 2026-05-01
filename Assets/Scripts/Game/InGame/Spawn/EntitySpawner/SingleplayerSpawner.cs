// SingleplayerSpawner.cs (đã sửa)
using UnityEngine;

public class SingleplayerSpawner : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private GameObject playerShellPrefab;

    [Header("Spawn Options")]
    [SerializeField] private bool useRandomSpawnPoint = true;
    [SerializeField] private Transform specificSpawnPoint; // Dùng nếu không random

    [Header("Options")]
    [SerializeField] private bool autoAddAnnounceOnSpawn = true;

    void OnEnable()
    {
        // Đảm bảo chỉ chạy 1 lần sau khi được bật
        if (IsRunnerRunning()) return;
        if (GameSession.Mode != AppPlayMode.Single) return;

        if (!playerShellPrefab)
        {
            Debug.LogError("[SingleplayerSpawner] Missing corePlayerPrefab!");
            return;
        }

        // Xác định vị trí spawn
        Transform spawnTransform = null;
        Vector3 pos;
        Quaternion rot;

        if (useRandomSpawnPoint)
        {
            // Lấy spawn point ngẫu nhiên từ manager
            if (PlayerSpawnManager.Instance != null)
            {
                spawnTransform = PlayerSpawnManager.Instance.GetRandomSpawnPoint();
            }

            if (spawnTransform == null)
            {
                Debug.LogWarning("[SingleplayerSpawner] No spawn point found, using Vector3.zero");
                pos = Vector3.zero;
                rot = Quaternion.identity;
            }
            else
            {
                pos = spawnTransform.position;
                rot = spawnTransform.rotation;
            }
        }
        else
        {
            // Dùng spawn point cụ thể
            if (specificSpawnPoint != null)
            {
                pos = specificSpawnPoint.position;
                rot = specificSpawnPoint.rotation;
            }
            else
            {
                Debug.LogWarning("[SingleplayerSpawner] No specific spawn point assigned, using Vector3.zero");
                pos = Vector3.zero;
                rot = Quaternion.identity;
            }
        }

        // Spawn player
        var player = Instantiate(playerShellPrefab, pos, rot);
        player.GetComponent<CoreWrapperActivator>()?.AttachWrappersForMode(false); // bật nhóm SINGLE

        var en = player.GetComponent<LocalOnlyEnabler>();
        if (en != null)
            en.Apply(null, player.transform); // (null => single/local), coreRoot = player.transform

        // Tự add Announce script nếu chưa có
        if (!player.TryGetComponent<PlayerAnnounceOnSpawn>(out _))
            player.AddComponent<PlayerAnnounceOnSpawn>();

        var refs = player.GetComponent<PlayerRefs>();
        var binder = FindFirstObjectByType<CameraBinder>(FindObjectsInactive.Exclude);
        if (refs && binder) binder.OnPlayerSpawned(refs);

     //   Debug.Log($"[SingleplayerSpawner] Spawned PlayerShell at position: {pos}");
    }

    bool IsRunnerRunning()
    {
#if FUSION_WEAVER
        return Fusion.NetworkRunner.Instances.Count > 0;
#else
        return false;
#endif
    }
}