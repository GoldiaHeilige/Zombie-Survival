using System;

[Serializable]
public class GameSettingsData
{
    public AudioSettingsData audio = new AudioSettingsData();
    public GraphicsSettingsData graphics = new GraphicsSettingsData();
}
