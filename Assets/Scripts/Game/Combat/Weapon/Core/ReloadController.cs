using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Điều phối reload ngoài WeaponController, giữ coroutine & trạng thái IsReloading.
/// Sử dụng AmmoModule.ReloadRoutine(...) với hủy & progress tùy chọn.
/// </summary>
public class ReloadController : MonoBehaviour
{
    [Header("Defaults")]
    [SerializeField] private float defaultReloadSeconds = 2.0f;

    public bool IsReloading { get; private set; }
    Coroutine _running;
    bool _cancelFlag;

    /// <summary> Có thể bắt đầu reload không? </summary>
    public bool CanReload(AmmoModule ammo)
    {
        if (IsReloading || ammo == null) return false;
        return ammo.CanReload();
    }

    /// <summary> Bắt đầu reload với thời lượng cụ thể. </summary>
    public Coroutine Reload(AmmoModule ammo, float seconds, Action<float> onProgress = null)
    {
        if (!CanReload(ammo)) return null;
        StopIfRunning();
        _cancelFlag = false;
        _running = StartCoroutine(ReloadRoutine(ammo, seconds, onProgress));
        return _running;
    }

    /// <summary> Bắt đầu reload với thời lượng mặc định. </summary>
    public Coroutine Reload(AmmoModule ammo, Action<float> onProgress = null)
        => Reload(ammo, defaultReloadSeconds, onProgress);

    /// <summary> Hủy reload hiện tại (nếu có). Không đổ đạn khi bị hủy. </summary>
    public void ForceStop()
    {
        _cancelFlag = true;
        StopIfRunning();
    }

    void StopIfRunning()
    {
        if (_running != null)
        {
            StopCoroutine(_running);
            _running = null;
            IsReloading = false;
        }
    }

    IEnumerator ReloadRoutine(AmmoModule ammo, float seconds, Action<float> onProgress)
    {
        IsReloading = true;

        // Gọi ReloadRoutine với khả năng hủy & progress
        yield return ammo.ReloadRoutine(
            reloadTime: seconds,
            onDone: (mag, reserve) =>
            {
                // Tại đây có thể phát sự kiện/UI nếu muốn
                onProgress?.Invoke(1f);
            },
            shouldCancel: () => _cancelFlag,
            onProgress: onProgress
        );

        IsReloading = false;
        _running = null;
        _cancelFlag = false;
    }
}
