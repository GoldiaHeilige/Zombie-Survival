using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

#if FUSION_WEAVER
using Fusion;
#endif

public class DebugLoadoutHUD : MonoBehaviour
{
    const Key ToggleKey = Key.F2;
    const Key NextKey = Key.F3;

    bool _visible = true;
    int _current = 0;
    Vector2 _scroll;

    readonly List<Tracked> _tracked = new();

    readonly Dictionary<ILoadoutState, List<string>> _logs = new();
    const int MaxLines = 80;

    class Tracked
    {
        public PlayerStateProvider provider;
        public ILoadoutState loadout;
        public PlayerWeaponBridge bridge;
#if FUSION_WEAVER
        public NetworkObject netObj;
#endif
        public string label;
    }

    void Awake()
    {
        RefreshPlayers();
    }

    void OnEnable()
    {
        SubscribeAll(true);

        try
        {
            TT.Observer.Instance?.AddObserver("weapon.fired", OnWpnFired);
            TT.Observer.Instance?.AddObserver("weapon.reload.started", OnReloadStarted);
            TT.Observer.Instance?.AddObserver("weapon.reload.finished", OnReloadFinished);
        }
        catch { }

    }

    void OnDisable()
    {
        SubscribeAll(false);
        _tracked.Clear();

        try
        {
            TT.Observer.Instance?.RemoveObserver("weapon.fired", OnWpnFired);
            TT.Observer.Instance?.RemoveObserver("weapon.reload.started", OnReloadStarted);
            TT.Observer.Instance?.RemoveObserver("weapon.reload.finished", OnReloadFinished);
        }
        catch { }
        _logs.Clear();

    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb[ToggleKey].wasPressedThisFrame)
        {
            _visible = !_visible;
            if (_visible) RefreshPlayers();
        }
        if (!_visible) return;

        if (kb[NextKey].wasPressedThisFrame && _tracked.Count > 0)
            _current = (_current + 1) % _tracked.Count;

