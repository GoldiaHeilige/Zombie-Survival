using System;
using TT;
using UnityEngine;

[DisallowMultipleComponent]
public class DamageableHealth : MonoBehaviour, IDamageable, IHealthSyncPort
{
    [Header("Team & Health")]
    public TeamId team = TeamId.Enemy;
    public float maxHealth = 100f;
    public float currentHealth = 100f;

    [Header("Hitbox multipliers (legacy display-only)")]
    public float headMultiplier = 2.0f;   // giữ lại để bạn nhìn tham số, nhưng multiplier thật xử lý ở Processor
    public float limbMultiplier = 0.8f;

    public event Action<DamageEvent, DamageResult> OnHit;
    public event Action<DamageEvent, DamageResult> OnDeathLocal;

    public event Action<int, int> OnHpChanged;
    public int Current => Mathf.RoundToInt(currentHealth);
    public int Max => Mathf.RoundToInt(maxHealth);

    public TeamId GetTeam() => team;

    public bool CanTakeDamage(in DamageEvent e)
    {
        // Gate bất tử/iframe nếu cần
        return currentHealth > 0f;
    }

    public DamageResult ApplyDamage(in DamageEvent e)
    {
/*        Debug.Log($"[DamageableHealth] ApplyDamage DIRECTLY CALLED - THIS SHOULD NOT HAPPEN FOR PLAYERS!");
        Debug.Log($"[DamageableHealth] StackTrace: {Environment.StackTrace}");*/

        if (!CanTakeDamage(e))
            return new DamageResult { isApplied = false, isFatal = false, remainingHealth = currentHealth };

        // 1) Tính sát thương thực sự áp dụng (đề phòng clamp/proc đã đổi e.baseDamage)
        float dmg = Mathf.Max(0f, e.baseDamage);

        // 2) Trừ MỘT LẦN
        float old = currentHealth;
        currentHealth = Mathf.Max(0f, old - dmg);

        // 3) Kết quả sau khi trừ
        bool fatal = currentHealth <= 0f;
        var result = new DamageResult
        {
            isApplied = dmg > 0f,
            finalDamage = dmg,
            isFatal = fatal,
            remainingHealth = currentHealth
        };

        // 4) Log sau khi gán
  //      Debug.Log($"[HP] {name}({team}) -{dmg:0.##} from '{(e.attacker ? e.attacker.name : "ENV")}' -> {old:0.##} -> {currentHealth:0.##}");


        // 5) Bắn event sau khi state đã đúng
        OnHpChanged?.Invoke(Mathf.RoundToInt(old), Mathf.RoundToInt(currentHealth));

        if (result.isApplied && !result.isFatal)
        {
            OnHit?.Invoke(e, result);
        }

        if (fatal)
        {
            OnDeathLocal?.Invoke(e, result);

#if FUSION_WEAVER
            // MP: client không chạy ApplyDamage -> phải pulse để mọi máy phát death audio
            var zs = GetComponent<ZombieStateNet>();
            zs?.PulseDeath();
#endif
        }

        return result;
    }


    public void ResetHealth()
    {
        currentHealth = Mathf.Max(0f, maxHealth);
        // nếu có cờ/logic khác cho trạng thái chết thì reset tại đây (isDead = false, v.v.)
    }

    public void SetCurrentSilent(int value)
    {
        currentHealth = Mathf.Clamp(value, 0f, maxHealth);
        // không gọi OnHit/OnDeath/Apply nữa — chỉ cập nhật số để UI/FX theo kịp
    }

    public void SetCurrentFromNet(int value)
    {
        int old = Mathf.RoundToInt(currentHealth);
        int clamped = Mathf.Clamp(value, 0, Mathf.RoundToInt(maxHealth));
        if (old == clamped) return;

        currentHealth = clamped;
        // Bắn event UI để HUD cập nhật trên client:
        OnHpChanged?.Invoke(old, clamped);
    }

    public Transform GetAimTarget() => transform;
}
