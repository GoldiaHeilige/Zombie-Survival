using System;

public enum WeaponState { None, Idle, Aiming, Firing, Reloading, Equipping }

/// <summary>
/// Nhẹ, thuần C#: quản lý trạng thái súng + phát sự kiện.
/// WeaponController chỉ gọi các "triggers" bên dưới.
/// </summary>
public sealed class WeaponFSM
{
    public WeaponState State { get; private set; } = WeaponState.None;
    public bool IsADS { get; private set; }

    /// <summary>(prev, next)</summary>
    public event Action<WeaponState, WeaponState> OnStateChanged;

    // internal: chống nhấp nháy giữa Firing/Idle trong nhịp bắn
    float _lastShotAt;
    float _fireHoldGrace = 0.02f; // buffer sau phát bắn để không tụt state quá sớm

    public void Reset()
    {
        IsADS = false;
        ChangeState(WeaponState.None);
    }

    public void OnEquipping()
    {
        ChangeState(WeaponState.Equipping);
    }

    public void OnEquippedIdle()
    {
        ChangeState(WeaponState.Idle);
    }

    public void OnUnequip()
    {
        ChangeState(WeaponState.None);
        IsADS = false;
    }

    public void SetADS(bool ads)
    {
        IsADS = ads;
        if (State != WeaponState.Firing && State != WeaponState.Reloading)
        {
            ChangeState(ads ? WeaponState.Aiming : WeaponState.Idle);
        }
    }

    public void BeginReload()
    {
        ChangeState(WeaponState.Reloading);
    }

    public void EndReload()
    {
        ChangeState(IsADS ? WeaponState.Aiming : WeaponState.Idle);
    }

    public void FireTick(bool canFireNow, bool fireHeld)
    {
        // Được gọi mỗi frame khi đang giữ/nhả cò để quyết định Firing/Idle/Aiming
        if (State == WeaponState.Reloading || State == WeaponState.Equipping) return;

        if (fireHeld && canFireNow)
        {
            if (State != WeaponState.Firing)
                ChangeState(WeaponState.Firing);
        }
        else if (State == WeaponState.Firing)
        {
            // Chỉ thoát khỏi Firing sau grace period
            if (TimeLike() - _lastShotAt > _fireHoldGrace)
            {
                ChangeState(IsADS ? WeaponState.Aiming : WeaponState.Idle);
            }
        }
    }

    public void NotifyFiredOnce()
    {
        _lastShotAt = TimeLike();
        // đảm bảo đang ở Firing (trong trường hợp bắn 1 phát đơn)
        if (State != WeaponState.Firing) ChangeState(WeaponState.Firing);
    }

    // Cho test thuần C# nếu cần (có thể inject time)
    Func<float> _timeProvider;
    public WeaponFSM(Func<float> timeProvider = null)
    {
        _timeProvider = timeProvider;
    }
    float TimeLike() => _timeProvider != null ? _timeProvider() : UnityEngine.Time.time;

    void ChangeState(WeaponState next)
    {
        if (State == next) return;
        var prev = State;
        State = next;
        OnStateChanged?.Invoke(prev, next);
    }
}
