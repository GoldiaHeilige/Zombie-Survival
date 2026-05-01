using UnityEngine;

/// <summary>
/// Global Simulation Clock – unify SP & MP simulation delta.
/// SP: runs a local tick-loop at 64 Hz.
/// MP: uses Fusion Runner's tick (do NOT tick here).
/// </summary>
public class SimTime : MonoBehaviour
{
    public const float SIM_TICK_RATE = 64f;
    public const float SIM_DT = 1f / SIM_TICK_RATE;

    // Tick counters (SP only)
    public static int Tick { get; private set; }
    public static float Delta { get; private set; } = SIM_DT;

    private float _accumulator;

    // Singleton
    public static SimTime Instance { get; private set; }

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        // Nếu đang ở Multiplayer → Fusion sẽ tick → KHÔNG chạy SP tick
        if (GameSession.Mode != AppPlayMode.Single)
            return;

        // SP tick-loop
        _accumulator += Time.deltaTime;

        while (_accumulator >= SIM_DT)
        {
            Delta = SIM_DT;
            Tick++;
            SimulateTick();
            _accumulator -= SIM_DT;
        }
    }

    /// <summary>
    /// Gọi logic Tick-based cho Singleplayer.
    /// (movement, ai, director... về sau)
    /// </summary>
    void SimulateTick()
    {
        // Movement SP (ta sẽ thêm hook ở đây)
        if (onTick != null)
            onTick.Invoke();
    }

    public static event System.Action onTick;
}
