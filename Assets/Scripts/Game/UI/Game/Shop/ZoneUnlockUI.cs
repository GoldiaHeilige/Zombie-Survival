using UnityEngine;
using TMPro;

/// <summary>
/// UI hiển thị khi player nhìn vào 1 khu vực có thể unlock bằng Points.
/// Ví dụ: "Press F Use 750 Points To Unlock Storage Room".
/// </summary>
public class ZoneUnlockUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private CanvasGroup rootGroup;
    [SerializeField] private TextMeshProUGUI label;

    [Header("Settings")]
    [Tooltip("Label của phím Interact (ví dụ: F, E, [X], ...)")]
    [SerializeField] private string interactKeyLabel = "F";

    [Header("Rich Text Styling")]
    [SerializeField] private Color highlightCyan = new Color(0.55f, 1f, 1f, 1f);

    private string Cyan(string s)
    {
        string hex = ColorUtility.ToHtmlStringRGBA(highlightCyan);
        return $"<color=#{hex}>{s}</color>";
    }
    private static string Bold(string s) => $"<b>{s}</b>";


    [Header("Debug")]
    [SerializeField] private bool startHidden = true;

    private Transform _playerRoot;
    private ZoneUnlockablePoints _currentZone;

    private void Awake()
    {
        if (startHidden)
            Hide();
    }

    /// <summary>
    /// Gọi từ HUDLocalBinder để gắn với local player.
    /// Hiện tại chủ yếu để giữ reference, sau này có thể dùng thêm.
    /// </summary>
    public void Bind(Transform playerRoot)
    {
        _playerRoot = playerRoot;
        Hide();
    }

    /// <summary>
    /// Gọi khi player đang nhìn vào 1 ZoneUnlockablePoints.
    /// Không check đủ/thiếu tiền – UI luôn hiển thị:
    /// "Press F Use 1234 Points To Unlock XXX".
    /// </summary>
    public void ShowFor(ZoneUnlockablePoints zone)
    {
        _currentZone = zone;

        if (zone == null || zone.IsUnlocked)
        {
            Hide();
            return;
        }

        if (label != null)
        {
            string key = Bold(interactKeyLabel);
            string zoneName = Bold(Cyan(zone.displayName));
            string cost = Cyan(zone.Cost.ToString());

            label.text = $"Press [{key}] to unlock {zoneName} with {cost} points";
        }

        if (rootGroup != null)
        {
            rootGroup.alpha = 1f;
            rootGroup.interactable = false;
            rootGroup.blocksRaycasts = false;
        }
        else
        {
            gameObject.SetActive(true);
        }
    }

    /// <summary>Gọi khi player không còn nhìn vào zone nào nữa.</summary>
    public void Hide()
    {
        _currentZone = null;

        if (rootGroup != null)
        {
            rootGroup.alpha = 0f;
            rootGroup.interactable = false;
            rootGroup.blocksRaycasts = false;
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Option: Interactor có thể query lại zone đang hiển thị nếu cần.
    /// </summary>
    public ZoneUnlockablePoints GetCurrentZone() => _currentZone;
}
