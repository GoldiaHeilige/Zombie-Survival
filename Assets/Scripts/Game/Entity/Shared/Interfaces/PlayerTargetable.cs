using UnityEngine;

[DisallowMultipleComponent]
public class PlayerTargetable : MonoBehaviour, ITargetable
{
    [SerializeField] Transform aiTargetPoint;   // drag "AI_TargetPoint" vào đây

    PlayerLifeController _life;

    void Awake()
    {
        _life = GetComponent<PlayerLifeController>();
        if (_life == null)
            Debug.LogWarning("[PlayerTargetable] Missing PlayerLifeController trên cùng GameObject");
    }

    public Transform TargetTransform
    {
        get
        {
            if (aiTargetPoint != null)
                return aiTargetPoint;

            // CHỈ TRẢ VỀ TRANSFORM CỦA CHÍNH PLAYER NÀY
            return transform;
        }
    }

    public bool IsAliveLike =>
        _life ? _life.IsAliveLike : true;

    public bool CanBeAttacked =>
        _life ? _life.CanBeAttacked : true;
}
