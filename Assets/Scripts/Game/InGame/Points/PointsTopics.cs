// PointsTopics.cs
namespace TT
{
    /// <summary>Chuẩn hóa tên topic cho hệ Points.</summary>
    public static class PointsTopics
    {
        /// <summary>Bắn khi điểm của 1 player thay đổi (HUD nghe cái này là đủ).</summary>
        public const string Changed = "points.changed";

        /// <summary>Bắn riêng khi được cộng điểm (hit, kill, bonus…).</summary>
        public const string Gained = "points.gained";

        /// <summary>Bắn riêng khi bị trừ điểm (mua đồ, mở cửa…).</summary>
        public const string Spent = "points.spent";
    }
}
