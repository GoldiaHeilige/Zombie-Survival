using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Quản lý đạn cho 1 vũ khí (instance). Không chứa state gameplay khác.
/// - Không tự bắn, không tự gọi FX; chỉ quản trị con số.
/// - Expose API rõ ràng: TryConsumeOne, CanFire, CanReload, IsMagEmpty, IsCompletelyEmpty.
/// - Phát sự kiện Changed(mag,reserve,magSize) mỗi khi có thay đổi.
/// - ReloadRoutine hỗ trợ progress và hủy giữa chừng (shouldCancel).
/// </summary>
[Serializable]
public class AmmoModule
{
    [SerializeField] public int mag;
    [SerializeField] public int reserve;
    [SerializeField] public int magSize;

    /// <summary>Sự kiện đổi đạn (mag, reserve, magSize).</summary>
    public event Action<int, int, int> Changed;

    /// <summary>Thiết lập lại từ WeaponDef.</summary>
    public void ResetFromDef(WeaponDef def, bool fullMag, int? setReserve)
    {
        magSize = Mathf.Max(0, def.magSize);
        mag = fullMag ? magSize : Mathf.Clamp(mag, 0, magSize);
        reserve = setReserve.HasValue ? Mathf.Max(0, setReserve.Value) : Mathf.Max(0, def.startReserve);
        RaiseChanged();
    }

    // ====== Query helpers ======
    public bool CanFire() => mag > 0;
    public bool CanReload() => mag < magSize && reserve > 0;

    public bool IsMagEmpty => mag <= 0;
    public bool HasReserve => reserve > 0;
    public bool IsCompletelyEmpty => mag <= 0 && reserve <= 0;

    public float MagFill01 => magSize > 0 ? Mathf.Clamp01(mag / (float)magSize) : 0f;

    // ====== Mutations ======

    /// <summary>Trừ 1 viên nếu còn. Trả về true nếu thực sự trừ được.</summary>
    public bool TryConsumeOne()
    {
        if (mag > 0)
        {
            mag--;
            RaiseChanged();
            return true;
        }
        return false;
    }

    /// <summary>Đổ đạn từ reserve sang mag (không vượt magSize). Trả về số viên đã chuyển.</summary>
    public int FillFromReserve(int want)
    {
        if (want <= 0 || reserve <= 0) return 0;
        int space = Mathf.Max(0, magSize - mag);
        int take = Mathf.Min(space, reserve, want);
        if (take <= 0) return 0;

        mag += take;
        reserve -= take;
        RaiseChanged();
        return take;
    }

    /// <summary>Set trực tiếp (dùng thận trọng; chủ yếu phục vụ load/save).</summary>
    public void SetCounts(int newMag, int newReserve, int newMagSize)
    {
        magSize = Mathf.Max(0, newMagSize);
        mag = Mathf.Clamp(newMag, 0, magSize);
        reserve = Mathf.Max(0, newReserve);
        RaiseChanged();
    }

    // ====== Reload ======

    /// <summary>
    /// Reload “một lần” sau reloadTime giây: đổ tối đa để đầy băng (tùy theo reserve).
    /// - onDone(mag, reserve) sẽ được gọi sau khi hoàn tất hoặc sau khi bị hủy (nhưng đổ đạn chỉ khi không hủy).
    /// - shouldCancel() trả true để hủy (ví dụ người chơi đổi súng/bỏ reload).
    /// - onProgress(0..1) tùy chọn: nếu muốn hiển thị tiến độ.
    /// </summary>
    public IEnumerator ReloadRoutine(
        float reloadTime,
        Action<int, int> onDone = null,
        Func<bool> shouldCancel = null,
        Action<float> onProgress = null)
    {
        float dur = Mathf.Max(0.01f, reloadTime);
        float t = 0f;

        while (t < dur)
        {
            // Cho phép UI theo dõi tiến độ
            onProgress?.Invoke(t / dur);

            // Cho phép hủy giữa chừng
            if (shouldCancel != null && shouldCancel())
            {
                // Không đổ đạn nếu hủy
                onDone?.Invoke(mag, reserve);
                yield break;
            }

            t += Time.deltaTime;
            yield return null;
        }

        // Hết thời gian -> thực hiện đổ đạn
        int need = Mathf.Max(0, magSize - mag);
        int take = Mathf.Min(need, reserve);
        if (take > 0)
        {
            mag += take;
            reserve -= take;
            RaiseChanged();
        }

        onProgress?.Invoke(1f);
        onDone?.Invoke(mag, reserve);
    }

    // ====== Private ======
    void RaiseChanged() => Changed?.Invoke(mag, reserve, magSize);
}
