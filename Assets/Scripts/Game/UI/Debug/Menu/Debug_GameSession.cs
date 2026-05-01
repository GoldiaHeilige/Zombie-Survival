using UnityEngine;
using System.Linq;
#if FUSION_WEAVER
using Fusion;
#endif

public class DebugGameSession: MonoBehaviour
{
    private static DebugGameSession _inst;

 //   [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreate()
    {
        if (_inst != null) return;
        var go = new GameObject("DebugGameSession");
        _inst = go.AddComponent<DebugGameSession>();
        DontDestroyOnLoad(go);
    }

    public KeyCode toggleKey = KeyCode.F1;
    private bool _show = true;
    private GUIStyle _style;

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) _show = !_show;
    }

    void OnGUI()
    {
        if (!_show) return;

        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 13,
                alignment = TextAnchor.UpperLeft
            };
            _style.normal.textColor = Color.white;
        }

        string mode = GameSession.Mode.ToString();
        string map = GameSession.SelectedMapSceneName ?? "";
        string extra = "";

#if FUSION_WEAVER
        var runner = Object.FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
        if (runner != null)
        {
            int playerCount = 0;
            try
            {
                // ActivePlayers là IEnumerable<PlayerRef>
                if (runner.ActivePlayers != null) playerCount = runner.ActivePlayers.Count();
            }
            catch { /* ignore */ }

            // SessionInfo có thể null hoặc Name rỗng
            var sessionName = (runner.SessionInfo != null && !string.IsNullOrEmpty(runner.SessionInfo.Name))
                ? runner.SessionInfo.Name
                : "<none>";

            // LocalPlayer là PlayerRef, hiển thị ToString() cho an toàn giữa các version
            var localRef = runner.LocalPlayer.ToString();

            extra =
                "\nRunner: IsRunning=" + runner.IsRunning + " Mode=" + runner.GameMode +
                "\nPlayers=" + playerCount + " Local=" + localRef +
                "\nSession: " + sessionName;
        }
        else
        {
            extra = "\nRunner: <none>";
        }
#endif

        string txt = $"[DEBUG OVERLAY]\nAppPlayMode: {mode}\nMap: {map}{extra}\n(F1 to toggle)";
        GUI.Box(new Rect(10, 10, 460, 90), txt, _style);
    }
}
