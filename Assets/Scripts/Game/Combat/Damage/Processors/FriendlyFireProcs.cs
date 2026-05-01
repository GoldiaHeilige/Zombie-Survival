using UnityEngine;

public class FriendlyFireProcessor : IDamageProcessor
{
    public bool allowFriendlyFire = false;

    public bool Process(ref DamageEvent e)
    {
        var victim = e.victim ?? e.victimGO?.GetComponent<IDamageable>();

        TeamId atkTeam = TeamId.Neutral;
        if (e.attacker)
        {
            var atk = e.attacker.GetComponentInParent<IDamageable>();
            if (atk != null) atkTeam = atk.GetTeam();
        }
        TeamId vicTeam = victim != null ? victim.GetTeam() : TeamId.Neutral;

        if (!allowFriendlyFire && atkTeam != TeamId.Neutral && atkTeam == vicTeam)
        {
            e.friendlyFireIgnored = true;
            Debug.Log($"[DMG-FF] BLOCK friendly-fire atk={atkTeam} -> vic={vicTeam}");
            return false;
        }
        return true;
    }

}
