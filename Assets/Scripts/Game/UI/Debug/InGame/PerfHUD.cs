using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Profiling;

// Optional: only compile if Fusion is present in the project
#if FUSION_WEAVER
using Fusion;
#endif

public class PerfHUD : MonoBehaviour
{
    [Header("Display")]
    public bool show = true;
    public KeyCode toggleKey = KeyCode.F10;
    public int fontSize = 14;

    [Header("Smoothing")]
    [Range(0.01f, 1f)] public float smooth = 0.1f;
    public float sampleWindowSeconds = 2.0f; // used for min/max frame time & GC alloc/s

    [Header("Fusion (optional)")]
#if FUSION_WEAVER
    public NetworkRunner runner; // drag your runner here if you have one
#endif

    float _smoothedDt;
    float _minDt = float.PositiveInfinity;
    float _maxDt = 0f;
    float _windowTimer;

    long _lastTotalAllocated;
    long _allocInWindow;

    GUIStyle _style;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        _smoothedDt = Time.unscaledDeltaTime;
        _lastTotalAllocated = Profiler.GetTotalAllocatedMemoryLong();
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            show = !show;

        float dt = Time.unscaledDeltaTime;
        _smoothedDt = Mathf.Lerp(_smoothedDt, dt, smooth);

        // frame time min/max within window
        _minDt = Mathf.Min(_minDt, dt);
        _maxDt = Mathf.Max(_maxDt, dt);

        // GC alloc / window
        long totalAllocated = Profiler.GetTotalAllocatedMemoryLong();
        long delta = totalAllocated - _lastTotalAllocated;
        if (delta > 0) _allocInWindow += delta;
        _lastTotalAllocated = totalAllocated;

        _windowTimer += dt;
        if (_windowTimer >= sampleWindowSeconds)
        {
            _windowTimer = 0f;
            _minDt = float.PositiveInfinity;
            _maxDt = 0f;
            _allocInWindow = 0;
        }
    }

    void OnGUI()
    {
        if (!show) return;

        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                normal = { textColor = Color.white }
            };
        }

        float fps = 1f / Mathf.Max(0.0001f, _smoothedDt);
        float ms = _smoothedDt * 1000f;

        float minMs = (_minDt == float.PositiveInfinity ? 0f : _minDt * 1000f);
        float maxMs = _maxDt * 1000f;

        long totalAlloc = Profiler.GetTotalAllocatedMemoryLong();
        long monoUsed = Profiler.GetMonoUsedSizeLong();
        long monoHeap = Profiler.GetMonoHeapSizeLong();
        long reserved = Profiler.GetTotalReservedMemoryLong();

        double allocPerSec = 0.0;
        if (sampleWindowSeconds > 0.0001f)
            allocPerSec = (_allocInWindow / sampleWindowSeconds) / 1024.0 / 1024.0;

        // Simple spike indicator
        string spike = (maxMs >= 33.0f) ? "  <SPIKE?>" : "";

        // Fusion stats (best effort)
        string fusionLine = "";
#if FUSION_WEAVER
        if (runner == null)
            runner = FindFirstObjectByType<NetworkRunner>();

        if (runner != null)
        {
            // Tick is stable
            int tick = runner.Tick;

            // Ping/RTT: Fusion API differs by version, try reflection
            int rttMs = TryGetIntByReflection(runner, new[] {
                "Simulation.Statistics.RoundTripTime",   // some builds
                "Simulation.Stats.RoundTripTime",
                "Simulation.Statistics.RTT",
                "Simulation.Stats.RTT",
                "GetPlayerRtt" // placeholder if you wrap it
            }, fallback: -1);

            fusionLine = rttMs >= 0
                ? $"Fusion: tick={tick}  rtt={rttMs}ms"
                : $"Fusion: tick={tick}";
        }
#endif

        string text =
            $"FPS: {fps:0.0}   Frame: {ms:0.0}ms  (min {minMs:0.0} / max {maxMs:0.0} over {sampleWindowSeconds:0.0}s){spike}\n" +
            $"GC TotalAlloc: {ToMB(totalAlloc):0.0}MB   Reserved: {ToMB(reserved):0.0}MB\n" +
            $"Mono Used: {ToMB(monoUsed):0.0}MB / Heap: {ToMB(monoHeap):0.0}MB   GC Alloc: {allocPerSec:0.00}MB/s\n" +
            fusionLine;

        // background box
        var rect = new Rect(10, 10, 900, 80);
        GUI.Box(rect, GUIContent.none);
        GUI.Label(new Rect(20, 15, 880, 200), text, _style);
    }

    static float ToMB(long bytes) => bytes / 1024f / 1024f;

#if FUSION_WEAVER
    static int TryGetIntByReflection(object root, string[] paths, int fallback)
    {
        try
        {
            foreach (var path in paths)
            {
                object obj = root;
                string[] parts = path.Split('.');
                bool ok = true;

                foreach (var part in parts)
                {
                    if (obj == null) { ok = false; break; }

                    var t = obj.GetType();
                    // property first
                    var p = t.GetProperty(part, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null)
                    {
                        obj = p.GetValue(obj);
                        continue;
                    }

                    // field
                    var f = t.GetField(part, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null)
                    {
                        obj = f.GetValue(obj);
                        continue;
                    }

                    // method (no args)
                    var m = t.GetMethod(part, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    if (m != null)
                    {
                        obj = m.Invoke(obj, null);
                        continue;
                    }

                    ok = false;
                    break;
                }

                if (!ok || obj == null) continue;

                if (obj is int i) return i;
                if (obj is float fl) return Mathf.RoundToInt(fl);
                if (obj is double db) return (int)Math.Round(db);
            }
        }
        catch { /* ignore */ }

        return fallback;
    }
#endif
}
