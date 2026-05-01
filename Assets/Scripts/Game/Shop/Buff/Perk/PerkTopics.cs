// PerkTopics.cs
namespace TT
{
    /// <summary>Chuẩn hóa tên topic cho hệ Perk.</summary>
    public static class PerkTopics
    {
        // data: PerkChangedEventData
        public const string Acquired = "perk.acquired";
        public const string Removed = "perk.removed";
        public const string Updated = "perk.updated";

        // data: PerkPurchaseFailedEventData
        public const string PurchaseFailed = "perk.purchase.failed";
    }
}
