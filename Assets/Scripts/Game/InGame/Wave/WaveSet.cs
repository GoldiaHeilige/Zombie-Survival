using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Wave Set", fileName = "WaveSet")]
public class WaveSet : ScriptableObject
{
    [Header("Fixed Playlist (ưu tiên theo thứ tự)")]
    [Tooltip("Danh sách WaveProfile chạy theo thứ tự. Nếu loopFixed = true, sẽ lặp lại vòng.")]
    public List<WaveProfile> fixedWaves = new List<WaveProfile>();

    [Tooltip("Bật để lặp lại fixedWaves vô hạn khi bước vào khối Fixed.")]
    public bool loopFixed = false;

    [Header("Khối xen kẽ Fixed → Procedural (kiểu CoD)")]
    [Tooltip("Số round liên tiếp dùng Fixed trong một chu kỳ.")]
    [Min(0)] public int fixedBlockLength = 4;
    [Tooltip("Số round liên tiếp dùng Procedural trong một chu kỳ.")]
    [Min(0)] public int proceduralBlockLength = 3;

    [Header("Procedural (vô hạn khi hết fixed hoặc theo khối)")]
    [Tooltip("Bật để tự sinh wave khi không dùng Fixed hoặc hết Fixed.")]
    public bool enableProcedural = true;

    [Tooltip("Catalog dùng cho procedural: sẽ lọc theo EnemyDefinition.minRound <= round.")]
    public EnemyCatalog catalog;

    [Header("Budget Cap (anti-infinite rounds)")]
    public bool useBudgetCap = true;

    [Tooltip("Trần ngân sách (budgetPoints) ở Singleplayer/1P. Budget procedural/fixed/special đều bị clamp.")]
    [Min(1)] public int maxBudget = 80;

    [Tooltip("Mỗi người chơi thêm (beyond 1) cộng thêm vào trần maxBudget.")]
    [Min(0)] public int mpMaxBudgetAddPerExtraPlayer = 20;


    [Header("Difficulty Curve (cho Procedural)")]
    [Min(1)] public int baseBudget = 8;
    [Min(1)] public int baseConcurrency = 5;
    [Min(1f)] public float budgetScalePerWave = 1.15f; // nhân dần theo round
    [Min(1)] public int addConcurrencyEvery = 3;       // mỗi N wave tăng +1 concurrency
    [Min(1)] public int maxConcurrency = 25;

    [Header("Multiplayer Scaling (player-count)")]
    [Tooltip("Bật scaling theo số người chơi. Áp dụng khi Host spawn round mới (snapshot theo round).")]
    public bool enableMpScaling = true;

    [Tooltip("Mỗi người chơi thêm (beyond 1) sẽ nhân ngân sách wave (budgetPoints) theo tỉ lệ này. Ví dụ 0.25 => 4P = x1.75.")]
    [Range(0f, 1f)] public float mpBudgetMulPerExtraPlayer = 0.25f;

    [Tooltip("Mỗi người chơi thêm sẽ cộng thêm vào concurrencyCap.")]
    [Min(0)] public int mpCapAddPerExtraPlayer = 1;

    [Tooltip("Mỗi người chơi thêm sẽ cộng thêm vào maxConcurrency (trần cap).")]
    [Min(0)] public int mpMaxCapAddPerExtraPlayer = 2;

    [Tooltip("Có áp scaling cho Special waves không (ví dụ dog round).")]
    public bool mpScaleSpecialWaves = true;

    [Header("Multiplayer Scaling (enemy HP)")]
    public bool enableMpEnemyHpScaling = true;

    [Range(0f, 1f)] public float mpEnemyHpMulPerExtraPlayer = 0.30f;
    [Min(1f)] public float mpEnemyHpPlayerCap = 2.0f;

    [Header("Burst Defaults (cho Procedural)")]
    [Min(1)] public int spawnBurst = 3;
    [Min(0f)] public float interBurstDelay = 2f;

    [Header("Special Waves")]
    [Tooltip("Mỗi N wave sẽ chèn 1 wave đặc biệt (đặt 0 để tắt).")]
    public int everyNSpecial = 5;

    [Tooltip("Danh sách wave đặc biệt sẽ được xoay vòng (nếu có).")]
    public List<WaveProfile> specialWaves = new List<WaveProfile>();

    [Tooltip("Fallback: Nếu không có list, dùng 1 profile đặc biệt duy nhất này.")]
    public WaveProfile specialOneShot;

    [Header("Inter-wave")]
    [Tooltip("Thời gian nghỉ giữa các wave (giây).")]
    [Min(0f)] public float interWaveDelay = 5f;

    /// <summary>
    /// Lấy WaveProfile cố định theo roundIndex (1-based).
    /// - Nếu loopFixed = true, trả về phần tử theo modulo.
    /// - Nếu loopFixed = false, trả về null khi round vượt quá số phần tử có sẵn.
    /// </summary>
    public WaveProfile GetFixedWaveOrNull(int roundIndex1Based)
    {
        if (fixedWaves == null || fixedWaves.Count == 0) return null;

        int idx = roundIndex1Based - 1;
        if (loopFixed)
        {
            if (fixedWaves.Count == 0) return null;
            idx = (roundIndex1Based - 1) % fixedWaves.Count;
            return fixedWaves[idx];
        }
        else
        {
            if (idx >= 0 && idx < fixedWaves.Count) return fixedWaves[idx];
            return null;
        }
    }
}
