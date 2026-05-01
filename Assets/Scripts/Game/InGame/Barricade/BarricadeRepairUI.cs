using UnityEngine;
using TMPro;

/// <summary>
/// UI hiển thị khi player đứng cạnh 1 Barricade có thể repair.
/// Ví dụ: "Press [F] to repair barricade".
/// Giống style của ZoneUnlockUI / BuyUI, dùng 1 HUD duy nhất cho local player.
/// </summary>
public class BarricadeRepairUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private CanvasGroup rootGroup;
    [SerializeField] private TextMeshProUGUI label;

    [Header("Settings")]
    [Tooltip("Label của phím Interact (ví dụ: F, E, [X], ...)")]
    [SerializeField] private string interactKeyLabel = "F";

    [Header("Debug")]
    [SerializeField] private bool startHidden = true;

    private Transform _playerRoot;
    private BarricadeWindow _currentWindow;

    public static BarricadeRepairUI Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (startHidden)
            Hide();
    }

    /// <summary>
    /// Gọi từ HUDLocalBinder để gắn với local player (giống BuyUI / ZoneUnlockUI).
    /// </summary>
    public void Bind(Transform playerRoot)
    {
        _playerRoot = playerRoot;
        Hide();
    }

    /// <summary>
    /// Gọi khi player đang "trong trigger" của 1 BarricadeWindow.
    /// Chỉ hiện nếu Barricade còn slot trống để repair.
    /// </summary>
    public void ShowFor(BarricadeWindow window)
    {
        _currentWindow = window;

        if (window == null || !window.HasEmptySlot())
        {
            Hide();
            return;
        }

        if (label != null)
        {
            label.text = $"Press [{interactKeyLabel}] to repair barricade";
            // Dịch: "Nhấn [{interactKeyLabel}] để sửa Barricade"
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

    /// <summary>Gọi khi player không còn đứng trong trigger Barricade này nữa.</summary>
    public void HideFor(BarricadeWindow window)
    {
        if (window != null && window != _currentWindow)
            return; // 1 cửa khác gọi hide → bỏ qua

        Hide();
    }

    private void Hide()
    {
        _currentWindow = null;

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
    /// Cho trigger / interactor query lại nếu cần.
    /// </summary>
    public BarricadeWindow GetCurrentWindow() => _currentWindow;
}
