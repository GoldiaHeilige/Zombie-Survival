// PointsHUDGroup.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

public class PointsHUDGroup : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Transform rowsRoot;
    [SerializeField] private PointsUI rowPrefab;

    private readonly Dictionary<PlayerRefs, PointsUI> _rows = new();
    private readonly Dictionary<PlayerRefs, Coroutine> _colorJobs = new();

    void OnEnable()
    {
        PlayerRegistry.OnPlayerRegistered += OnPlayerRegistered;
        PlayerRegistry.OnPlayerUnregistered += OnPlayerUnregistered;

        foreach (var p in PlayerRegistry.Players)
            if (p) OnPlayerRegistered(p);
    }

    void OnDisable()
    {
        PlayerRegistry.OnPlayerRegistered -= OnPlayerRegistered;
        PlayerRegistry.OnPlayerUnregistered -= OnPlayerUnregistered;
    }

    private void OnPlayerRegistered(PlayerRefs refs)
    {
        if (!refs) return;
        if (_rows.ContainsKey(refs)) return;
        if (!rowsRoot || !rowPrefab) return;

        if (GameSession.Mode == AppPlayMode.Single)
        {
            var local = PlayerRegistry.GetLocalPlayer();
            if (local != null && local != refs) return;
            if (_rows.Count > 0) return;
        }

        var row = Instantiate(rowPrefab, rowsRoot);
        row.gameObject.name = $"PointsRow_{refs.name}";
        _rows[refs] = row;

        row.Bind(refs.transform);

        // ✅ ĐỪNG set màu ngay ở đây nữa → hãy chờ NetworkObject valid rồi set.
        if (_colorJobs.TryGetValue(refs, out var oldJob) && oldJob != null)
            StopCoroutine(oldJob);

        _colorJobs[refs] = StartCoroutine(WaitThenApplyColor(refs, row));
    }

    private IEnumerator WaitThenApplyColor(PlayerRefs refs, PointsUI row)
    {
        if (!refs || !row) yield break;

        var bridge = refs.GetComponentInParent<FusionNetBridge>(true);
        if (!bridge) yield break;

        // chờ NetworkObject valid giống WorldUIBinder
        while (refs && row && bridge && (bridge.Object == null || !bridge.Object.IsValid))
            yield return null;

        if (!refs || !row || !bridge || bridge.Object == null || !bridge.Object.IsValid)
            yield break;

        int pid = bridge.Object.InputAuthority.PlayerId;

        bool isLocal = bridge.Object.HasInputAuthority;
        row.ApplyRowStyle(isLocal);

        row.SetMainPointsColor(PlayerColorPalette.GetFromFusionPlayerId(pid));
    }

    private void OnPlayerUnregistered(PlayerRefs refs)
    {
        if (!refs) return;

        if (_colorJobs.TryGetValue(refs, out var job) && job != null)
            StopCoroutine(job);
        _colorJobs.Remove(refs);

        if (_rows.TryGetValue(refs, out var row) && row)
            Destroy(row.gameObject);
        _rows.Remove(refs);
    }
}
