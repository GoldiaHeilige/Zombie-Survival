using UnityEngine;

[DefaultExecutionOrder(-200)] // chạy sớm để editor/instantiation ổn định
public class HitboxGroup : MonoBehaviour
{
    [Tooltip("Nếu để trống sẽ tự tìm IDamageable ở parent gần nhất.")]
    public MonoBehaviour damageable; // phải implement IDamageable

    [ContextMenu("Bind All Child Hitboxes")]
    public void BindAll()
    {
        var dmg = damageable as IDamageable;
        if (dmg == null)
            dmg = GetComponentInParent<IDamageable>();

        if (dmg == null)
        {
            Debug.LogWarning($"[HitboxGroup] No IDamageable found for {name}");
            return;
        }

        var mono = dmg as MonoBehaviour;
        var boxes = GetComponentsInChildren<Hitbox>(true);
        int count = 0;
        foreach (var hb in boxes)
        {
            if (hb == null) continue;
            hb.damageableOverride = mono; // gắn thẳng
            count++;
        }
        /*Debug.Log($"[HitboxGroup] Bound {count} hitboxes → {mono?.name}");*/
    }

#if UNITY_EDITOR
    void Reset() { BindAll(); }
    void OnValidate() { BindAll(); }
#endif
}
