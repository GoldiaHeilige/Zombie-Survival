using DG.Tweening;
using UnityEngine;

public class SettingsController : MonoBehaviour
{
    public static SettingsController Instance { get; private set; }

    [Header("Root (CanvasGroup)")]
    [SerializeField] private CanvasGroup root;      // canvas group của Settings panel root
    [SerializeField] private float fadeTime = 0.15f;

    [Header("Tabs")]
    [SerializeField] private GameObject panelAudio;
    [SerializeField] private GameObject panelKeyboard;

    [Header("Refs")]
    [SerializeField] private AudioSettingsUI audioUI;  // component thật của bạn
    [SerializeField] private GraphicsSettingsUI graphicsUI;

    private Tween _tween;
    private PauseMenuController _pauseMenu;

    public bool IsOpen
    => root != null && root.blocksRaycasts && root.alpha > 0.001f;


    private void Awake()
    {
        // Scene-local: chỉ set Instance nếu chưa có, nếu có thì override theo scene mới
        Instance = this;

        // đảm bảo lúc vào scene nó đang “ẩn”
        SetVisible(false, instant: true);
        ShowTabAudio(); // default
    }

    public void BindPauseMenu(PauseMenuController pauseMenu) => _pauseMenu = pauseMenu;

    public void OpenSettings()
    {
        audioUI?.ReloadFromData();
        graphicsUI?.ReloadFromData(); // THÊM
        SetVisible(true, instant: false);
    }

    public void CloseSettings()
    {
        SetVisible(false, instant: false);
        _pauseMenu?.ReturnFromSettings(); // bạn đã có hàm này :contentReference[oaicite:3]{index=3}
    }

    public void Apply()
    {
        Debug.Log($"[SettingsController] Apply pressed. audioUI={(audioUI ? "OK" : "NULL")} graphicsUI={(graphicsUI ? "OK" : "NULL")}");

        audioUI?.SaveChanges();
        graphicsUI?.SaveChanges();

        CloseSettings();
    }

    public void Cancel()
    {
        audioUI?.ReloadFromData();
        graphicsUI?.ReloadFromData(); // THÊM
        CloseSettings();
    }

    public void ShowTabAudio()
    {
        if (panelAudio) panelAudio.SetActive(true);
        if (panelKeyboard) panelKeyboard.SetActive(false);
    }

    public void ShowTabKeyboard()
    {
        if (panelAudio) panelAudio.SetActive(false);
        if (panelKeyboard) panelKeyboard.SetActive(true);
    }

    private void SetVisible(bool on, bool instant)
    {
        if (!root) return;

        _tween?.Kill();

        if (instant)
        {
            root.alpha = on ? 1f : 0f;
            root.interactable = on;
            root.blocksRaycasts = on;
            return;
        }

        // Khi đang fade in thì cho block raycast luôn để khỏi click xuyên
        root.blocksRaycasts = true;
        root.interactable = false;

        float target = on ? 1f : 0f;
        _tween = root.DOFade(target, fadeTime).SetUpdate(true).OnComplete(() =>
        {
            root.interactable = on;
            root.blocksRaycasts = on;
        });
    }

    public void ForceCloseForGameOver()
    {
        _tween?.Kill();
        SetVisible(false, instant: true);
        _pauseMenu = null; // tránh ReturnFromSettings sau này
    }

}
