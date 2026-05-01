using UnityEngine;
#if FUSION_WEAVER
using Fusion;
#endif
using System.Collections;

/// <summary>
/// Bật đúng spawner theo GameSession.Mode và đảm bảo không spawn trùng.
/// </summary>
public class GameSceneSelector : MonoBehaviour
{
    [Header("References in Scene")]
    [SerializeField] MonoBehaviour singleplayerSpawner;   // = SingleplayerSpawner
    [SerializeField] MonoBehaviour fusionPlayerSpawner;   // = FusionPlayerSpawner

    private void Awake()
    {
        // Tắt cả hai lúc mới load, để tránh bật nhầm
        Toggle(singleplayerSpawner, false);
        Toggle(fusionPlayerSpawner, false);

        // Bắt đầu routine chờ runner sẵn sàng
        StartCoroutine(Co_CheckAndEnableSpawner());
    }

    private IEnumerator Co_CheckAndEnableSpawner()
    {
#if FUSION_WEAVER
        NetworkRunner runner = null;

        // --- Giai đoạn 1: chờ runner spawn ---
        float t = 0f;
        while (runner == null && t < 20f)
        {
            runner = FindFirstObjectByType<NetworkRunner>();
            if (GameSession.Mode == AppPlayMode.Single) break; // ✅ Nếu là single, dừng chờ ngay
            yield return new WaitForSeconds(0.25f);
            t += 0.25f;
        }


        // --- Giai đoạn 2: chờ runner Running và có LocalPlayer ---
        while (runner != null && (!runner.IsRunning || runner.LocalPlayer == PlayerRef.None))
        {
            yield return null;
        }

        // --- Cập nhật GameSession.Mode ---
        if (runner != null)
        {
            if (runner.GameMode == GameMode.Host || runner.GameMode == GameMode.Server)
                GameSession.Mode = AppPlayMode.Host;
            else if (runner.GameMode == GameMode.Client)
                GameSession.Mode = AppPlayMode.Client;
            else
                GameSession.Mode = AppPlayMode.Single;
        }
#endif

        // --- Bật spawner tương ứng ---
        bool wantSingle = GameSession.Mode == AppPlayMode.Single;
#if FUSION_WEAVER
        bool runnerRunning = runner != null && runner.IsRunning;
#else
        bool runnerRunning = false;
#endif

        if (wantSingle || !runnerRunning)
        {
            Toggle(singleplayerSpawner, true);
            Toggle(fusionPlayerSpawner, false);
        }
        else
        {
            Toggle(singleplayerSpawner, false);
            Toggle(fusionPlayerSpawner, true);
        }

        Debug.Log($"[GameSceneSelector] Finalized → Mode={GameSession.Mode}, RunnerRunning={runnerRunning}");
    }

    private void Toggle(MonoBehaviour mb, bool on)
    {
        if (!mb) return;

        if (on)
            mb.gameObject.SetActive(true);           // ✅ đảm bảo GO đang active khi bật
        mb.enabled = on;
        // KHÔNG SetActive(false) khi off để tránh mất OnEnable lần sau
    }


    /*    static void Toggle(MonoBehaviour mb, bool on)
        {
            if (!mb) return;
            mb.enabled = on;
            if (mb.gameObject) mb.gameObject.SetActive(on);
        }*/
}
