using UnityEngine;
using System.Linq;
using Fusion; // <-- thêm

public class SetupCountdownHUD : MonoBehaviour
{
    public RoundDirector director;
    Rect _r = new Rect(10, 10, 520, 260);

    void OnGUI()
    {
        if (director == null) director = RoundDirector.Instance;
        if (director == null) return;

        GUILayout.BeginArea(_r, GUI.skin.box);
        if (!director.hasGameStarted)
        {
            GUILayout.Label("<b>SETUP PHASE</b>");
            float t = Mathf.Max(0f, director.setupTimeRemaining);
            if (director.isInSetup)
            {
                GUILayout.Label($"Auto-start in: {FormatTime(t)}");

                // --- ONLY HOST CAN START ---
                if (IsHostLike())
                {
                    GUILayout.Label("Press [E] at Start Console to begin early (cooldown).");
                }
                else
                {
                    GUILayout.Label("<color=#AAAAAA>Only host can start the game.</color>");
                }
            }
            else
            {
                GUILayout.Label("Starting…");
            }
        }
        else
        {
            var wave = director.currentWave;
            int budgetMax = wave ? wave.budgetPoints : 0;
            GUILayout.Label($"<b>Round</b> {director.roundIndex} | <b>Alive</b>: {director.alive} | <b>Spent</b>: {director.spentBudget}/{budgetMax}");

            // Player-count scaling debug
            GUILayout.Label($"<b>Players</b>: {director.scaledPlayerCount}  |  budget x{director.appliedBudgetMultiplier:0.00}  |  cap +{director.appliedCapAdd}  |  maxCap +{director.appliedMaxCapAdd}  (max={director.appliedMaxConcurrency})");

            GUILayout.Space(6);
            GUILayout.Label("<b>Source</b>");
            string src = director.currentSource.ToString(); // None/Fixed/Procedural/Special
            if (director.currentSource == RoundDirector.WaveSource.Procedural)
            {
                float jf = director.lastBudgetJitterFactor;
                int jd = director.lastCapJitterDelta;

                string jitterTxt = "";
                bool anyJitter = Mathf.Abs(jf - 1f) > 0.001f || jd != 0;
                if (anyJitter)
                {
                    string jfStr = Mathf.Abs(jf - 1f) > 0.001f ? $"budget x{jf:0.00}" : "";
                    string jdStr = jd != 0 ? $"cap {(jd >= 0 ? "+" : "")}{jd}" : "";
                    jitterTxt = $"  <color=#AAAAAA>(jitter {jfStr}{(jfStr != "" && jdStr != "" ? ", " : "")}{jdStr})</color>";
                }
                GUILayout.Label($"Source: {src}{jitterTxt}");
            }
            else
            {
                GUILayout.Label($"Source: {src}");
            }

            GUILayout.Space(6);
            GUILayout.Label("<b>Wave Profile</b>");
            if (wave)
            {
                GUILayout.Label($"Budget: {wave.budgetPoints}   Cap: {wave.concurrencyCap}");
                GUILayout.Label($"Burst: {wave.spawnBurst}   InterBurst: {wave.interBurstDelay:0.0}s");

                if (wave.allowTypes != null && wave.allowTypes.Count > 0)
                {
                    string types = string.Join(", ",
                        wave.allowTypes.Select(d =>
                        {
                            if (d == null) return "null";
                            return string.IsNullOrEmpty(d.id) ? d.name : d.id;
                        }));
                    GUILayout.Label($"Types: {types}");
                }
                else
                {
                    GUILayout.Label("Types: (none)");
                }
            }
            else
            {
                GUILayout.Label("Waiting next wave…");
            }
        }
        GUILayout.EndArea();
    }

    // Host/Singleplayer check:
    // - Nếu không có NetworkRunner => coi như single (được start).
    // - Nếu có: Host = IsServer || IsSharedModeMasterClient.
    bool IsHostLike()
    {
        var runner = FindFirstObjectByType<NetworkRunner>();
        if (runner == null) return true; // SP
        return runner.IsServer || runner.IsSharedModeMasterClient;
    }

    string FormatTime(float s)
    {
        int m = Mathf.FloorToInt(s / 60f);
        int sec = Mathf.CeilToInt(s % 60f);
        return $"{m:00}:{sec:00}";
    }
}
