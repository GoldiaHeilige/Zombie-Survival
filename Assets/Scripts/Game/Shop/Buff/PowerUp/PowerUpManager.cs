using System;
using System.Collections.Generic;
using UnityEngine;
using NIX.Core.DesignPatterns;
using TT;

#if FUSION_WEAVER
using Fusion;
#endif

/// <summary>
/// Central, authoritative power-up orchestrator.
/// Gameplay: WaW-style.
/// Audio: optional BO1-style announcer (toggle).
///
/// Design:
/// - Effects are plain C# strategy objects (no MonoBehaviour).
/// - This manager owns timed-buff state and expiry.
/// </summary>
[DisallowMultipleComponent]
public class PowerUpManager : SingletonBehaviour<PowerUpManager>
{
    [Header("Announcer (BO1-style, optional)")]
    [SerializeField] private bool announcerEnabled = true;

    [Tooltip("AudioEvent ID (2D global)")]
    [SerializeField] private int voMaxAmmoEventId;
    [SerializeField] private int voDoublePointsEventId;
    [SerializeField] private int voInstaKillEventId;
    [SerializeField] private int voNukeEventId;

    [Header("Timed Buff Durations (seconds)")]
    [SerializeField] private float doublePointsDuration = 30f;
    [SerializeField] private float instaKillDuration = 30f;

    [Header("Announcer Delay (seconds)")]
    [SerializeField] private float announcerDelayDefault = 0.25f;
    [SerializeField] private float announcerDelayNuke = 0.35f;

    // ===== Public gameplay flags (other systems can query these) =====
    public static float PointsMultiplier { get; private set; } = 1f;
    public static bool InstaKillActive { get; private set; }

    // ===== Events for integration with your existing systems =====
    public event Action<GameObject> OnMaxAmmo;          // collector
    public event Action<GameObject> OnNuke;             // collector
    public event Action<bool> OnDoublePointsChanged;    // active?
    public event Action<bool> OnInstaKillChanged;       // active?

    // ===== Internal =====
    readonly Dictionary<PowerUpType, IPowerUpEffect> _effects = new();

    // Timers (authority-owned)
    float _doublePointsUntil;
    float _instaKillUntil;

#if FUSION_WEAVER
    NetworkRunner _runner;
#endif

    protected override void Awake()
    {
        base.Awake();
        RegisterBuiltInEffects();

#if FUSION_WEAVER
        _runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
#endif
    }

    void RegisterBuiltInEffects()
    {
        _effects[PowerUpType.MaxAmmo] = new MaxAmmoEffect();
        _effects[PowerUpType.DoublePoints] = new DoublePointsEffect();
        _effects[PowerUpType.InstaKill] = new InstaKillEffect();
        _effects[PowerUpType.Nuke] = new NukeEffect();
    }

    /// <summary>
    /// Called by PowerUpPickup (or any authority system).
    /// In MP: should be called on Host/StateAuthority.
    /// In SP: called locally.
    /// </summary>
    public void Collect(PowerUpType type, GameObject collector)
    {
        if (!IsAuthority())
        {
            // Client gọi nhầm -> bỏ qua (Host mới là nguồn sự thật).
            return;
        }

        if (!_effects.TryGetValue(type, out var effect) || effect == null)
        {
            Debug.LogWarning($"[PowerUp] No effect registered for {type}", this);
            return;
        }

        // Apply gameplay effect
        effect.Apply(this, collector);

        // ✅ MP broadcast HUD timer for timed powerups
        if (type == PowerUpType.DoublePoints || type == PowerUpType.InstaKill)
        {
            float dur = GetConfiguredDuration(type);

#if FUSION_WEAVER
            if (_runner != null && _runner.IsRunning && _runner.IsServer && collector != null)
            {
                var nb = collector.GetComponentInParent<FusionNetBridge>();
                if (nb != null)
                    nb.RPC_HudPowerUpTimedStarted(type, dur);
            }
#endif
        }

        // Notify observers (UI/event feed etc.)
        try
        {
            TT.Observer.Instance?.NotifyWithData("powerup.collected", (type, collector));
        }
        catch { /* ignore */ }

        AnnounceEventFeedCollected(type, collector);
        // Announcer to ALL
        BroadcastAnnouncer(type);
    }

    // ===== Timed buff management =====

