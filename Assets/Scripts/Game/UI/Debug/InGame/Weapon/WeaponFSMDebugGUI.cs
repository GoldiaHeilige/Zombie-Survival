using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class WeaponFSMDebugGUI : MonoBehaviour
{
    const Key ToggleKey = Key.F6;

    bool _visible = false;
    Vector2 _scroll;

    readonly List<FSMTracked> _tracked = new();

    class FSMTracked
    {
        public WeaponController weapon;
        public WeaponFSM fsm;
        public string label;
        public List<string> stateHistory = new();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb[ToggleKey].wasPressedThisFrame)
        {
            _visible = !_visible;
            if (_visible) RefreshWeapons();
        }

        // Cập nhật real-time mỗi frame khi visible
        if (_visible)
        {
            UpdateFSMStates();
        }
    }

    void RefreshWeapons()
    {
        _tracked.Clear();

        var weapons = FindObjectsByType<WeaponController>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (var weapon in weapons)
        {
            if (weapon == null || weapon.fsm == null) continue;

            var tracked = new FSMTracked
            {
                weapon = weapon,
                fsm = weapon.fsm,
                label = BuildLabel(weapon.gameObject)
            };

            // Subscribe to state changes
            weapon.fsm.OnStateChanged += (prev, next) => OnStateChanged(tracked, prev, next);

            _tracked.Add(tracked);
        }
    }

    void UpdateFSMStates()
    {
        // Thỉnh thoảng quét lại để bắt weapon mới
        if (Time.frameCount % 10000 == 0)
        {
            RefreshWeapons();
        }
    }

    void OnStateChanged(FSMTracked tracked, WeaponState prev, WeaponState next)
    {
        string entry = $"{Time.time:0.000}s: {prev} -> {next} (ADS: {tracked.fsm.IsADS})";
        tracked.stateHistory.Add(entry);

        // Giữ tối đa 50 dòng lịch sử
        if (tracked.stateHistory.Count > 50)
            tracked.stateHistory.RemoveAt(0);
    }

    void OnGUI()
    {
        if (!_visible || _tracked.Count == 0) return;

        var w = Mathf.Min(800, Screen.width - 40);
        var h = Mathf.Min(600, Screen.height - 40);
        var r = new Rect(20, 20, w, h);

        GUILayout.BeginArea(r, GUI.skin.box);
        {
            GUILayout.Label("<b>Weapon FSM Debug</b>", RichStyle(14));
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Weapons found: {_tracked.Count}", RichStyle(12));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("↻ Refresh", GUILayout.Width(80))) RefreshWeapons();
            if (GUILayout.Button("Close", GUILayout.Width(80))) _visible = false;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            _scroll = GUILayout.BeginScrollView(_scroll);
            {
                foreach (var tracked in _tracked)
                {
                    DrawFSMInfo(tracked);
                    GUILayout.Space(10);
                }
            }
            GUILayout.EndScrollView();

            GUILayout.Space(4);
            GUILayout.Label("F6: Toggle  •  Auto-refresh every 60 frames", HintStyle());
        }
        GUILayout.EndArea();
    }

    void DrawFSMInfo(FSMTracked tracked)
    {
        if (tracked.fsm == null) return;

        GUILayout.BeginVertical(GUI.skin.box);
        {
            // Header với thông tin cơ bản
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{tracked.label}</b>", RichStyle(14));
            GUILayout.FlexibleSpace();

            // State hiện tại với màu sắc
            string stateColor = GetStateColor(tracked.fsm.State);
            GUILayout.Label($"<b>State: <color={stateColor}>{tracked.fsm.State}</color></b>", RichStyle(14));
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Thông tin chi tiết
            GUILayout.BeginHorizontal();
            GUILayout.Label($"IsADS: <b>{tracked.fsm.IsADS}</b>", RichStyle(12), GUILayout.Width(120));
            GUILayout.Label($"Weapon: <b>{(tracked.weapon.def != null ? tracked.weapon.def.weaponName : "None")}</b>", RichStyle(12));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Lịch sử state changes
            GUILayout.Label("<b>State History:</b>", RichStyle(12));

            GUILayout.BeginVertical(GUI.skin.textField, GUILayout.Height(120));
            {
                if (tracked.stateHistory.Count == 0)
                {
                    GUILayout.Label("No state changes yet", RichStyle(11));
                }
                else
                {
                    for (int i = tracked.stateHistory.Count - 1; i >= 0; i--)
                    {
                        GUILayout.Label(tracked.stateHistory[i], RichStyle(11));
                    }
                }
            }
            GUILayout.EndVertical();

            // Real-time monitoring
            GUILayout.Space(4);
            GUILayout.Label($"<size=10>Last update: {Time.time:0.000}s</size>", RichStyle(10));
        }
        GUILayout.EndVertical();
    }

    string GetStateColor(WeaponState state)
    {
        return state switch
        {
            WeaponState.None => "white",
            WeaponState.Idle => "green",
            WeaponState.Aiming => "blue",
            WeaponState.Firing => "red",
            WeaponState.Reloading => "orange",
            WeaponState.Equipping => "yellow",
            _ => "white"
        };
    }

    string BuildLabel(GameObject go)
    {
        // Thêm Network ID nếu có
#if FUSION_WEAVER
        var netObj = go.GetComponent<Fusion.NetworkObject>();
        if (netObj != null) return $"{go.name} (ID:{netObj.Id.Raw})";
#endif

        return go.name;
    }

    GUIStyle RichStyle(int fontSize = 12)
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            richText = true,
            fontSize = fontSize,
            wordWrap = true
        };
        return style;
    }

    GUIStyle HintStyle()
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }
        };
        return style;
    }

    void OnDestroy()
    {
        // Cleanup subscriptions
        foreach (var tracked in _tracked)
        {
            if (tracked.fsm != null)
            {
                // Không thể unsubscribe cụ thể, nhưng FSM sẽ bị destroy cùng WeaponController
            }
        }
    }
}