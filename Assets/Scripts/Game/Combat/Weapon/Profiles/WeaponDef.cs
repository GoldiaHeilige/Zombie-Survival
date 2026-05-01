using System.Collections.Generic;
using Game.Combat.Weapon.Recoil;
using UnityEngine;
using TT;

[CreateAssetMenu(menuName = "Game/Weapons/WeaponDef")]
public class WeaponDef : ScriptableObject
{
    public enum FireMode { Semi, Auto, Burst }
    public enum FireKind { Hitscan, Projectile }

    public enum AnimWeaponType
    {
        None = 0,
        Rifle = 1,
        Pistol = 2,
        Shotgun = 3,
        // sau này nếu cần thêm: Shotgun = 3, SMG = 4, ...
    }

    [Header("Identity")]
    public string weaponName = "Name Here";
    public string weaponId = "pistol_9mm";
    public FireMode fireMode = FireMode.Auto;
    public FireKind fireKind = FireKind.Hitscan;

    [Header("Damage & Range")]
    public DamageType damageType = DamageType.Bullet;
    public float baseDamage = 35f;
    public float maxDistance = 100f; // hitscan
    public float projectileSpeed = 60f; // m/s (projectile)

    [Header("Penetration (Hitscan)")]
    [Tooltip("Bật đạn xuyên qua nhiều mục tiêu (chỉ hitscan).")]
    public bool enablePenetration = false;

    [Min(0)]
    [Tooltip("Số lần xuyên tối đa qua mục tiêu (0 = hành vi cũ, chỉ hit 1 mục tiêu).")]
    public int maxPenetrations = 0;

    [Range(0f, 1f)]
    [Tooltip("Mỗi lần xuyên qua 1 mục tiêu, damage nhân với hệ số này. VD 0.8 = giảm 20% mỗi lần xuyên.")]
    public float damageMultiplierPerPenetration = 0.8f;

    [Min(0f)]
    [Tooltip("Damage tối thiểu (sau khi áp multiplier), giúp tránh về 0 nếu xuyên nhiều.")]
    public float minDamageAfterPenetration = 0f;

    [Header("Recoil")]
    public RecoilProfile RecoilProfile;

    [Header("Cadence")]
    public float rpm = 450f;      // rounds per minute
    public int burstCount = 3;    // nếu FireMode.Burst
    public float burstInterval = 0.06f;

    [Header("Shotgun Pellets (Hitscan)")]
    [Tooltip("Nếu > 1 thì bắn theo dạng shotgun pellets (mỗi pellet là 1 raycast).")]
    public int pelletCount = 1;

    [Tooltip("Độ spread (độ) khi HIP.")]
    public float pelletSpreadDegHip = 4.0f;

    [Tooltip("Độ spread (độ) khi ADS.")]
    public float pelletSpreadDegADS = 2.0f;

    [Header("Ammo")]
    public float reloadTime = 1.0f;
    public int magSize = 12;
    public int startReserve = 60;
    public int maxReserve = 180;

    [Header("Weapon ADS")]
    public float adsInTime = 0.12f;
    public float adsOutTime = 0.14f;
    public float adsFOV = 60f;

    [Header("Upgrade")]
    [Tooltip("Nếu null thì súng này không có bản Pack-a-Punch.")]
    public WeaponDef upgradedVersion;

    [Header("FX (optional)")]
    public GameObject muzzleFlashPrefab;
    public GameObject worldMuzzleFlashPrefab;
    public GameObject tracerPrefab;
    public GameObject projectilePrefab;

    [Header("Hitbox Multipliers")]
    [Tooltip("Nếu list rỗng hoặc không tìm thấy hitboxId → dùng Default multiplier = 1.")]
    public List<HitboxMultiplierEntry> hitboxMultipliers = new();

    [System.Serializable]
    public struct HitboxMultiplierEntry
    {
        public HitboxId hitboxId;
        public float multiplier;
    }

    public float GetHitboxMultiplier(HitboxId id)
    {
        if (hitboxMultipliers == null) return 1f;
        for (int i = 0; i < hitboxMultipliers.Count; i++)
        {
            if (hitboxMultipliers[i].hitboxId == id)
                return Mathf.Max(0f, hitboxMultipliers[i].multiplier);
        }
        return 1f;
    }

    [Header("Critical")]
    [Range(0f, 1f)]
    [Tooltip("Tỉ lệ chí mạng cho mọi hitbox (không phụ thuộc head/body).")]
    public float critChance = 0f;

    [Min(1f)]
    [Tooltip("Hệ số nhân sát thương khi crit.")]
    public float critMultiplier = 1.5f;

    [Header("Limb Detach")]
    [Min(0f)]
    public float limbDetachMultiplier = 1f; // 0 = không thể phá limb

