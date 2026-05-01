using UnityEngine;

public interface IPowerUpEffect
{
    PowerUpType Type { get; }
    bool IsTimed { get; }
    float DurationSeconds { get; } // only used when IsTimed

    /// <summary>Called when power-up is collected (server/authority).</summary>
    void Apply(PowerUpManager ctx, GameObject collector);

    /// <summary>Called when timed effect ends (server/authority).</summary>
    void End(PowerUpManager ctx);
}
