using UnityEngine;

public interface IDamageable
{
    TeamId GetTeam();
    bool CanTakeDamage(in DamageEvent e);
    DamageResult ApplyDamage(in DamageEvent e);
    Transform GetAimTarget(); // để đặt hitmarker/indicator (có thể trả transform thân)
}
