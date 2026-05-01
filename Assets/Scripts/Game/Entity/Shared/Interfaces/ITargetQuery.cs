using UnityEngine;

/// Cổng chọn mục tiêu cho AI.
/// SPImpl: dùng TargetService hiện tại. MPImpl: Host dùng thật; Client có thể trả null/no-op.
public interface ITargetQuery
{
    /// Trả về mục tiêu tốt nhất cho AI, xét từ vị trí 'fromPosition'.
    /// requireAttackable: nếu true, lọc chỉ mục tiêu có thể tấn công ngay.
    ITargetable GetBestTarget(Vector3 fromPosition, bool requireAttackable = true);
}
