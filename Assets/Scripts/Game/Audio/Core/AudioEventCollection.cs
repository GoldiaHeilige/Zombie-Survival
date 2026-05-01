using TT;
using UnityEngine;

[CreateAssetMenu(menuName = "Audio/Audio Event Collection")]
public class AudioEventCollection : ScriptableObject
{
    [Header("Audio Events")]
    public AudioEventSO[] events;

    [Header("Nested Collections")]
    public AudioEventCollection[] nestedCollections;
}
