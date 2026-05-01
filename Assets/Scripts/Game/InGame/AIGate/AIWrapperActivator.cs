using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bật/tắt các nhóm object theo môi trường chơi dựa trên GameSession.Mode:
///   - AppPlayMode.Single  → bật nhóm Singleplayer
///   - AppPlayMode.Host/Client → bật nhóm Multiplayer
///
/// Dùng cho hệ AI/Wave để enable đúng container SP hay MP.
/// Trong nhóm MP, mỗi script tự kiểm tra Host/Client (IsServer/HasStateAuthority) nếu cần.
///
/// Gợi ý: Sau khi Main Menu/Lobby set GameSession.Mode, hãy gọi
/// AIWrapperActivator.ReapplyAllInScene() hoặc gọi Reapply() trên component này.
/// </summary>
public class AIWrapperActivator : MonoBehaviour
{
    [Header("Groups")]
    public List<GameObject> enableInSingleplayer = new();
    public List<GameObject> enableInMultiplayer = new();

    [Header("Options")]
    [Tooltip("Nếu bật, mọi object không thuộc nhóm hiện hành sẽ bị disable.")]
    public bool disableOthers = true;

    [Tooltip("Tự động Apply khi component được bật.")]
    public bool applyOnEnable = true;

    void OnEnable()
    {
        if (applyOnEnable) ApplyByGameSession();
    }

    void Start()
    {
        if (!applyOnEnable) ApplyByGameSession();
    }

    /// <summary>Gọi thủ công nếu bạn muốn re-apply sau khi GameSession.Mode thay đổi.</summary>
    [ContextMenu("Re-Apply (Use GameSession.Mode)")]
    public void Reapply() => ApplyByGameSession();

    void ApplyByGameSession()
    {
        bool isSingle = (GameSession.Mode == AppPlayMode.Single);

        SetActiveList(enableInSingleplayer, isSingle);
        SetActiveList(enableInMultiplayer, !isSingle);

        if (disableOthers)
        {
            var activeSet = new HashSet<GameObject>();
            AddRange(activeSet, isSingle ? enableInSingleplayer : enableInMultiplayer);

            var all = new HashSet<GameObject>();
            AddRange(all, enableInSingleplayer);
            AddRange(all, enableInMultiplayer);

            foreach (var go in all)
            {
                if (!go) continue;
                if (!activeSet.Contains(go)) go.SetActive(false);
            }
        }

        Debug.Log($"[AIWrapperActivator] Applied by GameSession.Mode = {GameSession.Mode}", this);
    }

    static void SetActiveList(List<GameObject> list, bool active)
    {
        if (list == null) return;
        foreach (var go in list) if (go) go.SetActive(active);
    }

    static void AddRange(HashSet<GameObject> set, List<GameObject> list)
    {
        if (list == null) return;
        foreach (var go in list) if (go) set.Add(go);
    }

    // ------- tiện ích toàn cục (tuỳ chọn) -------
    /// <summary>
    /// Re-apply cho tất cả AIWrapperActivator trong scene hiện tại.
    /// Gọi hàm này sau khi bạn đổi GameSession.Mode ở Main Menu/Lobby.
    /// </summary>
    public static void ReapplyAllInScene()
    {
        var list = Object.FindObjectsByType<AIWrapperActivator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var it in list) it.ApplyByGameSession();
    }
}
