using System;
using System.Collections.Generic;
using UnityEngine;

#if FUSION_WEAVER
using Fusion;
#endif

/// <summary>
/// Registry tĩnh lưu toàn bộ PlayerRefs trong scene / room.
/// Dùng cho spectator / UI / targetting.
/// </summary>
public static class PlayerRegistry
{
    static readonly List<PlayerRefs> _players = new List<PlayerRefs>();

    /// <summary>Danh sách player hiện tại (read-only).</summary>
    public static IReadOnlyList<PlayerRefs> Players => _players;

    /// <summary>Event khi 1 PlayerRefs được đăng ký.</summary>
    public static event Action<PlayerRefs> OnPlayerRegistered;

    /// <summary>Event khi 1 PlayerRefs bị gỡ.</summary>
    public static event Action<PlayerRefs> OnPlayerUnregistered;

    /// <summary>Đăng ký 1 PlayerRefs mới.</summary>
    public static void Register(PlayerRefs refs)
    {
        if (!refs) return;
        if (_players.Contains(refs)) return;

        _players.Add(refs);
        OnPlayerRegistered?.Invoke(refs);
    }

    /// <summary>Gỡ 1 PlayerRefs khỏi registry.</summary>
    public static void Unregister(PlayerRefs refs)
    {
        if (!refs) return;
        if (_players.Remove(refs))
        {
            OnPlayerUnregistered?.Invoke(refs);
        }
    }

    /// <summary>
    /// Lấy local player:
    /// - MP (Fusion): player có InputAuthority.
    /// - SP hoặc không tìm thấy: trả về player đầu tiên còn sống.
    /// </summary>
    public static PlayerRefs GetLocalPlayer()
    {
#if FUSION_WEAVER
        // Ưu tiên player có InputAuthority
        foreach (var p in _players)
        {
            if (!p) continue;
            if (p.TryGetComponent<NetworkObject>(out var no))
            {
                if (no && no.Runner != null && no.HasInputAuthority)
                    return p;
            }
        }
#endif
        // Fallback: SP hoặc không có Fusion / không tìm thấy
        foreach (var p in _players)
        {
            if (p) return p;
        }

        return null;
    }

    /// <summary>
    /// Lấy danh sách player còn tồn tại (không null).
    /// Sau này dùng cho spectator cycle.
    /// </summary>
    public static List<PlayerRefs> GetAllValidPlayers()
    {
        var result = new List<PlayerRefs>(_players.Count);
        foreach (var p in _players)
        {
            if (p) result.Add(p);
        }
        return result;
    }
}
