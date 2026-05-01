using System.Collections;
using Fusion;
using UnityEngine;

public class SpawnGateOverlay : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private CanvasGroup group;
    [SerializeField] private float fadeOutDuration = 0.15f;

    [Header("Block Input")]
    [SerializeField] private bool disableUnityPlayerInput = true;

    private NetworkRunner _runner;
    private UnityEngine.InputSystem.PlayerInput _playerInput;

    private void Awake()
    {
        if (!group) group = GetComponentInChildren<CanvasGroup>(true);
        _runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
        _playerInput = FindFirstObjectByType<UnityEngine.InputSystem.PlayerInput>(FindObjectsInactive.Include);
    }

    private void OnEnable()
    {
        // show black immediately
        ShowInstant();

        // block camera look (CinemachineInputBlocker sẽ trả 0) + block toàn bộ game input
        InputBlockerSystem.Add(InputBlocker.Full); //  (InputBlockerSystem đã có trong project)

        // Fusion: ngắt nguồn input ngay từ runner
        if (_runner != null) _runner.ProvideInput = false;

        // SP fallback: disable Unity PlayerInput (nếu mày có dùng)
        if (disableUnityPlayerInput && _playerInput != null)
            _playerInput.enabled = false;

        StartCoroutine(Co_WaitAndRelease());
    }

    private IEnumerator Co_WaitAndRelease()
    {
        // đợi tới khi local PlayerRefs xuất hiện và cameraReady == true (CameraBinder set ở cuối OnPlayerSpawned)
        // :contentReference[oaicite:2]{index=2} :contentReference[oaicite:3]{index=3}
        while (true)
        {
            var local = FindLocalPlayerRefs();
            if (local != null && local.cameraReady)
                break;

            yield return null;
        }

        // mở lại input
        if (_runner != null) _runner.ProvideInput = true;

        if (disableUnityPlayerInput && _playerInput != null)
            _playerInput.enabled = true;

        InputBlockerSystem.Remove(InputBlocker.Full);

        // hide overlay (fade nhanh)
        yield return FadeOutThenDisable();
    }

    private PlayerRefs FindLocalPlayerRefs()
    {
        // ✅ Nếu không có runner đang chạy => Singleplayer (hoặc gameplay chưa có runner)
        if (_runner == null || !_runner.IsRunning)
            return FindFirstObjectByType<PlayerRefs>(FindObjectsInactive.Exclude);

        // ✅ Multiplayer: local player là thằng có InputAuthority
        var all = FindObjectsByType<PlayerRefs>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var r in all)
        {
            var no = r.GetComponentInParent<Fusion.NetworkObject>();
            if (no != null && no.HasInputAuthority)
                return r;
        }
        return null;
    }

    private void ShowInstant()
    {
        if (!group) return;
        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;
        gameObject.SetActive(true);
    }

    private IEnumerator FadeOutThenDisable()
    {
        if (!group)
        {
            gameObject.SetActive(false);
            yield break;
        }

        float t = 0f;
        float a0 = group.alpha;
        while (t < fadeOutDuration)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(a0, 0f, t / fadeOutDuration);
            yield return null;
        }

        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        gameObject.SetActive(false);
    }
}
