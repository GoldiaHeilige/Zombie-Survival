using UnityEngine;

public static class PlayerColorPalette
{
    // COD-like: 0 trắng, 1 xanh nhạt, 2 vàng, 3 xanh lá
    public static Color GetFromFusionPlayerId(int fusionPlayerId)
    {
        int idx = Mathf.Clamp(fusionPlayerId - 1, 0, 3);
        return idx switch
        {
            0 => Color.white,
            1 => new Color(0.75f, 0.95f, 1.0f, 1f), // light cyan
            2 => new Color(1.00f, 0.92f, 0.45f, 1f), // yellow
            3 => new Color(0.55f, 1.00f, 0.55f, 1f), // green
            _ => Color.white
        };
    }
}
