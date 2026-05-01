using UnityEngine;

public class ArmorPlate : MonoBehaviour
{
    [Tooltip("Nếu true: viên đạn KHÔNG gây damage khi trúng collider này.")]
    public bool blocksDamage = true;

    [Tooltip("Cho phép headshot xuyên giáp nếu hitboxId = Head (tuỳ bạn).")]
    public bool allowHeadshot = true;

    [Tooltip("Surface override để impact ra đúng (Metal).")]
    public SurfaceType surface = SurfaceType.Metal;
}
