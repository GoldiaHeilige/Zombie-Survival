// SessionCleanup.cs
using UnityEngine;
using TT; // Thêm namespace của AudioManager nếu cần
using TT.UI;

public static class SessionCleanup
{
    // WHITELIST: Các component cần được bảo vệ khỏi cleanup
    private static readonly System.Type[] PROTECTED_TYPES = {
        typeof(AudioManager),
        typeof(SettingsController),
        typeof(SceneTransitionFader),
        // Sau này có thể thêm: typeof(SettingsController), etc.
    };

    /// <summary>
    /// Dọn trạng thái giữa các trận.
    /// Hiện tại chỉ clear LastGameResult; các object DontDestroyOnLoad đã được
    /// Lobby/Menu tự quản lý (NetworkRunner, v.v.).
    /// </summary>
    public static void CleanupAll()
    {
        AudioManager.Instance?.StopAllExceptCategory(AudioCategory.UI);
        // Reset kết quả trận
        LastGameResult.Clear();

        // Đảm bảo time + cursor về default
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Xoá toàn bộ object trong scene DontDestroyOnLoad
        var allObjects = Object.FindObjectsByType<GameObject>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (var go in allObjects)
        {
            if (!go) continue;

            var scene = go.scene;
            if (!scene.IsValid()) continue;

            // Các object DontDestroyOnLoad nằm trong scene đặc biệt này
            if (scene.name == "DontDestroyOnLoad")
            {
                // Chỉ xử lý ROOT OBJECT
                if (go.transform.parent != null)
                    continue;

                bool protect = false;
                foreach (var protectedType in PROTECTED_TYPES)
                {
                    if (go.GetComponent(protectedType) != null)
                    {
                        protect = true;
                        break;
                    }
                }

                if (!protect)
                {
                    Object.Destroy(go);
                    Debug.Log($"[SessionCleanup] Destroyed root DDOL object: {go.name}");
                }
                else
                {
                    Debug.Log($"[SessionCleanup] Protected root DDOL: {go.name}");
                }
            }
        }

        Debug.Log("[SessionCleanup] Cleanup completed - AudioManager preserved");
    }
}