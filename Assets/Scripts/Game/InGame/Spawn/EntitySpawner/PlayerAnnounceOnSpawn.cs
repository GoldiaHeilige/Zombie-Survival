using System.Collections;
using UnityEngine;

/// <summary>
/// Thông báo cho SpawnManager biết đây là (local) player.
/// Hoạt động cho cả single và multi (khi player được spawn).
/// </summary>
public class PlayerAnnounceOnSpawn : MonoBehaviour
{
    private IEnumerator Start()
    {
        // Đảm bảo Tag "Player"
        SafeEnsurePlayerTag();

        // Thử bind nhiều khung hình đầu để tránh lệch thứ tự khởi tạo
        for (int i = 0; i < 30; i++)
        {
            var sm = FindAnyObjectByType<SpawnManager>();
            if (sm != null)
            {
                sm.BindLocalPlayer(transform); // dùng API mới ở SpawnManager
                yield break;
            }
            yield return null;
        }

        Debug.LogWarning("[PlayerAnnounce] SpawnManager not found in scene (after retries).");
    }

    private void SafeEnsurePlayerTag()
    {
        if (CompareTag("Player")) return;
        try
        {
            gameObject.tag = "Player";
        }
        catch
        {
            Debug.LogWarning("[PlayerAnnounce] Could not set tag 'Player'. Make sure the 'Player' tag exists in Tags & Layers.");
        }
    }
}
