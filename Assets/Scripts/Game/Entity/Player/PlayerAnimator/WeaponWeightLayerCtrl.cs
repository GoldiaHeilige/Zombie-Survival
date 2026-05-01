using Fusion;
using UnityEngine;

/// <summary>
/// Điều khiển weight của WeaponLayer dựa trên param HasWeapon trong Animator.
/// - HasWeapon = false  => layerWeight -> 0 (không override upper body)
/// - HasWeapon = true   => layerWeight -> 1 (override bằng Rifle/Pistol pose)
/// Chạy ở cả SP & MP vì HasWeapon đã được sync bởi PlayerNetworkAnimator.
/// </summary>
[DisallowMultipleComponent]
public class WeaponLayerWeightController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;

    [Header("Config")]
    [SerializeField] private string weaponLayerName = "WeaponLayer";
    [SerializeField] private string hasWeaponParam = "HasWeapon";
    [SerializeField] private string downedParam = "Downed";

    [Tooltip("Tốc độ blend weight layer (đơn vị: 1.0 = full weight / giây).")]
    [SerializeField] private float weightLerpSpeed = 10f;

    int _weaponLayerIndex = -1;
    int _hasWeaponHash;
    int _downedHash;

    public void Initialize(Animator newAnimator)
    {
        animator = newAnimator;

        if (animator)
        {
            _weaponLayerIndex = animator.GetLayerIndex(weaponLayerName);
            if (_weaponLayerIndex < 0)
            {
                Debug.LogWarning(
                    $"[WeaponLayerWeightController] Không tìm thấy layer '{weaponLayerName}' trên Animator {animator.name}");
            }
        }

        _hasWeaponHash = Animator.StringToHash(hasWeaponParam);
        _downedHash = string.IsNullOrEmpty(downedParam) ? 0 : Animator.StringToHash(downedParam);  // 🔹 thêm
    }


    void Update()
    {
        if (!animator || _weaponLayerIndex < 0)
            return;

        bool hasWeapon = animator.GetBool(_hasWeaponHash);
        bool isDowned = _downedHash != 0 && animator.GetBool(_downedHash);

        // Nếu Downed -> ép weight = 0, bất kể còn cầm súng hay không
        float target = (!isDowned && hasWeapon) ? 1f : 0f;

        float current = animator.GetLayerWeight(_weaponLayerIndex);
        float newWeight = Mathf.MoveTowards(current, target, weightLerpSpeed * Time.deltaTime);

        if (!Mathf.Approximately(current, newWeight))
            animator.SetLayerWeight(_weaponLayerIndex, newWeight);
    }

}