    internal void StartOrRefreshDoublePoints(float duration)
    {
        float now = Now();
        _doublePointsUntil = now + Mathf.Max(0.1f, duration);

        if (PointsMultiplier != 2f)
        {
            PointsMultiplier = 2f;
            OnDoublePointsChanged?.Invoke(true);
        }
    }

    internal void StartOrRefreshInstaKill(float duration)
    {
        float now = Now();
        _instaKillUntil = now + Mathf.Max(0.1f, duration);

        if (!InstaKillActive)
        {
            InstaKillActive = true;
            OnInstaKillChanged?.Invoke(true);
        }
    }

    void Update()
    {
        if (!IsAuthority()) return;

        float now = Now();

        if (PointsMultiplier > 1f && _doublePointsUntil > 0f && now >= _doublePointsUntil)
        {
            PointsMultiplier = 1f;
            _doublePointsUntil = 0f;
            OnDoublePointsChanged?.Invoke(false);
            try { TT.Observer.Instance?.Notify("powerup.doublepoints.ended"); } catch { }
            try { TT.Observer.Instance?.NotifyWithData("hud.powerup.timed.ended", PowerUpType.DoublePoints); } catch { }

#if FUSION_WEAVER
            if (_runner != null && _runner.IsRunning && _runner.IsServer)
            {
                var any = FindFirstObjectByType<FusionNetBridge>(FindObjectsInactive.Exclude);
                if (any != null) any.RPC_HudPowerUpTimedEnded(PowerUpType.DoublePoints);
            }
#endif

        }

        if (InstaKillActive && _instaKillUntil > 0f && now >= _instaKillUntil)
        {
            InstaKillActive = false;
            _instaKillUntil = 0f;
            OnInstaKillChanged?.Invoke(false);
            try { TT.Observer.Instance?.Notify("powerup.instakill.ended"); } catch { }
            try { TT.Observer.Instance?.NotifyWithData("hud.powerup.timed.ended", PowerUpType.InstaKill); } catch { }

#if FUSION_WEAVER
            if (_runner != null && _runner.IsRunning && _runner.IsServer)
            {
                var any = FindFirstObjectByType<FusionNetBridge>(FindObjectsInactive.Exclude);
                if (any != null) any.RPC_HudPowerUpTimedEnded(PowerUpType.InstaKill);
            }
#endif

        }
    }

    // ===== Effect helpers exposed to effects =====

    internal float GetConfiguredDuration(PowerUpType t) => t switch
    {
        PowerUpType.DoublePoints => doublePointsDuration,
        PowerUpType.InstaKill => instaKillDuration,
        _ => 0f
    };

    internal void RaiseMaxAmmo(GameObject collector) => OnMaxAmmo?.Invoke(collector);
    internal void RaiseNuke(GameObject collector) => OnNuke?.Invoke(collector);

    // ===== Authority / Time =====

    bool IsAuthority()
    {
#if FUSION_WEAVER
        if (_runner != null && _runner.IsRunning)
            return _runner.IsServer;
#endif
        return true; // SP
    }

    float Now()
    {
#if FUSION_WEAVER
        if (_runner != null && _runner.IsRunning)
            return (float)_runner.SimulationTime;
#endif
        return Time.time;
    }

    // ===== Audio =====

    void BroadcastAnnouncer(PowerUpType type)
    {
        if (!announcerEnabled) return;

        int eventId = GetAnnouncerEventId(type);
        if (eventId == 0) return;

        float delay = GetAnnouncerDelay(type);

#if FUSION_WEAVER
        // MP: chỉ host gọi. AudioEvents.PlayUiGlobal sẽ tự broadcast.
        if (_runner != null && _runner.IsRunning && !_runner.IsServer)
            return;
#endif

        if (delay <= 0.0001f)
        {
            TT.AudioEvents.PlayUiGlobal(eventId);
        }
        else
        {
            StartCoroutine(Co_PlayAnnouncerDelayed(eventId, delay));
        }
    }

    float GetAnnouncerDelay(PowerUpType type)
    {
        return type switch
        {
            PowerUpType.Nuke => announcerDelayNuke,
            _ => announcerDelayDefault
        };
    }

    System.Collections.IEnumerator Co_PlayAnnouncerDelayed(int eventId, float delay)
    {
        // unscaled cho chắc (pause menu etc.)
        float t = 0f;
        while (t < delay)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        TT.AudioEvents.PlayUiGlobal(eventId);
    }


