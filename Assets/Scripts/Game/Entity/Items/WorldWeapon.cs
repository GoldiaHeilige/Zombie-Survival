using UnityEngine;
using System;
using Fusion;

public class WorldWeapon : MonoBehaviour
{
    [Header("Weapon Data")]
    public WeaponDef weaponDef;
    public string weaponName;

    [Header("Visuals")]
    public Transform model;
    public GameObject pickupVFX;

    [Header("Pickup")]
    [Tooltip("Tạm khoá nhặt cho đến thời điểm này (Time.time).")]
    [SerializeField] private float _pickupBlockUntil = 0f;
    private float _emptySince = -1f;

    [Header("Lifetime")]
    [Tooltip("Khi mag + reserve đều 0, sau số giây này sẽ tự despawn.")]
    public float emptyDespawnDelay = 90f; // 1.5 phút, muốn thì chỉnh trong Inspector
    public bool IsPickupAvailable => weaponDef != null && Time.time >= _pickupBlockUntil;

    [Header("Runtime")]
    public string runtimeGuid;
    public int magOnGround = 0;
    public int reserveOnGround = 0;

    void Awake()
    {
        if (string.IsNullOrEmpty(runtimeGuid))
            runtimeGuid = System.Guid.NewGuid().ToString();
    }

    void Update()
    {
        // Nếu còn đạn (mag hoặc reserve) thì không đếm giờ
        if (magOnGround > 0 || reserveOnGround > 0)
        {
            _emptySince = -1f;
            return;
        }

        // Cả mag và reserve đều 0 → bắt đầu (hoặc tiếp tục) đếm
        if (_emptySince < 0f)
        {
            _emptySince = Time.time;
            return;
        }

        // Nếu chưa đủ delay thì thôi
        if (Time.time - _emptySince < emptyDespawnDelay)
            return;

        // Đến đây là rỗng lâu đủ thời gian → Host/SP sẽ despawn
#if FUSION_WEAVER
        var no = GetComponent<NetworkObject>();
        if (no && no.Runner != null)
        {
            // Chỉ cho StateAuthority (thường là host) được quyền despawn
            if (!no.HasStateAuthority)
                return;
        }
#endif

        // Gọi chung một đường hủy để SP & MP dùng cùng logic
        OnPickedUp(); // hoặc nếu muốn rõ ràng thì tạo hàm DestroySelf() rồi gọi
    }

    public void InitFromDrop(string guid, WeaponDef def, int mag, int reserve)
    {
        weaponDef = def;
        weaponName = def ? def.weaponName : "";
        magOnGround = Mathf.Max(0, mag);
        reserveOnGround = Mathf.Max(0, reserve);
        // ép ghi GUID từ slot khi drop
        runtimeGuid = string.IsNullOrEmpty(guid) ? Guid.NewGuid().ToString() : guid;
        _pickupBlockUntil = -1f;
    }

    public void BlockPickupFor(float seconds)
    {
        _pickupBlockUntil = Time.time + Mathf.Max(0f, seconds); // đặt tuyệt đối theo “bây giờ”
    }

    void Reset()
    {
        model = transform.childCount > 0 ? transform.GetChild(0) : null;
    }

    public void OnPickedUp()
    {
        if (pickupVFX != null)
            Instantiate(pickupVFX, transform.position, Quaternion.identity);

        // Nếu có NetworkObject thì Despawn để mọi client cùng biến mất
#if FUSION_WEAVER
        var no = GetComponent<NetworkObject>();
        if (no && no.Runner != null)
        {
            no.Runner.Despawn(no);
            return;
        }
#endif
        // Fallback single/local
        Destroy(gameObject);
    }
}
