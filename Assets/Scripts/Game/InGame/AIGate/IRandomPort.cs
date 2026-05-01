using UnityEngine;

/// Nguồn random duy nhất (Host). Client không tự random cho logic.
public interface IRandomPort
{
    int RangeInt(int minInclusive, int maxExclusive);
    float RangeFloat(float minInclusive, float maxInclusive);
    Vector2 InsideUnitCircle();

    /// Tuỳ chọn: đặt seed cố định để tái lập (test/recording).
    void SetSeed(int? seed);
}