        // thỉnh thoảng quét lại để bắt player spawn/despawn
        if (Time.frameCount % 60 == 0)
            RefreshPlayers();
    }

    void OnGUI()
    {
        if (!_visible || _tracked.Count == 0) return;

        var w = Mathf.Min(760, Screen.width - 40);
        var r = new Rect(20, 20, w, Screen.height - 40);
        GUILayout.BeginArea(r, GUI.skin.box);
        {
            GUILayout.Label("<b>Loadout Debug GUI</b>", Rich());
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Players found: {_tracked.Count}", RichSmall());
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("↻ Refresh", GUILayout.Width(90))) RefreshPlayers();
            if (GUILayout.Button("◀ Prev", GUILayout.Width(70))) _current = (_current - 1 + Math.Max(1, _tracked.Count)) % Math.Max(1, _tracked.Count);
            if (GUILayout.Button("Next ▶", GUILayout.Width(70))) _current = (_current + 1) % Math.Max(1, _tracked.Count);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            DrawOverviewTable();

            GUILayout.Space(10);
            DrawSelectedDetails();
        }
        GUILayout.EndArea();
    }

    // ================= Detail panes =================

    void DrawOverviewTable()
    {
        GUILayout.BeginHorizontal(GUI.skin.box);
        Cell("<b>#</b>", 28);
        Cell("<b>Name</b>", 180);
        Cell("<b>Active</b>", 56);
        Cell("<b>Weapon</b>", 200);
        Cell("<b>Mag</b>", 60);
        Cell("<b>Reserve</b>", 70);
#if FUSION_WEAVER
        Cell("<b>Auth</b>", 140);
#endif
        GUILayout.EndHorizontal();

        for (int i = 0; i < _tracked.Count; i++)
        {
            var t = _tracked[i];
            var style = (i == _current) ? GUI.skin.button : GUI.skin.box;

            int active = SafeActive(t.loadout);
            var s = SafeSlot(t.loadout, active);
            var def = (s.weaponKey != 0) ? WeaponIdRegistry.GetDef(s.weaponKey) : null;

            GUILayout.BeginHorizontal(style);
            Cell(i.ToString(), 28);
            Cell(t.label, 180);
            Cell(active >= 0 ? active.ToString() : "-", 56);
            Cell(def != null ? def.name : "(empty)", 200);
            Cell(s.mag.ToString(), 60);
            Cell(s.reserve.ToString(), 70);
#if FUSION_WEAVER
            var auth = t.netObj
                ? $"State:{(t.netObj.HasStateAuthority ? "Y" : "N")}  Input:{(t.netObj.HasInputAuthority ? "Y" : "N")}"
                : "-";
            Cell(auth, 140);
#endif
            GUILayout.EndHorizontal();
        }
    }

    void DrawSelectedDetails()
    {
        if (_tracked.Count == 0) return;

        var t = _tracked[Mathf.Clamp(_current, 0, _tracked.Count - 1)];
        GUILayout.Label($"<b>Selected:</b> {t.label}", Rich());

        int slotCount = t.loadout?.SlotCount ?? 0;
        int active = SafeActive(t.loadout);

        _scroll = GUILayout.BeginScrollView(_scroll, GUI.skin.box, GUILayout.ExpandHeight(true));
        for (int i = 0; i < slotCount; i++)
        {
            var s = SafeSlot(t.loadout, i);
            var def = (s.weaponKey != 0) ? WeaponIdRegistry.GetDef(s.weaponKey) : null;

            bool isActive = (i == active);
            GUILayout.BeginVertical(isActive ? GUI.skin.button : GUI.skin.box);

            GUILayout.Label(isActive ? $"<b>Slot {i} (ACTIVE)</b>" : $"Slot {i}", RichSmall());
            GUILayout.Label($"Key: {s.weaponKey}  •  Weapon: {(def != null ? def.name : "(empty)")}", RichSmall());
            GUILayout.Label($"STATE  → mag:{s.mag}  reserve:{s.reserve}", RichSmall());

            // Runtime/GUID nếu có bridge
            if (t.bridge != null)
            {
                string guid = t.bridge.GetRuntimeGuid(i);
                var rt = t.bridge.GetRuntime(i);
                GUILayout.Label($"RUNTIME→ mag:{(rt != null ? rt.mag : -1)}  reserve:{(rt != null ? rt.reserve : -1)}", RichSmall());
                GUILayout.Label($"GUID(runtime): {(!string.IsNullOrEmpty(guid) ? guid : "(none)")}", RichSmall());
            }

            GUILayout.EndVertical();
        }
        GUILayout.EndScrollView();

        GUILayout.Space(8);
        GUILayout.Label("<b>Event Log</b>", Rich());

        var tSel = _tracked[Mathf.Clamp(_current, 0, _tracked.Count - 1)];
        Vector2 logScroll = Vector2.zero; // nếu bạn đã có _scroll riêng cho log thì dùng lại

        logScroll = GUILayout.BeginScrollView(logScroll, GUI.skin.box, GUILayout.Height(200));
        if (_logs.TryGetValue(tSel.loadout, out var lines))
        {
            for (int i = lines.Count - 1; i >= 0; --i)
            {
                GUILayout.Label(lines[i], RichSmall());
            }
        }
        else
        {
            GUILayout.Label("— no events —", RichSmall());
        }
        GUILayout.EndScrollView();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Clear Log", GUILayout.Width(90))) _logs[tSel.loadout] = new List<string>();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();


        GUILayout.BeginHorizontal();
        GUILayout.Label("F4: Toggle  •  F5: Next Player", Hint());
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    // ================= Wire-up & helpers =================

    void RefreshPlayers()
    {
        SubscribeAll(false);
        _tracked.Clear();

        var providers = UnityEngine.Object.FindObjectsByType<PlayerStateProvider>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (var p in providers)
        {
            if (p == null || p.gameObject == null) continue;

            var load = p.Loadout;
            if (load == null) continue;

            var tr = new Tracked
            {
                provider = p,
                loadout = load,
                bridge = p.GetComponentInChildren<PlayerWeaponBridge>(true),
#if FUSION_WEAVER
                netObj = p.GetComponent<NetworkObject>(),
#endif
                label = BuildLabel(p.gameObject)
            };

            _tracked.Add(tr);
        }

        SubscribeAll(true);
        _current = Mathf.Clamp(_current, 0, Math.Max(0, _tracked.Count - 1));
    }

    void SubscribeAll(bool add)
    {
        foreach (var t in _tracked)
        {
            if (t.loadout == null) continue;

            if (add)
            {
                t.loadout.OnSlotChanged -= OnAnySlotChanged;
                t.loadout.OnActiveSlotChanged -= OnAnySlotChanged;
                t.loadout.OnSlotChanged += OnAnySlotChanged;
                t.loadout.OnActiveSlotChanged += OnAnySlotChanged;
            }
            else
            {
                t.loadout.OnSlotChanged -= OnAnySlotChanged;
                t.loadout.OnActiveSlotChanged -= OnAnySlotChanged;
            }
        }
    }

    void PushLog(ILoadoutState key, string line)
    {
        if (!_logs.TryGetValue(key, out var list))
        {
            list = new List<string>();
            _logs[key] = list;
        }
        list.Add(line);
        if (list.Count > MaxLines) list.RemoveRange(0, list.Count - MaxLines);
    }

    void OnWpnFired(object payload)
    {
        // payload: (GameObject who, int slot, string weaponId)
        try
        {
            var (who, slot, wid) = ((GameObject, int, string))payload;
            var p = who ? who.GetComponent<PlayerStateProvider>() : null;
            var l = p?.Loadout; if (l == null) return;
            string line = $"{Time.time:0.000}s  FIRE   • slot {slot} • {wid}";
            PushLog(l, line);
        }
        catch { }
    }

    void OnReloadStarted(object payload)
    {
        try
        {
            var (who, slot, wid) = ((GameObject, int, string))payload;
            var p = who ? who.GetComponent<PlayerStateProvider>() : null;
            var l = p?.Loadout; if (l == null) return;
            string line = $"{Time.time:0.000}s  RELOAD START • slot {slot} • {wid}";
            PushLog(l, line);
        }
        catch { }
    }

    void OnReloadFinished(object payload)
    {
        try
        {
            var (who, slot) = ((GameObject, int))payload;
            var p = who ? who.GetComponent<PlayerStateProvider>() : null;
            var l = p?.Loadout; if (l == null) return;
            string line = $"{Time.time:0.000}s  RELOAD DONE  • slot {slot}";
            PushLog(l, line);
        }
        catch { }
    }


    void OnAnySlotChanged(int _)
    {
        // chỉ cần repaint – dữ liệu đọc trực tiếp từ state nên luôn đúng
    }

    static int SafeActive(ILoadoutState l)
    {
        try { return l != null ? l.ActiveSlot : -1; } catch { return -1; }
    }

    static WeaponSlotState SafeSlot(ILoadoutState l, int i)
    {
        if (l == null || i < 0 || i >= l.SlotCount) return default;
        try { return l.GetSlot(i); } catch { return default; }
    }

    static string BuildLabel(GameObject go)
    {
#if FUSION_WEAVER
        var no = go.GetComponent<NetworkObject>();
        if (no) return $"{go.name} (NO:{no.Id.Raw})";
#endif
        return go.name;
    }

    static GUIStyle Rich()
    {
        var s = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14 };
        return s;
    }
    static GUIStyle RichSmall()
    {
        var s = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12 };
        return s;
    }
    static GUIStyle Hint()
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = new Color(0.6f, 0.6f, 0.6f) } };
        return s;
    }

    static void Cell(string text, float width)
    {
        GUILayout.Label(text, RichSmall(), GUILayout.Width(width));
    }
}
