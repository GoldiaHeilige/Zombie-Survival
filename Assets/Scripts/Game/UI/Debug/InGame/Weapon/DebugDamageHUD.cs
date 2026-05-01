// DebugDamageHUD.cs — SP/MP friendly, Unity 6 + Fusion v2 compatible
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
#if FUSION_WEAVER
using Fusion;
#endif

// Nếu bạn đang dùng SingletonBehaviour của bạn, có thể đổi base class cho thống nhất
public class DebugDamageHUD : MonoBehaviour
{
    const Key ToggleKey = Key.F6;
    const int MaxLines = 120;

    bool _visible = true;
    Vector2 _scroll;
    readonly List<string> _lines = new();
    static DebugDamageHUD _inst;

    void Awake()
    {
        _inst = this;
        DamageSystem.OnReady += HandleSysReady;    // <= MỚI
        TryHook(true);
    }

    void Start()
    {
        TryHook(true); // phòng khi Awake của HUD chạy trước DamageSystem
    }

    void OnEnable() { TryHook(true); }

    void OnDestroy()
    {
        DamageSystem.OnReady -= HandleSysReady;    // <= MỚI
        TryHook(false);
        if (_inst == this) _inst = null;
    }

    void HandleSysReady(DamageSystem sys) { TryHook(true); }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb[ToggleKey].wasPressedThisFrame) _visible = !_visible;
    }

    void OnGUI()
    {
        if (!_visible) return;

        var w = Mathf.Min(780, Screen.width - 40);
        var r = new Rect(20, 20, w, Screen.height - 40);
        GUILayout.BeginArea(r, GUI.skin.box);
        {
            GUILayout.Label("<b>Damage Debug HUD</b>", Rich());
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear", GUILayout.Width(80))) _lines.Clear();
            GUILayout.FlexibleSpace();
            GUILayout.Label("F6: Toggle", Hint());
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            _scroll = GUILayout.BeginScrollView(_scroll, GUI.skin.box, GUILayout.ExpandHeight(true));
            if (_lines.Count == 0) GUILayout.Label("— no damage events —", RichSmall());
            for (int i = _lines.Count - 1; i >= 0; --i) GUILayout.Label(_lines[i], RichSmall());
            GUILayout.EndScrollView();
        }
        GUILayout.EndArea();
    }

    // ===== Hook vào DamageSystem =====
    void TryHook(bool add)
    {
        var sys = DamageSystem.Instance;
        if (sys == null) return;

        if (add)
        {
            sys.OnAfterApplied -= OnAfterApplied; // tránh double-subscribe
            sys.OnAfterApplied += OnAfterApplied;
        }
        else
        {
            sys.OnAfterApplied -= OnAfterApplied;
        }
    }

    // Chỉ log “ai → ai” và HP before/after/finalDamage (không show pipeline/procs)
    void OnAfterApplied(DamageEvent e, DamageResult r)
    {
        if (!r.isApplied) return;
        float hpAfter = r.remainingHealth;
        float hpBefore = hpAfter + r.finalDamage;
        PushLine(e.attacker, e.victimGO, hpBefore, hpAfter, r.finalDamage, Time.time);
    }

    // ===== Helpers =====
    string BuildEntityLabel(GameObject go)
    {
        if (!go) return "ENV";
        string name = go.name;

        // team
        string teamStr = "-";
        try
        {
            var dmg = go.GetComponentInParent<IDamageable>();
            teamStr = dmg != null ? dmg.GetTeam().ToString() : "-";
        }
        catch { }

        // net info (nếu có Fusion)
#if FUSION_WEAVER
        string net = "-";
        var no = go.GetComponentInParent<NetworkObject>();
        if (no) net = $"NO:{no.Id.Raw} SA:{(no.HasStateAuthority ? 'Y' : 'N')} IA:{(no.HasInputAuthority ? 'Y' : 'N')}";
        return $"{name} ({teamStr}; {net})";
#else
    return $"{name} ({teamStr})";
#endif
    }

    void Push(string line)
    {
        _lines.Add(line);
        if (_lines.Count > MaxLines)
            _lines.RemoveRange(0, _lines.Count - MaxLines);
    }

    void PushLine(GameObject attacker, GameObject victim, float hpBefore, float hpAfter, float amount, float t)
    {
        string atk = BuildEntityLabel(attacker);
        string vic = BuildEntityLabel(victim);
        string line =
          $"{t:0.000}s  <b>{atk}</b>  →  <b>{vic}</b>  HP: {hpBefore:0.##} → <b>{hpAfter:0.##}</b>  (dmg {amount:0.##}{(hpAfter <= 0 ? ", FATAL" : "")})";
        Push(line);
    }

    // [ADD] static API để MPDamageDriver gọi khi nhận RPC_Notice
#if FUSION_WEAVER
    public static void PushMirror(NetworkRunner runner, NetworkId attackerId, NetworkId victimId,
                                  int hpBefore, int hpAfter, int amount, float t)
    {
        if (_inst == null) return;
        GameObject atk = null, vic = null;
        if (runner && attackerId.IsValid) atk = runner.FindObject(attackerId)?.gameObject;
        if (runner && victimId.IsValid) vic = runner.FindObject(victimId)?.gameObject;
        _inst.PushLine(atk, vic, hpBefore, hpAfter, amount, t);
    }
#endif


    static GUIStyle Rich() { var s = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14 }; return s; }
    static GUIStyle RichSmall() { var s = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12 }; return s; }
    static GUIStyle Hint() { var s = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = new Color(0.6f, 0.6f, 0.6f) } }; return s; }
}
