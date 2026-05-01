// WeaponObserverTopics.cs
namespace TT
{
    /// <summary>Chuẩn hóa tên topic cho hệ Weapon.</summary>
    public static class WeaponTopics
    {
        public const string Picked = "weapon.picked";    // (ownerGO, def, slotIndex, guid, mag, reserve)
        public const string Replaced = "weapon.replaced";  // (ownerGO, oldDef, newDef, slotIndex, guid)
        public const string Dropped = "weapon.dropped";   // (ownerGO, def, slotIndex, guid, mag, reserve)
        public const string Fired = "weapon.fired";
        public const string ReloadStarted = "weapon.reload.started";
        public const string ReloadFinished = "weapon.reload.finished";
        public const string ReloadCancelled = "weapon.reload.cancelled";
    }
}
