// [ADD] Cổng sync cực mỏng cho HealthSyncFusion dùng
using System;

public interface IHealthSyncPort
{
    int Current { get; }                     // đọc HP hiện tại (int)
    int Max { get; }                         // đọc Max (int)
    event Action<int, int> OnHpChanged;       // (before, after) — host nghe để đẩy NetHP
    void SetCurrentSilent(int value);        // client ghi “silent” từ NetHP về local
    void SetCurrentFromNet(int value);
}
