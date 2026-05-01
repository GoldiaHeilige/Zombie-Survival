using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using UnityEngine.SceneManagement;
#if FUSION_WEAVER
using Fusion;
#endif

/// <summary>
/// Điều khiển UI GameOverPanel:
/// - Set text Map / Rounds / Time
/// - Đổ dữ liệu cho 4 row người chơi
/// - Nút Leave: thoát về MainMenu và cleanup
/// </summary>
public class GameOverPanel : MonoBehaviour
{
    [Header("Root (optional)")]
    [SerializeField] private GameObject root;        // chỉ để tham chiếu, KHÔNG SetActive
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Header Texts")]
    [SerializeField] private TMP_Text txtMapName;
    [SerializeField] private TMP_Text txtRound;
    [SerializeField] private TMP_Text txtTime;

    [Header("Player Rows (max 4)")]
    [SerializeField] private PlayerRowUI[] playerRows = new PlayerRowUI[4];

    [Header("Buttons")]
    [SerializeField] private Button btnLeave;

    // GameOverPanel.cs
    [Header("Hide HUD while GameOver (optional)")]
    [SerializeField] private CanvasGroup[] hudToHide;


#if FUSION_WEAVER
    private NetworkRunner runner;
#endif

    [System.Serializable]
    public class PlayerRowUI
    {
        public GameObject root;
        public TMP_Text txtName;
        public TMP_Text txtPoints;
        public TMP_Text txtKills;
        public TMP_Text txtRevive;
        public TMP_Text txtDowned;
    }

    // --------------------------------------------------------

    void Awake()
    {
        if (root == null) root = gameObject;
        if (!canvasGroup)
            canvasGroup = GetComponentInChildren<CanvasGroup>(true);

        // Panel luôn active trong scene, chỉ ẩn bằng CanvasGroup
        SetVisible(false);

        if (btnLeave != null)
        {
            btnLeave.onClick.RemoveAllListeners();
            btnLeave.onClick.AddListener(OnLeaveClicked);
        }
    }

    void Start()
    {
#if FUSION_WEAVER
        runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
#endif
    }

    // --------------------------------------------------------
    // Show API – DeadOverlay sẽ gọi cái này
    // --------------------------------------------------------

    /// <summary>
    /// Gọi trực tiếp khi GameOver → build snapshot → show.
    /// </summary>
    public void ShowFromCurrentRuntime()
    {
        RoundDirector director = RoundDirector.Instance;
        LastGameResult.BuildFromRuntime(director, GameOverReason.AllPlayersDead);
        ApplySnapshot(LastGameResult.Data);

        // Force đóng Pause + Settings nếu đang mở
        var pause = FindFirstObjectByType<PauseMenuController>();
        if (pause) pause.ForceCloseUIOnly();

        var settings = SettingsController.Instance;
        if (settings) settings.ForceCloseForGameOver();

        // Ép block gameplay input + mở chuột
        InputBlockerSystem.Add(InputBlocker.Full);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SetVisible(true);
    }

    /// <summary>
    /// Cho phép gọi nếu snapshot đã build sẵn.
    /// </summary>
    public void ShowFromSnapshot(GameResultSnapshot snap)
    {
        ApplySnapshot(snap);
        SetVisible(true);
    }

    /// <summary>
    /// Delay X giây rồi bật GameOverPanel.
    /// </summary>
    public void ShowFromCurrentRuntimeDelayed(float delay)
    {
        StartCoroutine(Co_ShowDelayed(delay));
    }

    IEnumerator Co_ShowDelayed(float delay)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, delay));
        ShowFromCurrentRuntime();
    }

    void SetVisible(bool visible)
    {
        if (!canvasGroup) return;

        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;

        SetHudVisible(!visible);

        if (visible)
        {
            InputBlockerSystem.Add(InputBlocker.Full);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            InputBlockerSystem.Remove(InputBlocker.Full);
        }
    }


    // --------------------------------------------------------
    // Binding dữ liệu vào UI
    // --------------------------------------------------------

    void ApplySnapshot(GameResultSnapshot snap)
    {
        if (snap == null)
        {
            Debug.LogWarning("[GameOverPanel] Snapshot NULL");
            return;
        }

        // ---- Header ----
        if (txtMapName) txtMapName.text = $"Map : {snap.mapName}";
        if (txtRound) txtRound.text = $"Rounds Survived : {snap.roundsSurvived}";
        if (txtTime) txtTime.text = $"Time Survived {MatchClock.FormatMMSS(snap.timeSurvivedSeconds)}";

        // ---- Player Rows ----
        int count = snap.players != null ? snap.players.Count : 0;
        count = Mathf.Min(count, playerRows.Length);

        for (int i = 0; i < playerRows.Length; i++)
        {
            bool active = (i < count);

            var row = playerRows[i];
            if (row.root) row.root.SetActive(active);

            if (!active) continue;

            var p = snap.players[i];

            if (row.txtName) row.txtName.text = p.playerName;
            if (row.txtPoints) row.txtPoints.text = p.points.ToString();
            if (row.txtKills) row.txtKills.text = p.kills.ToString();
            if (row.txtRevive) row.txtRevive.text = p.revives.ToString();
            if (row.txtDowned) row.txtDowned.text = p.downs.ToString();
        }
    }

    // --------------------------------------------------------
    // Nút LEAVE → về MainMenu + cleanup
    // --------------------------------------------------------

    void OnLeaveClicked()
    {
        LeaveToMenu();
    }

    void LeaveToMenu()
    {
#if FUSION_WEAVER
        if (runner != null)
        {
            // Không cần chờ coroutine shutdown để tránh cảm giác "đơ"
            runner.Shutdown();
        }
#endif

        SessionCleanup.CleanupAll();
        SceneManager.LoadScene("MainMenu");
    }

    void SetHudVisible(bool visible)
    {
        if (hudToHide == null) return;

        foreach (var cg in hudToHide)
        {
            if (!cg) continue;
            cg.alpha = visible ? 1f : 0f;
            cg.interactable = visible;
            cg.blocksRaycasts = visible;
        }
    }

}
