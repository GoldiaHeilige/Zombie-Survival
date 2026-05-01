// Assets/Scripts/Lobby/LobbyState.cs
using System.Collections;
using Fusion;
using UnityEngine;

public class LobbyState : NetworkBehaviour
{
    [Networked] public bool CountdownActive { get; private set; }
    [Networked] public int SecondsLeft { get; private set; }
    [Networked] public NetworkString<_32> MapName { get; private set; }

    private Coroutine _countdownCoro;

    private bool _spawned;

    public override void Spawned()
    {
        _spawned = true;
        Debug.Log($"[LobbyState] Spawned. ObjValid={Object && Object.IsValid}, HasStateAuth={Object && Object.HasStateAuthority}");

        // ✅ NEW: Host publish map ngay từ đầu để client join thấy UI map
        if (Object != null && Object.HasStateAuthority)
        {
            if (string.IsNullOrWhiteSpace(MapName.ToString()))
            {
                var map = string.IsNullOrWhiteSpace(LobbyParams.SelectedMapSceneName)
                    ? "Gameplay_Map_01"
                    : LobbyParams.SelectedMapSceneName;

                MapName = map;
                Debug.Log($"[LobbyState] Initial MapName set -> {map}");
            }
        }
    }


    // --- ADD: safe checks to avoid reading networked props before spawn ---
    public bool IsReady => _spawned && Runner != null && Object != null && Object.IsValid;
    public bool GetCountdownActiveSafe() => IsReady ? CountdownActive : false;
    public int GetSecondsLeftSafe() => IsReady ? SecondsLeft : 0;
    public string GetMapNameSafe() => IsReady ? MapName.ToString() : "";
    // ----------------------------------------------------------------------

    public void HostStartCountdown(int seconds, string mapName)
    {
        if (!Object || !Object.HasStateAuthority)
        {
            Debug.LogWarning($"[LobbyState] HostStartCountdown ignored. HasStateAuthority={Object && Object.HasStateAuthority}");
            return;
        }

        if (CountdownActive) return;

        SecondsLeft = Mathf.Max(1, seconds);
        MapName = mapName;
        CountdownActive = true;

        Debug.Log($"[LobbyState] Countdown START {SecondsLeft}s → map={mapName}");

        if (_countdownCoro != null) StopCoroutine(_countdownCoro);
        _countdownCoro = StartCoroutine(Co_Countdown());
    }


    private IEnumerator Co_Countdown()
    {
        while (SecondsLeft > 0 && CountdownActive)
        {
            yield return new WaitForSeconds(1f);
            SecondsLeft--;
        }

        if (!CountdownActive) yield break;

        var runner = Runner;
        if (runner && Object.HasStateAuthority)
        {
            string map = string.IsNullOrWhiteSpace(GetMapNameSafe()) ? "Gameplay_Map_01" : GetMapNameSafe();
            _countdownCoro = null;
            CountdownActive = false;
            RPC_FadeOutAll();
            yield return new WaitForSecondsRealtime(0.40f); // >= fadeOutDuration của bạn (0.25f) + đệm

            _countdownCoro = null;
            CountdownActive = false;

            _ = runner.LoadScene(map);

        }
    }

    public void HostCancelCountdown()
    {
        if (!Object || !Object.HasStateAuthority)
        {   // <- sửa đúng HasStateAuthority
            Debug.LogWarning($"[LobbyState] HostCancelCountdown IGNORED. HasStateAuth={Object && Object.HasStateAuthority}");
            return;
        }
        if (!CountdownActive) return;

        CountdownActive = false;
        SecondsLeft = 0;
        if (_countdownCoro != null) StopCoroutine(_countdownCoro);
        _countdownCoro = null;
        Debug.Log("[LobbyState] Countdown CANCELLED");
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestStart(int seconds, string mapName) => HostStartCountdown(seconds, mapName);

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestCancel() => HostCancelCountdown();

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_FadeOutAll()
    {
        TT.UI.SceneTransitionFader.Instance?.BeginNetworkFadeOut();
    }

}
