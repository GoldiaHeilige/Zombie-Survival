using UnityEngine;

/// Gắn lên Canvas. Bật/tắt overlay khi player Downed/Revived/Respawned/Dead.
/// Không phụ thuộc vào prefab player.
public class DownedUIBinder : MonoBehaviour
{
    [Header("UI Elements")]
    [Tooltip("Ảnh máu/vignette overlay khi gục")]
    public GameObject downedOverlay;
    public GameObject crosshair;
    [Tooltip("Panel hướng dẫn (giữ E để tự hồi, v.v.) - tuỳ chọn")]
    public GameObject hintPanel;

    [Header("Only affect local player?")]
    public bool onlyLocal = true;

/*    void OnEnable()
    {
        PlayerLifeController.OnDowned += OnDowned;
        PlayerLifeController.OnRevived += OnUp;
        PlayerLifeController.OnRespawned += OnUp;
        PlayerLifeController.OnDead += OnUp;   // tắt overlay khi chết hẳn (tuỳ bạn)
    }
    void OnDisable()
    {
        PlayerLifeController.OnDowned -= OnDowned;
        PlayerLifeController.OnRevived -= OnUp;
        PlayerLifeController.OnRespawned -= OnUp;
        PlayerLifeController.OnDead -= OnUp;
    }*/

    void OnDowned(PlayerLifeController plc)
    {
        if (onlyLocal && !IsLocal(plc)) return;
        if (downedOverlay) downedOverlay.SetActive(true);
        if (downedOverlay) crosshair.SetActive(true);
        if (hintPanel) hintPanel.SetActive(true);
    }
    void OnUp(PlayerLifeController plc)
    {
        if (onlyLocal && !IsLocal(plc)) return;
        if (downedOverlay) downedOverlay.SetActive(false);
        if (downedOverlay) crosshair.SetActive(false);
        if (hintPanel) hintPanel.SetActive(false);
    }

    // Tuỳ framework netcode của bạn – hiện để true là local.
    bool IsLocal(PlayerLifeController p) => true;
}