    [Header("Shell Casing (optional)")]
    public GameObject casingPrefab;

    [Tooltip("Tên transform trong prefab để làm điểm văng vỏ. VD: Eject, EjectionPort, ShellEject")]
    public string[] casingEjectNameCandidates = new[] { "Eject", "EjectionPort", "ShellEject", "CasingEject" };

    [Tooltip("Lực văng vỏ (min/max).")]
    public Vector2 casingEjectForce = new Vector2(1.5f, 3.0f);

    [Tooltip("Lực xoay vỏ (min/max).")]
    public Vector2 casingTorque = new Vector2(0.5f, 2.0f);

    [Tooltip("Thời gian tự huỷ vỏ (giây).")]
    public float casingLifetime = 6f;

    [Header("Shell Audio (FP only)")]
    [Tooltip("SFX vỏ chạm đất (chỉ FP/local).")]
    public AudioEventSO casingImpactAudio;

    [Header("Animation")]
    [Tooltip("Dùng cho param WeaponType trong Animator: 0=None, 1=Rifle, 2=Pistol,...")]
    public AnimWeaponType animWeaponType = AnimWeaponType.Rifle;

    // THAY ĐOẠN FX CŨ BẰNG ĐOẠN NÀY

    [System.Serializable]
    public class ImpactVFXSet
    {
        public GameObject Default;
        public GameObject Flesh;
        public GameObject Metal;
        public GameObject Wood;
        public GameObject Concrete;
        public GameObject Glass;
        public GameObject Dirt;
        public GameObject Water;
    }

    [System.Serializable]
    public class ImpactSFXSet
    {
        public AudioEventSO Default;
        public AudioEventSO Flesh;
        public AudioEventSO Metal;
        public AudioEventSO Wood;
        public AudioEventSO Concrete;
        public AudioEventSO Glass;
        public AudioEventSO Dirt;
        public AudioEventSO Water;
    }

    [Header("Impact VFX")]
    public ImpactVFXSet impactVFX;

    [Header("Impact SFX")]
    public ImpactSFXSet impactSFX;

    [Header("Audio")]
    [Tooltip("Tiếng bắn FP / local, dùng AudioEventSO (category nên để FirstPerson).")]
    public AudioEventSO fireAudio;

    [Tooltip("Tiếng reload FP / local, dùng AudioEventSO (category nên để FirstPerson, có thể để loop).")]
    public AudioEventSO reloadAudio;

    [Header("View (1st-person)")]
    public GameObject viewPrefab;

    [Header("View (3rd-person / world model)")]
    [Tooltip("Súng 3D gắn lên model nhân vật (tay phải, holster, v.v.). Không dùng cho pickup.")]
    public GameObject thirdPersonPrefab;

    // === FIRST PERSON HIP OFFSET ===
    [Header("View (1st-person Hip Offset)")]
    [Tooltip("Local position offset HIP cho rig FP, relative với parent của WeaponADSAlign/refHip.")]
    public Vector3 fpHipPositionOffset = Vector3.zero;

    [Tooltip("Local rotation offset HIP (Euler) cho rig FP, relative với parent của WeaponADSAlign/refHip.")]
    public Vector3 fpHipRotationOffsetEuler = Vector3.zero;

    [Header("World (pickup)")]
    public GameObject worldPrefab;

    [Header("Raycast")]
    public LayerMask hitMask = ~0;

    // --- HIPS ---
    [Tooltip("Offset vị trí local của súng (hip) so với WeaponSocket_Hand_R.")]
    public Vector3 thirdPersonPositionOffset;

    [Tooltip("Offset xoay local (Euler) của súng (hip) so với WeaponSocket_Hand_R.")]
    public Vector3 thirdPersonRotationOffsetEuler;

    [Tooltip("Scale local của súng 3P khi hip. Mặc định = (1,1,1).")]
    public Vector3 thirdPersonScale = Vector3.one;

    // --- ADS ---
    [Header("View (3rd-person ADS offset)")]
    [Tooltip("Bật nếu muốn dùng offset riêng khi ADS cho world model.")]
    public bool useADSOffsetsForWorldModel = false;

    [Tooltip("Offset vị trí local khi ADS (nếu tắt, sẽ dùng lại offset hip).")]
    public Vector3 thirdPersonADSPositionOffset;

    [Tooltip("Offset xoay local (Euler) khi ADS (nếu tắt, sẽ dùng lại rot hip).")]
    public Vector3 thirdPersonADSRotationOffsetEuler;

    [Tooltip("Scale local khi ADS (nếu (0,0,0) hoặc tắt, sẽ dùng lại scale hip).")]
    public Vector3 thirdPersonADSScale = Vector3.one;
}
