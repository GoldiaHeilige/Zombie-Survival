using UnityEngine;

public class SPDamageDriver : MonoBehaviour, IDamageDriver
{
    public static SPDamageDriver Instance { get; private set; }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DamageRouter.SetDriver(this);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public DamageResult Apply(in DamageEvent eIn)
    {
        if (DamageSystem.Instance == null)
        {
            Debug.LogError("[SPDamageDriver] DamageSystem missing!");
            return default;
        }

        // Tạo bản copy để bổ sung đầy đủ thông tin
        DamageEvent e = eIn;

        // =============================
        // 1) Resolve attacker (fallback hợp lý)
        // =============================
        GameObject attackerGO = null;

        if (eIn.attacker != null)
            attackerGO = eIn.attacker;
        else
            attackerGO = gameObject; // fallback: chính driver

        e.attacker = attackerGO;

        // =============================
        // 2) Resolve victim
        // =============================
        GameObject victimGO = null;
        IDamageable victim = null;

        // Nếu caller đã set victimGO thì dùng luôn
        if (eIn.victimGO != null)
        {
            victimGO = eIn.victimGO;

            // Ưu tiên PlayerLifeController (để xử lý Downed/Dead đúng)
            var lc = victimGO.GetComponent<PlayerLifeController>();
            if (lc != null) victim = lc;

            // Fallback
            if (victim == null)
                victim = victimGO.GetComponent<IDamageable>();
        }

        // Nếu caller chỉ set victim, chưa có victimGO
        if (victim == null && victimGO == null && eIn.victim != null)
        {
            victim = eIn.victim;
            if (victim is MonoBehaviour mb)
                victimGO = mb.gameObject;
        }

        if (victimGO == null)
        {
            Debug.LogWarning("[SPDamageDriver] Cannot resolve victimGO. Cancel damage.");
            return new DamageResult { isApplied = false };
        }

        if (victim == null)
        {
            Debug.LogWarning("[SPDamageDriver] Cannot resolve victim (IDamageable). Cancel damage.");
            return new DamageResult { isApplied = false };
        }

        e.victimGO = victimGO;
        e.victim = victim;

        // =============================
        // 3) Resolve collider (hitbox)
        // =============================
        if (eIn.hitCollider == null)
        {
            var col = victimGO.GetComponentInChildren<Collider>();
            e.hitCollider = col;
        }

        // Nếu caller không cung cấp hitbox, fallback Default
        if (eIn.hitboxId == 0)
            e.hitboxId = HitboxId.Default;

        // =============================
        // 4) Weapon ID đảm bảo hợp lệ
        // =============================
        int key = WeaponIdRegistry.GetKey(eIn.weaponId);
        var def = WeaponIdRegistry.GetDef(key);

        if (def != null)
            e.weaponId = def.weaponId;
        else
            e.weaponId = default; // fallback nếu invalid


        // =============================
        // 5) Fill dữ liệu vật lý nếu thiếu
        // =============================
        if (eIn.distance <= 0f && eIn.attacker && victimGO)
        {
            e.distance = Vector3.Distance(eIn.attacker.transform.position, victimGO.transform.position);
        }

        if (eIn.shotDirection == Vector3.zero && attackerGO && victimGO)
        {
            e.shotDirection = (victimGO.transform.position - attackerGO.transform.position).normalized;
        }

        if (eIn.hitPoint == Vector3.zero)
            e.hitPoint = victimGO.transform.position;

        if (eIn.hitNormal == Vector3.zero)
            e.hitNormal = -e.shotDirection;

        e.time = Time.time;

     //   Debug.Log("[SPDriver] Apply CALLED, victimGO=" + eIn.victimGO);
        // =============================
        // 6) APPLY DAMAGE CHUẨN PIPELINE
        // =============================
        return DamageSystem.Instance.Apply(e);
    }
}
