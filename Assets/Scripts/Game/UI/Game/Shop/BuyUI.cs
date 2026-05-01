using UnityEngine;
using TMPro;

/// <summary>
/// UI hiện thông báo mua súng / mua đạn (CoD style).
/// Chỉ hoạt động ở local-player.
/// </summary>
public class BuyUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TextMeshProUGUI text;
    [SerializeField] private CanvasGroup canvasGroup;

    private PlayerPickup pickup;
    private PlayerPoints points;
    private ILoadoutState loadout;

    private Transform playerRoot;

    void Awake()
    {
        if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();
        Hide();
    }

    public void Bind(Transform player)
    {
        playerRoot = player;
        if (playerRoot == null) return;

        pickup = playerRoot.GetComponentInChildren<PlayerPickup>(true);
        points = playerRoot.GetComponentInChildren<PlayerPoints>(true);

        var prov = playerRoot.GetComponentInChildren<PlayerStateProvider>(true);
        if (prov != null)
            loadout = prov.Loadout;
    }

    void Update()
    {
        if (pickup == null || points == null)
        {
            Hide();
            return;
        }

        var pap = pickup.GetLookedPackAPunch();
        var box = pickup.GetLookedRandomBox();
        var perk = pickup.GetLookedPerkMachine();
        var shop = pickup.GetLookedShop();

        if (pap == null && box == null && perk == null && shop == null)
        {
            Hide();
            return;
        }

        Show();

        if (pap != null) UpdatePackAPunchText(pap);
        else if (box != null) UpdateRandomBoxText(box);
        else if (perk != null) UpdatePerkText(perk);
        else if (shop != null) UpdateShopText(shop);
    }

    private void UpdatePerkText(TT.PerkMachineSpot perk)
    {
        if (perk == null || perk.perk == null)
        {
            Hide();
            return;
        }

        // ===== Quick Revive: SP -> Out of Order =====
#if FUSION_WEAVER
        var runner = FindFirstObjectByType<Fusion.NetworkRunner>(FindObjectsInactive.Include);
        bool isFusionRunning = runner != null && runner.IsRunning;
#else
    bool isFusionRunning = false;
#endif

        bool isQuickRevive = perk.perk.IsValid && perk.perk.perkId == "perk_quickrevive";
        if (isQuickRevive && !isFusionRunning)
        {
            text.text = $"<b>{perk.DisplayName}</b> <color=#FF5555> is OUT OF ORDER</color>";
            return;
        }

        // ===== Double Tap: MP -> Out of Order =====
        bool isDoubleTap = perk.perk.IsValid && perk.perk.perkId == "perk_doubletap";
        if (isDoubleTap && isFusionRunning)
        {
            text.text = $"<b>{perk.DisplayName}</b> <color=#FF5555> is OUT OF ORDER</color>";
            return;
        }

        // nếu muốn “Already owned” text thì đọc PerkManager ở player:
        bool has = false;

#if FUSION_WEAVER
        var net = playerRoot ? playerRoot.GetComponentInChildren<TT.PerkNetState>(true) : null;
        if (isFusionRunning && net != null && perk.perk.IsValid &&
            TT.PerkNetState.TryMapPerkString(perk.perk.perkId, out var pid))
        {
            has = net.Has(pid);
        }
        else
#endif
        {
            var perkMgr = playerRoot ? playerRoot.GetComponentInChildren<TT.PerkManager>(true) : null;
            has = perkMgr != null && perk.perk.IsValid && perkMgr.HasPerk(perk.perk.perkId);
        }


        int cost = perk.Cost;

        if (has)
        {
            text.text = $"<b>{perk.DisplayName}</b> <color=#66FF66>OWNED</color>";
            return;
        }

        text.text = $"Press <b>[F]</b> to buy <b>{perk.DisplayName}</b> with <color=#00FFFF>{cost}</color> points";

    }


    private void UpdateShopText(WeaponShopSpot shop)
    {
        if (shop == null || shop.weaponDef == null)
        {
            Hide();
            return;
        }

        string wName = shop.DisplayName;

        int key = WeaponIdRegistry.GetKey(shop.weaponDef.weaponId);
        bool alreadyHave = false;

        if (key != 0 && loadout != null)
        {
            int slotCount = loadout.SlotCount;
            for (int i = 0; i < slotCount; i++)
            {
                var s = loadout.GetSlot(i);
                if (s.weaponKey == key)
                {
                    alreadyHave = true;
                    break;
                }
            }
        }

        if (alreadyHave)
        {
            text.text = $"Press <b>[F]</b> to buy ammo for <b>{wName}</b> with <color=#00FFFF>{shop.ammoCost}</color> points";
        }
        else
        {
            text.text = $"Press <b>[F]</b> to buy <b>{wName}</b> with <color=#00FFFF>{shop.weaponCost}</color> points";
        }
    }


    private void UpdateRandomBoxText(RandomWeaponBoxSpot box)
    {
        if (box == null)
        {
            Hide();
            return;
        }

        text.text = $"Press <b>[F]</b> to buy <b>random weapon</b> with <color=#00FFFF>{box.cost}</color> points";
    }

    private void UpdatePackAPunchText(PackAPunchSpot pap)
    {
        if (pap == null || loadout == null)
        {
            Hide();
            return;
        }

        int activeIndex = loadout.ActiveSlot;
        if (activeIndex < 0 || activeIndex >= loadout.SlotCount)
        {
            text.text = "Cannot upgrade: no active weapon";
            return;
        }

        var slot = loadout.GetSlot(activeIndex);
        if (slot.weaponKey == 0)
        {
            text.text = "Cannot upgrade: no active weapon";
            return;
        }

        var baseDef = WeaponIdRegistry.GetDef(slot.weaponKey);
        if (!baseDef)
        {
            text.text = "Cannot upgrade: weapon data missing";
            return;
        }

        if (!baseDef.upgradedVersion)
        {
            text.text = $"<b>{baseDef.weaponName}</b> cannot be upgraded";
            return;
        }

        string wName = baseDef.weaponName;
        int cost = pap.upgradeCost;

        text.text = $"Press <b>[F]</b> to upgrade <b>{wName}</b> with <color=#00FFFF>{cost}</color> points";
    }


    private void Show()
    {
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    private void Hide()
    {
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }
}
