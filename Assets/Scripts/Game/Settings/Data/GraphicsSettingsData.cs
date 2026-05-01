using System;

[Serializable]
public class GraphicsSettingsData
{
    public bool vsync = true;

    // Chỉ dùng khi vsync = false
    public int fpsCap = 120;

    public bool runInBackground = false;

    // URP renderScale: 0.1 -> 1.0
    public float renderScale = 1f;
}
