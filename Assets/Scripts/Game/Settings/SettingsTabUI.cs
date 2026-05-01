using UnityEngine;
using UnityEngine.UI;

public class SettingsTabUI : MonoBehaviour
{
    [Header("Tabs")]
    public Button tabAudio;
    public Button tabKeybroad;

    [Header("Panels")]
    public GameObject panelAudio;
    public GameObject panelKeybroad;

    [Header("Buttons")]
    public Button btnApply;
    public Button btnCancel;
    public Button btnBack; // Thêm nút Back

    [Header("Optional (MainMenu)")]
    [SerializeField] private TT.UI.MenuController menuController; // chỉ set ở MainMenu scene

    private AudioSettingsUI audioUI;

    void Awake()
    {
        // Bind tabs
        tabAudio.onClick.AddListener(() => Switch(true));
        tabKeybroad.onClick.AddListener(() => Switch(false));

        // Find AudioSettingsUI in children
        audioUI = GetComponentInChildren<AudioSettingsUI>(true);

        // Bind buttons
        if (btnApply)
            btnApply.onClick.AddListener(OnApply);

        if (btnCancel)
            btnCancel.onClick.AddListener(OnCancel);

        if (btnBack) // Bind nút Back
            btnBack.onClick.AddListener(OnCancel);

        // Default: show Audio panel
        Switch(true);
    }

    void Switch(bool audio)
    {
        panelAudio.SetActive(audio);
        panelKeybroad.SetActive(!audio);

        tabAudio.interactable = !audio;
        tabKeybroad.interactable = audio;
    }

    private void OnApply()
    {
        if (audioUI) audioUI.SaveChanges();
        CloseUI();
    }

    private void OnCancel()
    {
        if (audioUI) audioUI.ReloadFromData();
        CloseUI();
    }

    private void CloseUI()
    {
        // MainMenu: đóng bằng tween của MenuController
        if (menuController != null)
        {
            menuController.HidePanelFromExternal(); // sẽ tạo hàm public ở MenuController (bước dưới)
            return;
        }

        // GameScene: đóng bằng SettingsController (fade canvas group + return pause)
        SettingsController.Instance?.CloseSettings();
    }
}