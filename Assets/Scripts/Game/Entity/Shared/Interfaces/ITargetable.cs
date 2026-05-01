using UnityEngine;

public interface ITargetable
{
    Transform TargetTransform { get; }
    bool CanBeAttacked { get; }   // cho phép gây damage?
    bool IsAliveLike { get; }     // còn hiện diện để bám theo/bao vây?
}
