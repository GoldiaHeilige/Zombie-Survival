using UnityEngine;

public static class BarricadeTopics
{
    /// <summary>
    /// Player bắt đầu repair 1 slot ván.
    /// Payload: BarricadeRepairEvent
    /// </summary>
    public const string RepairStarted = "barricade.repair.started";

    /// <summary>
    /// Zombie đánh trúng barricade (kể cả chưa vỡ ván).
    /// Payload: BarricadeRepairEvent (player có thể null, slotIndex có thể -1)
    /// </summary>
    public const string Hit = "barricade.hit";

    /// <summary>
    /// Một ván bị phá vỡ.
    /// Payload: BarricadeRepairEvent (slotIndex = slot của ván bị vỡ)
    /// </summary>
    public const string BoardBroken = "barricade.board.broken";

    /// <summary>
    /// Một ván build xong / snap vào chỗ.
    /// Payload: BarricadeRepairEvent (slotIndex = slot của ván vừa build xong)
    /// </summary>
    public const string BoardBuilt = "barricade.board.built";
}

public struct BarricadeRepairEvent
{
    public GameObject player;   // có thể null cho các event không cần
    public GameObject window;
    public int slotIndex;
}
