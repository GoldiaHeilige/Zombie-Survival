#if FUSION_WEAVER
using Fusion;
using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class MPDamageDriver : NetworkBehaviour, IDamageDriver
{
    struct DamageNotice
    {
        public NetworkId attackerId;
        public NetworkId victimId;
        public int hpBefore;
        public int hpAfter;
        public int amount;
        public float time;
    }

    // Hàng đợi notice để phát RPC ở tick kế tiếp (không gọi RPC trong RPC)
    private readonly Queue<(NetworkObject atkRef, NetworkObject vicRef, int before, int after, int amt, float t)> _pendingNotices
      = new Queue<(NetworkObject, NetworkObject, int, int, int, float)>();


    public override void Spawned()
    {
        base.Spawned();

        // Offline/SP không có runner -> cứ set bình thường
        if (Runner == null || !Runner.IsRunning)
        {
            DamageRouter.SetDriver(this);
            return;
        }

        // MP: chỉ player local của peer này mới được quyền set global driver
        if (Object != null && Object.IsValid && Object.InputAuthority == Runner.LocalPlayer)
        {
            DamageRouter.SetDriver(this);
        }
    }


    void OnEnable()
    {
        // CHỈ dùng để support Play-In-Scene kiểu SP (không có runner).
        // Trong MP, đừng set driver khi Object chưa Spawned.
        var runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
        if (runner == null)
        {
            DamageRouter.SetDriver(this);
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        // Nếu router đang trỏ vào mình mà mình bị despawn -> clear
        DamageRouter.ClearDriverIfEquals(this);
        base.Despawned(runner, hasState);
    }


    // === Client gọi ===
    public DamageResult Apply(in DamageEvent e)
    {
        // Nếu behaviour chưa Spawned/Runner chưa chạy -> không làm gì cả (tránh Behaviour not initialized)
        if (Runner == null || !Runner.IsRunning || Object == null || !Object.IsValid)
            return default;

        // Host tự áp trực tiếp
        if (Object != null && Object.HasStateAuthority)
        {
            return DamageSystem.Instance != null ? DamageSystem.Instance.Apply(e) : default;
        }

        // Client: gửi yêu cầu lên host
        var victimNO = e.victimGO ? e.victimGO.GetComponentInParent<NetworkObject>() : null;
        var victimId = victimNO ? victimNO.Id : default;

        int weaponKey = WeaponIdRegistry.GetKey(e.weaponId);

        RPC_RequestApplyDamage(
            victimId,
            weaponKey,
            (int)e.damageType,
            (int)e.hitboxId,
            e.baseDamage,
            e.distance,
            e.hitPoint,
            e.hitNormal,
            e.shotDirection
        );

        // Client không tự áp; host sẽ sync kết quả qua HealthState/network
        return default;
    }

    // === Host nhận RPC và áp ===
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestApplyDamage(
        NetworkId victimId,
        int weaponKey,
        int dmgType,
        int hitboxId,
        float baseDamage,
        float distance,
        Vector3 hitPoint,
        Vector3 hitNormal,
        Vector3 shotDir,
        RpcInfo info = default)
    {
        if (Object == null || !Object.HasStateAuthority || DamageSystem.Instance == null) return;

        // Resolve victim
        IDamageable victim = null;
        GameObject victimGO = null;
        if (victimId.IsValid)
        {
            var no = Runner.FindObject(victimId);
            if (no)
            {
                victim = no.GetComponentInChildren<IDamageable>(true);
                if (victim is MonoBehaviour mb) victimGO = mb.gameObject;
                else victimGO = no.gameObject;
            }
        }

        // Nếu không resolve được thì bỏ
        if (victim == null)
        {
            Debug.LogWarning($"[MPDamageDriver] Victim not resolved for NetworkId={victimId}.");
            return;
        }

        GameObject attackerGO = null;

        // Lấy attacker từ PlayerRef gửi RPC
        if (Runner != null)
        {
            var playerNO = Runner.GetPlayerObject(info.Source);
            if (playerNO)
            {
                attackerGO = playerNO.gameObject;
            }
        }

        // Fallback: nếu vì lý do nào đó không lấy được, dùng tạm GO gắn MPDamageDriver
        if (attackerGO == null)
        {
            attackerGO = this.gameObject;
        }


        // Dựng DamageEvent để đi qua cùng pipeline/procs
        var def = WeaponIdRegistry.GetDef(weaponKey);
        var e = new DamageEvent
        {
            attacker = attackerGO,
            victimGO = victimGO,
            victim = victim,
            weaponId = def != null ? def.weaponId : default,
            baseDamage = baseDamage,
            damageType = (DamageType)dmgType,
            distance = distance,
            hitPoint = hitPoint,
            hitNormal = hitNormal,
            shotDirection = shotDir,
            hitCollider = victimGO ? victimGO.GetComponent<Collider>() : null,
            hitboxId = (HitboxId)hitboxId,
            time = Time.time
        };

        var res = DamageSystem.Instance.Apply(e);
        // Impact FX đã có đường RPC riêng RPC_SpawnImpact trong FusionNetBridge (nếu bạn dùng), không làm ở đây.

        // [ADD] Mirror notice cho mọi máy để HUD client cũng log
        if (res.isApplied)
        {
            // Lấy NetworkObjectRef an toàn cho RPC
            NetworkObject atkRef = default, vicRef = default;

            var atkNO = e.attacker ? e.attacker.GetComponentInParent<NetworkObject>() : null;
            if (atkNO) atkRef = atkNO;

            var vicNO = victimGO ? victimGO.GetComponentInParent<NetworkObject>() : null;
            if (vicNO) vicRef = vicNO;

            int before = Mathf.RoundToInt(res.remainingHealth + res.finalDamage);
            int after = Mathf.RoundToInt(res.remainingHealth);
            int amt = Mathf.RoundToInt(res.finalDamage);

            // KHÔNG gọi RPC tại đây. Chỉ xếp hàng để gửi ở tick kế tiếp.
            _pendingNotices.Enqueue((atkRef, vicRef, before, after, amt, Time.time));
        }

    }

    public override void FixedUpdateNetwork()
    {
        if (Object == null || !Object.HasStateAuthority) return;

        while (_pendingNotices.Count > 0)
        {
            var n = _pendingNotices.Dequeue();
            RPC_Notice(n.atkRef, n.vicRef, n.before, n.after, n.amt, n.t);
        }
    }


    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_Notice(NetworkObject attackerRef, NetworkObject victimRef,
                        int hpBefore, int hpAfter, int amount, float time, RpcInfo info = default)
    {
        // Dò lại object từ ref (an toàn trên mọi máy)
        NetworkObject atkNO = default, vicNO = default;
        if (Runner.TryFindObject(attackerRef, out var atkFound)) atkNO = atkFound;
        if (Runner.TryFindObject(victimRef, out var vicFound)) vicNO = vicFound;

        var atkGO = atkNO ? atkNO.gameObject : null;
        var vicGO = vicNO ? vicNO.gameObject : null;

        // Mirror lên HUD client
        DebugDamageHUD.PushMirror(Runner,
            atkNO ? atkNO.Id : default,
            vicNO ? vicNO.Id : default,
            hpBefore, hpAfter, amount, time);
    }

}
#endif
