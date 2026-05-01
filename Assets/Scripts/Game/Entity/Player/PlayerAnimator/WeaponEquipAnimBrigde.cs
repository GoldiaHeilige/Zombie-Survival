using System.Collections;
using UnityEngine;

/// <summary>
/// Bridge đổi ActiveSlot -> bool IsEquipping trên Animator (world model).
/// Đồng thời block Combat + SlotSwap cho local player trong thời gian equip.
/// Không đụng gì tới reload (reload chỉ dùng IsReloading).
/// </summary>
[DisallowMultipleComponent]
public class WeaponEquipAnimBridge : MonoBehaviour
{
    [Header("References")]
    [AutoBindInParent, SerializeField]
    private PlayerStateProvider stateProvider;

    [Tooltip("Animator của world model. Nếu để trống sẽ tự tìm trong con.")]
    [SerializeField] private Animator animator;

#if FUSION_WEAVER
    [Header("Multiplayer (local check)")]
    [AutoBindInParent, SerializeField]
    private FusionNetBridge netBridge;
#endif

    [Header("Animator Param")]
    [SerializeField] private string isEquippingParam = "IsEquipping";

    [Tooltip("Thời gian coi như đang rút súng (giây). Nên khớp với độ dài clip equip.")]
    [SerializeField] private float equipDuration = 0.25f;

    private ILoadoutState _loadout;
    private bool _bound;
    private int _hashIsEquipping;
    private int _lastSlotIndex = int.MinValue;
    private bool _initializedSlot = false;

    private Coroutine _equipRoutine;

    void Awake()
    {
        if (!animator)
            animator = GetComponentInChildren<Animator>(true);
        if (!stateProvider)
            stateProvider = GetComponentInParent<PlayerStateProvider>(true);

        if (!string.IsNullOrEmpty(isEquippingParam))
            _hashIsEquipping = Animator.StringToHash(isEquippingParam);
    }

    public void SetAnimator(Animator newAnimator)
    {
        animator = newAnimator;
    }

    void OnEnable()
    {
        StartCoroutine(BindRoutine());
    }

    void OnDisable()
    {
        Unbind();
        StopEquip();
    }

    IEnumerator BindRoutine()
    {
        if (_bound)
            yield break;

        if (!stateProvider)
            yield break;

        // Đợi đến khi PlayerStateProvider pick được loadout (SP hoặc MP)
        while (_loadout == null)
        {
            _loadout = stateProvider.Loadout;
            if (_loadout != null)
                break;

            yield return null;
        }

        _loadout.OnActiveSlotChanged += HandleActiveSlotChanged;
        _bound = true;
    }

    void Unbind()
    {
        if (_bound && _loadout != null)
            _loadout.OnActiveSlotChanged -= HandleActiveSlotChanged;

        _bound = false;
        _loadout = null;
    }

    void HandleActiveSlotChanged(int newIndex)
    {
        if (_loadout == null || animator == null || _hashIsEquipping == 0)
            return;

        // Slot không hợp lệ -> stop equip & reset
        if (newIndex < 0 || newIndex >= _loadout.SlotCount)
        {
            StopEquip();
            _initializedSlot = false;
            _lastSlotIndex = int.MinValue;
            return;
        }

        // 🔹 Lần đầu tiên nhận event -> chỉ lưu lại, không equip
        if (!_initializedSlot)
        {
            _initializedSlot = true;
            _lastSlotIndex = newIndex;
            return;
        }

        // 🔹 Nếu slot không đổi -> không equip lại (fix reload xong + bấm lại cùng slot)
        if (newIndex == _lastSlotIndex)
            return;

        _lastSlotIndex = newIndex;

        var slot = _loadout.GetSlot(newIndex);
        if (slot.weaponKey == 0)
        {
            StopEquip();
            return;
        }

        // Nếu đang reload thì coi như không phải equip (reload anim lo phần này)
        if (_loadout.IsReloading)
            return;

        StartEquip();
    }

    // ===== EQUIP LOGIC =====

    bool ShouldAffectLocalInput()
    {
#if FUSION_WEAVER
        // Trong MP: chỉ block input trên máy đang sở hữu player này
        if (netBridge && !netBridge.IsLocalOwner)
            return false;
#endif
        // SP: luôn luôn local
        return true;
    }

    void StartEquip()
    {
        if (_equipRoutine != null)
            StopCoroutine(_equipRoutine);

        _equipRoutine = StartCoroutine(CoEquip());
    }

    IEnumerator CoEquip()
    {
        animator.SetBool(_hashIsEquipping, true);

        // Block combat + đổi slot cho local player trong thời gian equip
        if (ShouldAffectLocalInput())
        {
            InputBlockerSystem.Add(InputBlocker.Combat);
        }

        float t = 0f;
        while (t < equipDuration)
        {
            t += Time.deltaTime;
            yield return null;
        }

        animator.SetBool(_hashIsEquipping, false);

        if (ShouldAffectLocalInput())
        {
            InputBlockerSystem.Remove(InputBlocker.Combat);
        }

        _equipRoutine = null;
    }

    void StopEquip()
    {
        if (animator != null && _hashIsEquipping != 0)
            animator.SetBool(_hashIsEquipping, false);

        if (_equipRoutine != null)
        {
            if (ShouldAffectLocalInput())
            {
                InputBlockerSystem.Remove(InputBlocker.Combat);
            }

            StopCoroutine(_equipRoutine);
            _equipRoutine = null;
        }
    }
}
