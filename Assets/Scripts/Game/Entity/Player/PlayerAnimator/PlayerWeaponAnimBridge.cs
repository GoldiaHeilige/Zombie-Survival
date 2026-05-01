using System.Collections;
using UnityEngine;
#if FUSION_WEAVER
using Fusion;
#endif

/// <summary>
/// Bridge loadout → Animator params (HasWeapon, WeaponType).
/// Chạy cả SP & MP. MP: chỉ StateAuthority mới ghi vào Networked fields.
/// </summary>
[DisallowMultipleComponent]
public class PlayerWeaponAnimBridge : MonoBehaviour
{
    [Header("References")]
    [AutoBindInParent, SerializeField]
    private PlayerStateProvider stateProvider;

    [SerializeField]
    private PlayerNetworkAnimator networkAnimator;

    private ILoadoutState _loadout;
    private bool _bound;

#if FUSION_WEAVER
    private NetworkBehaviour _netLoadout;
#endif

    private void Awake()
    {
        if (!stateProvider)
            stateProvider = GetComponentInParent<PlayerStateProvider>(true);
        if (!networkAnimator)
            networkAnimator = GetComponentInParent<PlayerNetworkAnimator>(true);
    }

    private void OnEnable()
    {
        StartCoroutine(CoBind());
    }

    private void OnDisable()
    {
        Unbind();
    }

    IEnumerator CoBind()
    {
        if (_bound)
            yield break;

        if (!stateProvider)
            yield break;

        _loadout = stateProvider.Loadout;
        if (_loadout == null)
            yield break;

#if FUSION_WEAVER
        _netLoadout = _loadout as NetworkBehaviour;
        if (_netLoadout != null)
        {
            // Chờ network object spawn xong
            yield return new WaitUntil(() =>
                _netLoadout.Object != null &&
                _netLoadout.Object.IsValid &&
                _netLoadout.Runner != null);
        }
#endif

        // Đợi 1–2 frame cho state prime
        yield return null;
        yield return null;

        _loadout.OnSlotChanged += HandleSlotChanged;
        _loadout.OnActiveSlotChanged += HandleActiveSlotChanged;
        _bound = true;

        // Prime lần đầu
        UpdateAnimFromLoadout();
    }

    void Unbind()
    {
        if (_bound && _loadout != null)
        {
            _loadout.OnSlotChanged -= HandleSlotChanged;
            _loadout.OnActiveSlotChanged -= HandleActiveSlotChanged;
        }

        _bound = false;
        _loadout = null;
#if FUSION_WEAVER
        _netLoadout = null;
#endif
    }

    void HandleSlotChanged(int idx)
    {
        UpdateAnimFromLoadout();
    }

    void HandleActiveSlotChanged(int idx)
    {
        UpdateAnimFromLoadout();
    }

    void UpdateAnimFromLoadout()
    {
        if (_loadout == null || networkAnimator == null)
            return;

        int active = _loadout.ActiveSlot;
        int slotCount = _loadout.SlotCount;

        // Mặc định: tay không
        bool hasWeapon = false;
        int weaponTypeInt = 0;

        if (active >= 0 && active < slotCount)
        {
            var slot = _loadout.GetSlot(active);
            if (slot.weaponKey != 0)
            {
                var def = WeaponIdRegistry.GetDef(slot.weaponKey);
                if (def != null)
                {
                    hasWeapon = true;
                    weaponTypeInt = (int)def.animWeaponType; // 0=None,1=Rifle,2=Pistol
                }
            }
        }

        networkAnimator.SetHasWeapon(hasWeapon);
        networkAnimator.SetWeaponType(weaponTypeInt);
    }
}
