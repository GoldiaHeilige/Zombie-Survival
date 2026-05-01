// MovementDebugGUI.cs (patched for Unity 6 + your Observer API)
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

#if FUSION_WEAVER
using Fusion;
#endif

using TT;
using NIX.Core.DesignPatterns;

public class MovementDebugGUI : SingletonBehaviour<MovementDebugGUI>
{
    const Key ToggleKey = Key.F4;
    const Key NextKey = Key.F5;
    const int MaxLinesPerPlayer = 60;

    bool _visible;
    Vector2 _scroll;
    int _current;

    readonly List<Tracked> _tracked = new();
    readonly Dictionary<IMovementState, List<string>> _logs = new();

    class Tracked
    {
        public GameObject go;
        public IMovementState movement;
#if FUSION_WEAVER
        public NetworkObject netObj;
#endif
        public string label;
    }

    protected override void Awake() { base.Awake(); RefreshPlayers(); } // ⬅️ gọi base

    void OnEnable()
    {
        try { Observer.Instance?.AddObserver("player.movement.changed", OnObserverMovementChanged); } catch { }
    }
    void OnDisable()
    {
        try { Observer.Instance?.RemoveObserver("player.movement.changed", OnObserverMovementChanged); } catch { }
        foreach (var t in _tracked) if (t.movement != null) t.movement.OnStateChanged -= OnStateChangedDirect;
        _tracked.Clear(); _logs.Clear();
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

        if (Time.frameCount % 60 == 0)
            RefreshPlayers();
    }

    void OnGUI()
    {
        if (!_visible) return;

        var w = Mathf.Min(560, Screen.width - 40);
        var r = new Rect(20, 20, w, Screen.height - 40);
        GUILayout.BeginArea(r, GUI.skin.box);
        {
            GUILayout.Label("<b>Movement Debug GUI</b>", Rich());
            GUILayout.Space(4);

            GUILayout.Label("Players found: " + _tracked.Count, Rich());

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("↻ Refresh", GUILayout.Width(90))) RefreshPlayers();
            if (GUILayout.Button("◀ Prev", GUILayout.Width(70))) _current = (_current - 1 + Math.Max(1, _tracked.Count)) % Math.Max(1, _tracked.Count);
            if (GUILayout.Button("Next ▶", GUILayout.Width(70))) _current = (_current + 1) % Math.Max(1, _tracked.Count);
            GUILayout.FlexibleSpace();
            GUILayout.Label("F4: Toggle  •  F5: Next Player", EditorNote());
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            DrawOverviewTable();

            GUILayout.Space(10);
            GUILayout.Label("<b>Selected Player Log</b>", Rich());
            GUILayout.Space(2);

            if (_tracked.Count == 0)
            {
                GUILayout.Label("No players with IMovementState found.");
            }
            else
            {
                var t = _tracked[Mathf.Clamp(_current, 0, _tracked.Count - 1)];
                GUILayout.Label(t.label, RichSmall());

                if (!_logs.TryGetValue(t.movement, out var list))
                    list = new List<string>();

                _scroll = GUILayout.BeginScrollView(_scroll, GUI.skin.box, GUILayout.ExpandHeight(true));
                for (int i = list.Count - 1; i >= 0; --i) GUILayout.Label(list[i]);
                GUILayout.EndScrollView();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Clear Log", GUILayout.Width(90))) list.Clear();
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }
        GUILayout.EndArea();
    }

    void DrawOverviewTable()
    {
        if (_tracked.Count == 0) { GUILayout.Label("— No player —"); return; }

        GUILayout.BeginHorizontal(GUI.skin.box);
        Cell("<b>#</b>", 30);
        Cell("<b>Name</b>", 160);
        Cell("<b>State</b>", 120);
        Cell("<b>Stamina</b>", 80);
#if FUSION_WEAVER
        Cell("<b>Auth</b>", 120);
#endif
        GUILayout.EndHorizontal();

        for (int i = 0; i < _tracked.Count; i++)
        {
            var t = _tracked[i];
            var isSel = (i == _current);
            var style = isSel ? GUI.skin.button : GUI.skin.box;

            GUILayout.BeginHorizontal(style);
            Cell(i.ToString(), 30);
            Cell(t.label, 160);
            Cell(t.movement?.Current.ToString() ?? "-", 120);
            Cell(t.movement != null ? t.movement.Stamina.ToString("0.0") : "-", 80);
#if FUSION_WEAVER
            var auth = t.netObj
                ? $"State:{(t.netObj.HasStateAuthority ? "Y" : "N")}  Input:{(t.netObj.HasInputAuthority ? "Y" : "N")}"
                : "-";
            Cell(auth, 120);
#endif
            GUILayout.EndHorizontal();
        }
    }

    static void Cell(string text, float width) => GUILayout.Label(text, RichSmall(), GUILayout.Width(width));
    static GUIStyle Rich() { var s = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14 }; return s; }
    static GUIStyle RichSmall() { var s = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12 }; return s; }
    static GUIStyle EditorNote() { var s = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = new Color(0.6f, 0.6f, 0.6f) } }; return s; }

    // === Player discovery ===
    void RefreshPlayers()
    {
        foreach (var t in _tracked)
            if (t.movement != null)
                t.movement.OnStateChanged -= OnStateChangedDirect;

        _tracked.Clear();

        // ⬇️ Unity 6 API
        var providers = UnityEngine.Object.FindObjectsByType<PlayerStateProvider>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (var p in providers)
        {
            if (p == null || p.gameObject == null) continue;
            var mv = p.Movement;
            if (mv == null) continue;

            var tr = new Tracked
            {
                go = p.gameObject,
                movement = mv,
#if FUSION_WEAVER
                netObj = p.GetComponent<NetworkObject>(),
#endif
                label = BuildLabel(p.gameObject)
            };

            mv.OnStateChanged += OnStateChangedDirect;
            if (!_logs.ContainsKey(mv)) _logs[mv] = new List<string>();
            _tracked.Add(tr);
        }

        _current = Mathf.Clamp(_current, 0, Math.Max(0, _tracked.Count - 1));
    }

    static string BuildLabel(GameObject go)
    {
#if FUSION_WEAVER
        var no = go.GetComponent<NetworkObject>();
        if (no) return $"{go.name}  (NO:{no.Id.Raw})";
#endif
        return go.name;
    }

    // === Log handlers ===
    void OnObserverMovementChanged(object payload)
    {
        try
        {
            var tuple = ((object prev, object now, GameObject who))payload;
            var mv = tuple.who ? tuple.who.GetComponent<PlayerStateProvider>()?.Movement : null;
            if (mv == null) return;

            string line = $"{Time.time:0.000}s  {tuple.prev} → <b>{tuple.now}</b>";
            PushLog(mv, line);
        }
        catch { }
    }

    void OnStateChangedDirect(MovementStateId prev, MovementStateId now)
    {
        foreach (var t in _tracked)
        {
            if (t.movement == null) continue;
            if (t.movement.Current.Equals(now))
            {
                string line = $"{Time.time:0.000}s  {prev} → <b>{now}</b>";
                PushLog(t.movement, line);
            }
        }
    }

    void PushLog(IMovementState key, string line)
    {
        if (!_logs.TryGetValue(key, out var list))
        {
            list = new List<string>();
            _logs[key] = list;
        }
        list.Add(line);
        if (list.Count > MaxLinesPerPlayer)
            list.RemoveRange(0, list.Count - MaxLinesPerPlayer);
    }
}
