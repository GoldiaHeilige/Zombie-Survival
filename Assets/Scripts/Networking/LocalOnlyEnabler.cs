using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Bật/tắt các component & GameObject chỉ dành cho LOCAL input owner.
/// - Gắn script này trên SHELL (có NetworkObject).
/// - Gọi Apply(Object, coreRoot) sau khi Shell đã Instantiate Core.
/// - Nếu autoDiscover=true: nó tự tìm các comp phổ biến trong phạm vi coreRoot.
/// </summary>
public class LocalOnlyEnabler : NetworkBehaviour
{
    [Header("Auto discover")]
    [Tooltip("Tự tìm các comp phổ biến trong phạm vi searchRoot")]
    public bool autoDiscover = true;

    [Tooltip("Giới hạn phạm vi tìm — thường là Core (Transform) vừa spawn")]
    public Transform searchRoot;

    [Header("Manual assign (tuỳ chọn)")]
    [SerializeField] private List<UnityEngine.Behaviour> behaviours = new();
    [SerializeField] private List<GameObject> gameObjects = new();

    bool? _lastState;

    /// <summary>
    /// Gọi từ NetworkPlayerShell sau khi Instantiate Core:
    /// enabler.searchRoot = core.transform; enabler.Apply(Object, core.transform);
    /// </summary>
    public void Apply(NetworkObject authoritySource, Transform coreRoot = null)
    {
        if (authoritySource == null) return;

        if (coreRoot != null) searchRoot = coreRoot;

        bool isLocal = authoritySource.HasInputAuthority;

        // 🔧 Vô hiệu hóa PlayerInput ở các player remote
        if (searchRoot != null)
        {
            var inputs = searchRoot.GetComponentsInChildren<UnityEngine.InputSystem.PlayerInput>(true);
            foreach (var pi in inputs)
            {
                if (pi == null) continue;
                if (!isLocal)
                {
                    try { pi.DeactivateInput(); } catch { }
                    pi.enabled = false;
                }
                else
                {
                    try { pi.ActivateInput(); } catch { }
                    pi.enabled = true;
                }
            }
        }


        // Tự điền danh sách 1 lần nếu đang để trống
        if (autoDiscover && searchRoot != null && behaviours.Count == 0 && gameObjects.Count == 0)
            DiscoverDefault(searchRoot);

        // Bật/tắt các Behaviour
        for (int i = 0; i < behaviours.Count; i++)
        {
            var b = behaviours[i];
            if (!b) continue;
            b.enabled = isLocal;
        }

        // Active/Inactive các GameObject
        for (int i = 0; i < gameObjects.Count; i++)
        {
            var go = gameObjects[i];
            if (!go) continue;
            go.SetActive(isLocal);
        }

        _lastState = isLocal;
#if UNITY_EDITOR
        // Debug nhẹ để bạn xem nó bắt được gì
        Debug.Log($"[LocalOnlyEnabler] {(isLocal ? "LOCAL" : "REMOTE")} — toggled {behaviours.Count} behaviours, {gameObjects.Count} GOs", this);
#endif
    }

    /// <summary>
    /// Auto discover theo TÊN CLASS script & một số GO thường gặp (HUD, WeaponView_FPS).
    /// Bạn có thể tuỳ biến rule ngay tại đây cho đúng project.
    /// </summary>
    void DiscoverDefault(Transform root)
    {
        // 1) Tìm các Behaviour cần tắt ở remote (chỉ local mới bật)
        var foundBehaviours = root.GetComponentsInChildren<UnityEngine.Behaviour>(true);
        foreach (var b in foundBehaviours)
        {
            if (b == null) continue;
            var typeName = b.GetType().Name;

            // Input / Movement chủ động
            if (typeName == "PlayerInput")
            { behaviours.Add(b); continue; }

            // Camera overlay / input provider
            if (typeName.Contains("WeaponCamera"))
            { behaviours.Add(b); continue; }

            // Vũ khí: các dao động local-only
            if (typeName.Contains("WeaponSway") || typeName.Contains("SwayBob") || typeName.Contains("WeaponKick"))
            { behaviours.Add(b); continue; }

            // AudioListener trên camera local (nếu có trong core)
            if (typeName == "AudioListener")
            { behaviours.Add(b); continue; }
        }

        // 2) Tìm GO cần ẩn ở remote (HUD/FPSView)
        var allTs = root.GetComponentsInChildren<Transform>(true);
        foreach (var t in allTs)
        {
            string n = t.name;
            if (n.Contains("HUD") || n.Contains("WeaponView_FPS"))
                gameObjects.Add(t.gameObject);
        }
    }
}
