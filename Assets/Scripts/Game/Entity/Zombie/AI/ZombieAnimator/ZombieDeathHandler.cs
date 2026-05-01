using System.Collections;
using UnityEngine;

public class ZombieDeathHandler : MonoBehaviour, IPoolable
{
    [Header("Anim")]
    public Animator animator;
    public string deathStateName = "Z_Death";

    [Tooltip("Tên tham số bool dùng để chuyển sang state chết (ví dụ: \"Die\")")]
    public string deathBoolParam = "Die";

    [Tooltip("Thời gian chờ trước khi despawn (giây)")]
    public float deathAnimDuration = 1.6f;

    ZombieBrain brain;
    ZombieNetworkAnimator netAnim;
    bool _isDespawning;
    int _dieHash;

    void Awake()
    {
        brain = GetComponent<ZombieBrain>();
        netAnim = GetComponent<ZombieNetworkAnimator>();

        if (!animator)
            animator = GetComponentInChildren<Animator>();

        if (deathAnimDuration <= 0f)
            deathAnimDuration = 1.5f;

        if (!string.IsNullOrEmpty(deathBoolParam))
            _dieHash = Animator.StringToHash(deathBoolParam);
        else
            _dieHash = 0;
    }

    // ===== IPoolable =====

    public void OnSpawned()
    {
        _isDespawning = false;

        var hitboxes = GetComponentsInChildren<Hitbox>(true);
        foreach (var hb in hitboxes)
        {
            var col = hb.GetComponent<Collider>();
            if (col) col.enabled = true;
        }

        if (animator && _dieHash != 0)
        {
            animator.SetBool(_dieHash, false);
        }
    }

    public void OnDespawned()
    {
        // không cần gì thêm
    }

    // ===== API =====

    public void PlayDeathAndDespawn()
    {
        if (_isDespawning) return;
        _isDespawning = true;

        DisableHitboxes();

        // === FIXED: Animation logic ===
        // LUÔN thử dùng netAnim trước (nó đã được sửa để hỗ trợ cả SP)
        if (netAnim != null)
        {
            netAnim.PlayDeath();
        }
        // Fallback: nếu không có netAnim
        else if (animator && _dieHash != 0)
        {
            animator.SetBool(_dieHash, true);
        }

        // 2) Đợi 1 khoảng chắc chắn đủ cho anim
        if (animator && deathAnimDuration > 0f)
            StartCoroutine(WaitDeathAnimation());
        else
            DespawnNow();
    }

    IEnumerator WaitDeathAnimation()
    {
        yield return new WaitForSeconds(deathAnimDuration);
        DespawnNow();
    }

    void DespawnNow()
    {
        var hub = AIPortHub.I;
        if (hub != null && hub.Spawn != null)
        {
            hub.Spawn.Despawn(gameObject);
            return;
        }

        Destroy(gameObject);
    }

    void DisableHitboxes()
    {
        // Chỉ tắt các collider thuộc hitbox (không tắt collider nền nếu bạn có)
        var hitboxes = GetComponentsInChildren<Hitbox>(true);
        foreach (var hb in hitboxes)
        {
            var col = hb.GetComponent<Collider>();
            if (col) col.enabled = false;
        }
    }

}
