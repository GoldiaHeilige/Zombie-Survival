// AudioCategory.cs
namespace TT
{
    /// <summary>Loại âm thanh để mapping sang AudioMixer group và menu settings.</summary>
    public enum AudioCategory
    {
        SFX = 0,
        UI = 1,
        Music = 2,
        Voice = 3,
        Ambient = 4,
        FirstPerson = 5,   // tiếng tay mình (gun FP, reload, bước chân local...)
    }
}
