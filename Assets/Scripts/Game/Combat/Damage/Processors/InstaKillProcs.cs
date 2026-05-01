using UnityEngine;

/// <summary>
/// If InstaKillActive, convert eligible player->enemy damage into lethal damage.
/// Put this BEFORE ClampProcessor so clamp can cap finalDamage.
/// </summary>
public class InstaKillProcessor : IDamageProcessor
{
    public bool Process(ref DamageEvent e)
    {
        if (!PowerUpManager.InstaKillActive) return true;

        // Resolve attacker team
        TeamId atkTeam = TeamId.Neutral;
        if (e.attacker)
        {
            var atk = e.attacker.GetComponentInParent<IDamageable>();
            if (atk != null) atkTeam = atk.GetTeam();
        }

        // Resolve victim team (DamageSystem already resolves e.victim in step 1, but be safe)
        var victim = e.victim ?? (e.victimGO ? e.victimGO.GetComponent<IDamageable>() : null);
        TeamId vicTeam = victim != null ? victim.GetTeam() : TeamId.Neutral;

        // Only apply for Player -> Enemy
        if (atkTeam == TeamId.Player && vicTeam == TeamId.Enemy)
        {
            // Set huge damage. ClampProcessor will cap it to maxDamage (default 10000).
            e.baseDamage = 999999f;

            // Optional: mark as critical for UI/feedback if you want
            // e.isCritical = true;
        }

        return true;
    }
}
