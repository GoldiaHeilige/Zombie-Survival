using UnityEngine;

[System.Flags]
public enum InputBlocker
{
    None = 0,
    Movement = 1 << 0,
    CameraLook = 1 << 1,
    Combat = 1 << 2,
    Interaction = 1 << 3,
    SlotSwap = 1 << 4,
    UIOnly = 1 << 5,
    Full = 1 << 6,
}

public static class InputBlockerSystem
{
    public static InputBlocker Active = InputBlocker.None;

    public static void Add(InputBlocker flags)
    {
        Active |= flags;
    }

    public static void Remove(InputBlocker flags)
    {
        Active &= ~flags;
    }

    public static bool Has(InputBlocker flags)
    {
        return (Active & flags) != 0;
    }

    public static void Clear()
    {
        Active = InputBlocker.None;
    }
}
