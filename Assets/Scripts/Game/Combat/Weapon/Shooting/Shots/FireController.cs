using System;
using System.Collections;
using UnityEngine;

public class FireController : MonoBehaviour
{
    [Header("Runtime")]
    public bool IsBusy { get; private set; }
    Coroutine _runningBurst;
    float _lastShotAt = -999f;

    IShot _shot;
    public event Action<bool /*isADS*/, float /*dtSinceLast*/> OnBulletFired;


    public void Reset(WeaponDef def)
    {
        StopAllCoroutines();
        _runningBurst = null;
        IsBusy = false;
        _lastShotAt = -999f;

        // Chọn strategy theo def.fireKind (chỉ khởi tạo nếu có)
        _shot = null;
        if (def != null)
        {
            if (def.fireKind == WeaponDef.FireKind.Hitscan)
            {
                if (def.pelletCount > 1)
                    _shot = new ShotgunPelletShot();   // <-- mới
                else
                    _shot = new HitscanShot();
            }

            // Nếu bạn có ProjectileShot, mở khóa dòng dưới:
            // else if (def.fireKind == WeaponDef.FireKind.Projectile)
            // {
            //     _shot = new ProjectileShot();
            // }
        }
    }

    public bool CanFire(float now, WeaponDef def, float rofMult = 1f)
    {
        if (def == null || _shot == null) return false;

        rofMult = Mathf.Max(0.01f, rofMult);
        float rpm = Mathf.Max(1f, def.rpm) * rofMult;

        float rofSec = 60f / rpm;
        return now - _lastShotAt >= rofSec && !IsBusy;
    }


    /// <summary>Dừng burst/coroutine đang chạy (nếu có).</summary>
    public void Abort()
    {
        if (_runningBurst != null)
        {
            StopCoroutine(_runningBurst);
            _runningBurst = null;
        }
        IsBusy = false;
    }

    /// <summary>Entry bắn. Gọi từ WeaponController khi đủ điều kiện bắn.</summary>
    public void Fire(ref WeaponContext ctx,
                     AmmoModule ammo,
                     WeaponDef def,
                     Action onAmmoChanged = null,
                     Action onCommittedShot = null,
                     float rofMult = 1f)
    {
        if (def == null || _shot == null || ammo == null) return;

        switch (def.fireMode)
        {
            case WeaponDef.FireMode.Semi:
            case WeaponDef.FireMode.Auto:
                FireOnce(ref ctx, ammo, def, onAmmoChanged, onCommittedShot);
                break;

            case WeaponDef.FireMode.Burst:
                if (_runningBurst == null)
                    _runningBurst = StartCoroutine(BurstRoutine(ctx, ammo, def, onAmmoChanged, onCommittedShot, rofMult));
                break;
        }
    }


    void FireOnce(ref WeaponContext ctx,
                  AmmoModule ammo,
                  WeaponDef def,
                  Action onAmmoChanged,
                  Action onCommittedShot)
    {
        // Chỉ commit khi trừ đạn được
        /*        if (!ammo.TryConsumeOne()) return;*/

        float now = Time.time;
        float dtSince = (_lastShotAt < 0f) ? 999f : (now - _lastShotAt);

        _shot.Fire(ref ctx);              // thực sự bắn 1 viên
        _lastShotAt = Time.time;

        onAmmoChanged?.Invoke();
        OnBulletFired?.Invoke(ctx.isADS, dtSince);
        onCommittedShot?.Invoke();        // -> Recoil.OnShot(...), FX, FSM.NotifyFiredOnce()
    }

    IEnumerator BurstRoutine(WeaponContext ctx,
                             AmmoModule ammo,
                             WeaponDef def,
                             Action onAmmoChanged,
                             Action onCommittedShot,
                             float rofMult)
    {
        IsBusy = true;

        rofMult = Mathf.Max(0.01f, rofMult);
        int burstCount = Mathf.Max(1, def.burstCount);

        float rpm = Mathf.Max(1f, def.rpm) * rofMult;
        float rofSec = 60f / rpm;

        var localCtx = ctx;

        for (int i = 0; i < burstCount; i++)
        {
            float now = Time.time;
            float dtSince = (_lastShotAt < 0f) ? 999f : (now - _lastShotAt);

            _shot.Fire(ref localCtx);
            _lastShotAt = now;

            onAmmoChanged?.Invoke();
            OnBulletFired?.Invoke(localCtx.isADS, dtSince);
            onCommittedShot?.Invoke();

            if (i < burstCount - 1)
            {
                float nextAt = _lastShotAt + rofSec;
                while (Time.time < nextAt) yield return null;
            }
        }

        IsBusy = false;
        _runningBurst = null;
    }

}