    int GetAnnouncerEventId(PowerUpType type) => type switch
    {
        PowerUpType.MaxAmmo => voMaxAmmoEventId,
        PowerUpType.DoublePoints => voDoublePointsEventId,
        PowerUpType.InstaKill => voInstaKillEventId,
        PowerUpType.Nuke => voNukeEventId,
        _ => 0
    };

    void AnnounceEventFeedCollected(PowerUpType type, GameObject collector)
    {
        if (collector == null)
            return;

#if FUSION_WEAVER
        // MP: Host -> call RPC on collector's net bridge (broadcast to all)
        if (_runner != null && _runner.IsRunning)
        {
            if (!_runner.IsServer) return;

            var nb = collector.GetComponentInParent<FusionNetBridge>();
            if (nb != null)
                nb.RPC_AnnouncePowerUpCollected(type);

            return;
        }
#endif

        // SP fallback (no runner)
        string msg = type switch
        {
            PowerUpType.MaxAmmo => "Picked up MAX AMMO!",
            PowerUpType.DoublePoints => "Picked up DOUBLE POINTS!",
            PowerUpType.InstaKill => "Picked up INSTA-KILL!",
            PowerUpType.Nuke => "Picked up NUKE!",
            _ => "Picked up a power-up!"
        };

        var feedType = type switch
        {
            PowerUpType.Nuke => EventFeedType.Danger,
            PowerUpType.MaxAmmo => EventFeedType.Success,
            PowerUpType.DoublePoints => EventFeedType.Action,
            PowerUpType.InstaKill => EventFeedType.Action,
            _ => EventFeedType.Info
        };

        EventFeed.Push(msg, feedType);
    }


    // =====================================================================
    // Built-in Effects (Strategy Objects)
    // =====================================================================

    sealed class MaxAmmoEffect : IPowerUpEffect
    {
        public PowerUpType Type => PowerUpType.MaxAmmo;
        public bool IsTimed => false;
        public float DurationSeconds => 0f;
        public void Apply(PowerUpManager ctx, GameObject collector)
        {
            ctx.RaiseMaxAmmo(collector);
            try { TT.Observer.Instance?.Notify("powerup.maxammo"); } catch { }
        }
        public void End(PowerUpManager ctx) { }
    }

    sealed class NukeEffect : IPowerUpEffect
    {
        public PowerUpType Type => PowerUpType.Nuke;
        public bool IsTimed => false;
        public float DurationSeconds => 0f;
        public void Apply(PowerUpManager ctx, GameObject collector)
        {
            ctx.RaiseNuke(collector);
            try { TT.Observer.Instance?.Notify("powerup.nuke"); } catch { }
        }
        public void End(PowerUpManager ctx) { }
    }

    sealed class DoublePointsEffect : IPowerUpEffect
    {
        public PowerUpType Type => PowerUpType.DoublePoints;
        public bool IsTimed => true;
        public float DurationSeconds => 30f;

        public void Apply(PowerUpManager ctx, GameObject collector)
        {
            float dur = ctx.GetConfiguredDuration(PowerUpType.DoublePoints);
            ctx.StartOrRefreshDoublePoints(dur);

            try
            {
                TT.Observer.Instance?.Notify("powerup.doublepoints.started"); // event cũ (nếu bạn còn xài)
                TT.Observer.Instance?.NotifyWithData("hud.powerup.timed.started", (PowerUpType.DoublePoints, dur)); // ✅ HUD timer
            }
            catch { }
        }
        public void End(PowerUpManager ctx) { }
    }

    sealed class InstaKillEffect : IPowerUpEffect
    {
        public PowerUpType Type => PowerUpType.InstaKill;
        public bool IsTimed => true;
        public float DurationSeconds => 30f;
        public void Apply(PowerUpManager ctx, GameObject collector)
        {
            float dur = ctx.GetConfiguredDuration(PowerUpType.InstaKill);
            ctx.StartOrRefreshInstaKill(dur);

            try
            {
                TT.Observer.Instance?.Notify("powerup.instakill.started");
                TT.Observer.Instance?.NotifyWithData("hud.powerup.timed.started", (PowerUpType.InstaKill, dur)); // ✅ HUD timer
            }
            catch { }
        }
        public void End(PowerUpManager ctx) { }
    }
}
