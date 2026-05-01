using System.Collections;
using UnityEngine;
using Fusion;
using TT;

public class PlayerPickup : MonoBehaviour
{
    [Header("References")]
    [SerializeField] PlayerInventory inventory;
    [SerializeField] PlayerWeaponBridge bridge;
    [SerializeField] Camera playerCamera;

    [AutoBindInParent] private FusionNetBridge _net;
    private PlayerRefs _refs;
    private PlayerStateProvider _stateProv;
    private ILoadoutState _loadout;

    [Header("Pickup Settings")]
    [SerializeField] float pickupRange = 2.5f;
    [SerializeField] float replaceConfirmWindow = 1.5f;
    [SerializeField] bool destroyEvenIfNoGain = true;
    [SerializeField] LayerMask pickupMask = ~0;
    [SerializeField] QueryTriggerInteraction triggerQuery = QueryTriggerInteraction.UseGlobal;

    private bool _buyWasDown;

    // ==== AUDIO ====
    [Header("Audio")]
    [SerializeField] private AudioEventSO buySfx2D;
    [SerializeField] private AudioEventSO barricadeBuySfx2D;

    // Fallback server-cast “rộng” hơn
    const float SERVER_SPHERE_RADIUS = 0.35f;
    const float SERVER_DOWN_TILT_DEG = 8f;

    WorldWeapon _pendingReplaceWW;
    float _pendingReplaceUntil = -1f;

    string lastDroppedGuid;
    float lastDropTime;
    private float _dropCooldownUntil = 0f;

    private bool _dropInProgress;
    public bool IsDroppingNow => _dropInProgress;


    public bool HasPendingReplace => _pendingReplaceWW && Time.time <= _pendingReplaceUntil;
    public string PendingReplaceNewName => _pendingReplaceWW ? _pendingReplaceWW.weaponDef.weaponName : null;
    public string PendingReplaceOldName
    {
        get
        {
            var wc = bridge?.GetCurrentWeapon();
            return wc != null && wc.def != null ? wc.def.weaponName : null;
        }
    }

    private void Awake()
    {
        _refs = GetComponentInParent<PlayerRefs>();
        if (_net == null) _net = GetComponentInParent<FusionNetBridge>(true);
        StartCoroutine(WaitForCam());

        IEnumerator WaitForCam()
        {
            if (_refs != null)
                yield return new WaitUntil(() => _refs.cameraReady);
            playerCamera = _refs ? _refs.mainCam : playerCamera ?? Camera.main;
        }

        _stateProv = GetComponentInParent<PlayerStateProvider>(true);
        _loadout = _stateProv ? _stateProv.Loadout : null;
    }

    void Update()
    {
        // Auto prompt replace khi nhìn thấy WW và inventory full
        var ww = PeekLookedWeaponLocalOnly();
        if (ww && inventory != null && bridge != null)
        {
            bool isFull = true;
            foreach (var s in inventory.slots) { if (s == null) { isFull = false; break; } }

            if (isFull)
            {
                var def = ww.weaponDef;
                if (def != null)
                {
                    int sameIdSlot = bridge.FindSlotByWeaponId(def.weaponId);
                    if (sameIdSlot < 0 && _pendingReplaceWW != ww)
                    {
                        _pendingReplaceWW = ww;
                        _pendingReplaceUntil = float.PositiveInfinity;
                    }
                }
            }
            else
            {
                if (_pendingReplaceWW && _pendingReplaceUntil == float.PositiveInfinity)
                    _pendingReplaceWW = null;
            }
        }
        else
        {
            if (_pendingReplaceWW && _pendingReplaceUntil == float.PositiveInfinity)
                _pendingReplaceWW = null;
        }

        // Clear pending theo timer
        if (_pendingReplaceWW && _pendingReplaceUntil != float.PositiveInfinity &&
            Time.time > _pendingReplaceUntil)
        {
            _pendingReplaceWW = null;
        }

        // ===== THÊM: Xử lý input Buy cho barricade =====
        HandleBuyInputForBarricade();
        HandleBuyInputForPerk();
    }

    private void HandleBuyInputForBarricade()
    {
        // Không có HUD hoặc không có cửa đang active → khỏi xử lý
        if (BarricadeRepairUI.Instance == null) return;
        var window = BarricadeRepairUI.Instance.GetCurrentWindow();
        if (window == null) return;

#if FUSION_WEAVER
        // Nếu đang có NetworkRunner chạy → đang ở chế độ Fusion (MP / SP offline),
        // lúc này Buy đã được xử lý trong FusionNetBridge.FixedUpdateNetwork().
        var runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
        if (runner != null && runner.IsRunning)
            return;
#endif

        // ---- Chỉ còn lại SINGLEPLAYER THUẦN (không Fusion Runner) ----
        if (InputHub.Instance == null) return;

        bool buyPressed = InputHub.Instance.Current.BuyDown;
        if (buyPressed)
        {
            TryInteractBarricade();
        }
    }

    private void HandleBuyInputForPerk()
    {
#if FUSION_WEAVER
        var runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
        if (runner != null && runner.IsRunning)
            return;
#endif

        if (InputHub.Instance == null) return;

        bool buyDown = InputHub.Instance.Current.BuyDown;

        // edge detect (chỉ true đúng 1 frame lúc vừa bấm)
        bool buyEdge = buyDown && !_buyWasDown;
        _buyWasDown = buyDown;

        if (!buyEdge)
            return;

        TryInteractPerkMachine();
    }



    public bool CanPickupNow(WorldWeapon ww)
    {
        if (!ww) return false;
        if (!ww.IsPickupAvailable) return false;            // đã có sẵn trong WW
                                                            // chặn nhặt lại chính khẩu vừa thả trong 0.25–0.35s
        if (!string.IsNullOrEmpty(lastDroppedGuid) &&
            ww.runtimeGuid == lastDroppedGuid &&
            Time.time < _dropCooldownUntil)
        {
            return false;
        }
        return true;
    }


    // ===== Entry points =====
    public void TryPickupLooked()
    {
#if FUSION_WEAVER
        if (_net && _net.Object && _net.Object.HasInputAuthority && !_net.HasStateAuthority)
        {
            // Client → chọn WW bằng camera thật → RPC lên host
            var ww = PeekLookedWeaponLocalOnly();
            if (!ww) return;
            var no = ww.GetComponent<NetworkObject>();
            if (!no) return;
            _net.RPC_RequestPickup(no);
            return;
        }
#endif
        // Host / Single → xử lý trực tiếp qua state
        if (_loadout == null)
        {
            _stateProv = GetComponentInParent<PlayerStateProvider>(true);
            _loadout = _stateProv ? _stateProv.Loadout : null;
        }
        var serverWW = PeekLookedWeaponLocalOnly();
        if (!serverWW || _loadout == null) return;
        if (!CanPickupNow(serverWW)) return;


        if (_loadout.TryPickup(serverWW))
        {
            // GÁN GUID WW cho slot active (host/SP)
            int slotIdx = _loadout.ActiveSlot;
            bridge?.SetRuntimeGuid(slotIdx, serverWW.runtimeGuid);

            serverWW.OnPickedUp();
            //  Debug.Log($"[Pickup-SP] def={serverWW.weaponDef.name} guid={serverWW.runtimeGuid} mag={serverWW.magOnGround} reserve={serverWW.reserveOnGround}");
        }

        else
        {
            _pendingReplaceWW = serverWW;
            _pendingReplaceUntil = Time.time + replaceConfirmWindow;
        }

    }

    public void ConfirmReplaceIfPending()
    {
        if (!_pendingReplaceWW) return;

#if FUSION_WEAVER
        if (_net && _net.Object && _net.Object.HasInputAuthority && !_net.HasStateAuthority)
        {
            var no = _pendingReplaceWW.GetComponent<NetworkObject>();
            if (no) _net.RPC_RequestReplace(no);
            return;
        }
#endif

        if (_stateProv == null) _stateProv = GetComponentInParent<PlayerStateProvider>(true);
        if (_loadout == null && _stateProv != null) _loadout = _stateProv.Loadout;
        if (_loadout == null) return;

        // 1) Drop active
        ServerSpawnDropActive();

        // 2) Pickup mới đúng 1 lần
        if (_loadout.TryPickup(_pendingReplaceWW))
        {
            // gán GUID cho slot active
            bridge?.SetRuntimeGuid(_loadout.ActiveSlot, _pendingReplaceWW.runtimeGuid);
            _pendingReplaceWW.OnPickedUp();
        }

        _pendingReplaceWW = null;
        _pendingReplaceUntil = -1f;
    }



    public void DropActiveWeaponPublic()
    {
        Debug.Log($"[DropActiveWeaponPublic] Start - HasStateAuth: {(_net?.HasStateAuth ?? false)}, IsLocalOwner: {(_net?.IsLocalOwner ?? false)}");

#if FUSION_WEAVER
        // Client trong multiplayer gửi RPC đến server
        if (_net != null && _net.Runner != null && _net.IsLocalOwner && !_net.HasStateAuth)
        {
            Debug.Log("[DropActiveWeaponPublic] CLIENT - Sending RPC");

            // TÌM PlayerLoadoutStateMP để gửi RPC
            var mpState = GetComponentInParent<PlayerLoadoutStateMP>(true);
            if (mpState != null)
            {
                Debug.Log("[DropActiveWeaponPublic] Sending RPC_RequestDropActive");
                mpState.RPC_RequestDropActive();
            }
            else
            {
                Debug.LogError("[DropActiveWeaponPublic] Cannot find PlayerLoadoutStateMP for RPC!");
            }
            return;
        }
#endif

        // ✅ host/SP: thực thi trực tiếp
        Debug.Log("[DropActiveWeaponPublic] HOST/SP - Executing directly");
        ServerSpawnDropActive();
    }

    // Server/Host: dùng bridge hiện tại để spawn world-weapon, rồi clear qua state
    // PlayerPickup.cs
    public void ServerSpawnDropActive()
    {
        Debug.Log($"[ServerSpawnDropActive] Start - HasStateAuth: {(_net?.HasStateAuth ?? false)}");
        if (_net != null && _net.Runner != null && !_net.HasStateAuth)
        {
            Debug.Log("[ServerSpawnDropActive] SKIPPED - Not State Authority");
            return;
        }

        if (_dropInProgress)
        {
            Debug.Log("[ServerSpawnDropActive] SKIPPED - Drop in progress");
            return;
        }

        _dropInProgress = true;
        try
        {
            if (_stateProv == null) _stateProv = GetComponentInParent<PlayerStateProvider>(true);
            if (_loadout == null && _stateProv != null) _loadout = _stateProv.Loadout;
            if (_loadout == null || bridge == null || inventory == null)
            {
                Debug.LogWarning($"[Drop] abort: loadout={_loadout != null}, bridge={bridge != null}, inv={inventory != null}");
                return;
            }

            // Ưu tiên dữ liệu từ view local (nếu có)
            var wc = bridge.GetCurrentWeapon();
            int activeSlot = bridge.GetActiveSlotIndex();
            var rt = bridge.GetRuntime(activeSlot);
            WeaponDef def = wc ? wc.def : null;

            // 🔁 Fallback qua STATE MP (cho player remote trên host)
            if (def == null || rt == null)
            {
                // ActiveSlot “sự thật” từ state
                activeSlot = (_loadout is PlayerLoadoutStateMP mp) ? mp.ActiveSlot : activeSlot;
                var slot = _loadout.GetSlot(activeSlot);
                if (slot.weaponKey == 0) { Debug.Log("[Drop] state says empty – abort"); return; }
                def = WeaponIdRegistry.GetDef(slot.weaponKey);
                if (def == null) { Debug.LogWarning("[Drop] Cannot resolve WeaponDef from state"); return; }
                rt = new AmmoModule { mag = slot.mag, reserve = slot.reserve, magSize = def.magSize };
                Debug.Log($"[Drop] Fallback state -> slot={activeSlot} def={def.weaponName} mag={rt.mag} reserve={rt.reserve}");
            }

            // GUID: lấy cache nếu có, nếu rỗng thì tạo mới
            string guidNow = bridge.GetRuntimeGuid(activeSlot);
            if (string.IsNullOrEmpty(guidNow))
            {
                guidNow = System.Guid.NewGuid().ToString();

#if FUSION_WEAVER
                // ✅ Chỉ sync GUID bằng RPC nếu đang chạy Fusion thật sự
                var net = GetComponentInParent<FusionNetBridge>(true);
                if (net != null && net.Runner != null && net.Object != null)
                {
                    net.RPC_SetSlotRuntimeGuid(activeSlot, guidNow);
                }
#endif
            }

            lastDroppedGuid = guidNow;
            lastDropTime = Time.time;
            _dropCooldownUntil = Time.time + 0.30f;

            // Spawn WW trên host
            var dropped = SpawnDropped(def, rt.mag, rt.reserve, guidNow);
            Debug.Log($"[Drop] Host spawn {def.weaponName} slot={activeSlot} guid={guidNow}");

            // Clear qua STATE (sự thật)
            _loadout.TryDropActive();

            // Clear cache local & view
            bridge.ClearRuntimeForSlot(activeSlot);
            if (inventory) inventory.ClearSlot(activeSlot);
            var vc = bridge.GetCurrentWeapon();
            if (vc != null && (_net == null || _net.IsLocalOwner))
                vc.EquipFromDef(null);

            Debug.Log($"[Drop] Done — slot {activeSlot} cleared locally & by state.");
        }
        finally { _dropInProgress = false; }
    }



    // ===== Ray helpers =====
    // Dùng camera cục bộ: phục vụ client chọn mục tiêu để gửi RPC và để UI/prompt
    WorldWeapon PeekLookedWeaponLocalOnly()
    {
        if (!playerCamera) return null;
        if (Physics.Raycast(playerCamera.transform.position,
                            playerCamera.transform.forward,
                            out var hit, pickupRange, pickupMask, triggerQuery))
        {
            return hit.collider.GetComponentInParent<WorldWeapon>();
        }
        return null;
    }

    public WorldWeapon GetLookedWeapon()
    {
#if FUSION_WEAVER
        // Trong MP: chỉ cho phép máy có InputAuthority dùng ray UI để hiển thị prompt
        if (_net && _net.Object && !_net.Object.HasInputAuthority)
            return null;
#endif
        return PeekLookedWeaponLocalOnly();
    }

    // ===== Core gameplay =====
    public void Pickup(WorldWeapon ww)
    {
        Debug.Log($"[Pickup] SA={_net?.HasStateAuthority} bridge={(bridge != null)} inv={(inventory != null)} ww={ww?.name} def={ww?.weaponDef?.weaponName}");

        if (!ww || !bridge) return;
        var pickedDef = ww.weaponDef;
        if (!pickedDef) return;

        // 1) Merge reserve nếu trùng loại
        int sameIdSlot = bridge.FindSlotByWeaponId(pickedDef.weaponId);
        if (sameIdSlot >= 0)
        {
            bool absorbed = TryMergeReserveIntoSlot(sameIdSlot, ww, out int gained);
            if (absorbed || destroyEvenIfNoGain) ww.OnPickedUp();
            return;
        }

        // 2) Slot trống?
        int freeSlot = -1;
        var slotsArr = inventory.slots;
        if (slotsArr != null)
        {
            for (int i = 0; i < slotsArr.Length; i++) if (slotsArr[i] == null) { freeSlot = i; break; }
        }

        if (freeSlot >= 0)
        {
            var runtime = new AmmoModule
            {
                mag = Mathf.Clamp(ww.magOnGround, 0, pickedDef.magSize),
                reserve = Mathf.Clamp(ww.reserveOnGround, 0, pickedDef.maxReserve),
                magSize = pickedDef.magSize
            };

            bridge.BeginInventoryTransaction();
            try
            {
                inventory.SetSlot(freeSlot, pickedDef);
                bridge.EquipIntoSlot(freeSlot, pickedDef, runtime, ww.runtimeGuid);
            }
            finally { bridge.EndInventoryTransaction(); }

            ww.OnPickedUp();
            return;
        }

        // 3) Full → bật pending replace
        _pendingReplaceWW = ww;
        _pendingReplaceUntil = Time.time + replaceConfirmWindow;
    }

    bool TryMergeReserveIntoSlot(int slotIndex, WorldWeapon ww, out int gainedReserve)
    {
        gainedReserve = 0;
        var def = bridge.GetSlotDef(slotIndex);
        var ammo = bridge.GetRuntime(slotIndex);
        if (def == null || ammo == null || ww == null) return false;

        int maxReserve = Mathf.Max(def.maxReserve, def.startReserve);
        int available = Mathf.Max(0, ww.reserveOnGround);
        int canTake = Mathf.Min(maxReserve - ammo.reserve, available);
        if (canTake <= 0) return false;

        ammo.reserve += canTake;
        ww.reserveOnGround = Mathf.Max(0, ww.reserveOnGround - canTake);
        gainedReserve = canTake;
        bridge.NotifySlotAmmoChanged(slotIndex);
        return true;
    }

    // === Drop spawn (giữ nguyên logic Runner.Spawn nếu có) ===
    WorldWeapon SpawnDropped(WeaponDef def, int mag, int reserve, string runtimeGuid)
    {
        if (!def || !def.worldPrefab)
        {
            Debug.LogWarning($"Drop failed: {(def ? def.name : "NULL")} missing worldPrefab!");
            return null;
        }

        Transform root = transform;
        Vector3 feet = root.position;
        var cc = GetComponent<CharacterController>();
        if (cc != null) feet = cc.bounds.center - new Vector3(0, cc.height * 0.5f, 0);

        Vector3 dropPos = feet + Vector3.up * 0.1f;
        Vector3 forwardOnGround = Vector3.ProjectOnPlane((playerCamera ? playerCamera.transform.forward : transform.forward), Vector3.up);
        if (forwardOnGround.sqrMagnitude < 0.0001f) forwardOnGround = root.forward;

        if (Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore))
        {
            dropPos = hit.point + hit.normal * 0.5f;
            forwardOnGround = Vector3.ProjectOnPlane(forwardOnGround, hit.normal);
        }

        Quaternion rot = Quaternion.LookRotation(forwardOnGround.normalized, Vector3.up);

#if FUSION_WEAVER
        var netObjPrefab = def?.worldPrefab ? def.worldPrefab.GetComponent<NetworkObject>() : null;
        if (_net != null && _net.Runner != null && netObjPrefab != null)
        {
            var no = _net.Runner.Spawn(
              netObjPrefab, dropPos, rot, inputAuthority: null,
              onBeforeSpawned: (runner, obj) => {
                  var ww = obj.GetComponent<WorldWeapon>();
                  ww.InitFromDrop(string.IsNullOrEmpty(runtimeGuid) ? System.Guid.NewGuid().ToString() : runtimeGuid,
                        def, mag, reserve);
                  ww.BlockPickupFor(0.2f);
              });
            return no ? no.GetComponent<WorldWeapon>() : null;
        }
#endif

        var go = Instantiate(def.worldPrefab, dropPos, rot);
        var ww = go.GetComponent<WorldWeapon>();
        ww.InitFromDrop(string.IsNullOrEmpty(runtimeGuid) ? System.Guid.NewGuid().ToString() : runtimeGuid,
                        def, mag, reserve);
        ww.BlockPickupFor(0.2f);

        return ww;
    }

    // Helper cho UI/debug
    public bool IsInventoryFull()
    {
        if (inventory?.slots == null) return false;
        for (int i = 0; i < inventory.slots.Length; i++)
            if (inventory.slots[i] == null) return false;
        return true;
    }

    public int FindSameWeaponSlot(WeaponDef def)
    {
        if (def == null) return -1;
        return bridge?.FindSlotByWeaponId(def.weaponId) ?? -1;
    }

    // ==== SHOP HELPER ====

    // ==== PERK MACHINE HELPER ====

    public PerkMachineSpot PeekLookedPerkMachineLocalOnly(float maxDistanceOverride = -1f)
    {
        if (!playerCamera) return null;

        float dist = maxDistanceOverride > 0f ? maxDistanceOverride : pickupRange;

        if (Physics.Raycast(playerCamera.transform.position,
                            playerCamera.transform.forward,
                            out var hit, dist, pickupMask, triggerQuery))
        {
            return hit.collider.GetComponentInParent<TT.PerkMachineSpot>();
        }
        return null;
    }

    public TT.PerkMachineSpot GetLookedPerkMachine()
    {
#if FUSION_WEAVER
        if (_net && _net.Object && !_net.Object.HasInputAuthority)
            return null;
#endif
        var perk = PeekLookedPerkMachineLocalOnly();
        if (perk != null && perk.CanUse())
            return perk;
        return null;
    }


    // ==== AUDIO HELPERS ====
    private void PlayLocalBuySfx()
    {
        if (buySfx2D == null)
            return;

        // 2D local-only
        TT.AudioEvents.PlayUI(buySfx2D.eventId);
    }

    private void PlayLocalBarricadeBuySfx()
    {
        // fallback: nếu chưa gán riêng thì dùng chung để khỏi câm tiếng
        var sfx = barricadeBuySfx2D != null ? barricadeBuySfx2D : buySfx2D;
        if (sfx == null) return;

        TT.AudioEvents.PlayUI(sfx.eventId);
    }

    public void PlayLocalBuySfx_Authoritative()
    {
        PlayLocalBuySfx();
    }

    // NEW: gọi từ RPC riêng (barricade)
    public void PlayLocalBarricadeBuySfx_Authoritative()
    {
        PlayLocalBarricadeBuySfx();
    }

    /// <summary>Raycast local để tìm WeaponShopSpot đang nhìn.</summary>
    public WeaponShopSpot PeekLookedShopLocalOnly(float maxDistanceOverride = -1f)
    {
        if (!playerCamera) return null;

        float dist = maxDistanceOverride > 0f ? maxDistanceOverride : pickupRange;

        if (Physics.Raycast(playerCamera.transform.position,
                            playerCamera.transform.forward,
                            out var hit, dist, pickupMask, triggerQuery))
        {
            return hit.collider.GetComponentInParent<WeaponShopSpot>();
        }

        return null;
    }

    /// <summary>Chỉ trả về shop nếu đúng local owner (MP) và shop còn dùng được.</summary>
    public WeaponShopSpot GetLookedShop()
    {
#if FUSION_WEAVER
        // Trong MP: chỉ cho máy có InputAuthority dùng để quyết định UI và gửi RPC
        if (_net && _net.Object && !_net.Object.HasInputAuthority)
            return null;
#endif
        var shop = PeekLookedShopLocalOnly();
        if (shop && shop.weaponDef && shop.CanUse())
            return shop;
        return null;
    }

    /// <summary>
    /// Thử tương tác với shop (mua súng hoặc mua đạn).
    /// Trả về true nếu đã xử lý shop (đã mua hoặc không đủ tiền nhưng vẫn "tiêu" interact),
    /// false nếu không có shop → để code ngoài biết chuyển sang logic nhặt súng cũ.
    /// </summary>
    public bool TryInteractShop()
    {
        var shop = GetLookedShop();
        if (!shop) return false;

        // Check khoảng cách local (cho tương tác mượt)
        float dist = Vector3.Distance(transform.position, shop.transform.position);
        if (dist > shop.interactRange + 0.5f)
            return false;

        // Lấy loadout + points
        if (_stateProv == null) _stateProv = GetComponentInParent<PlayerStateProvider>(true);
        if (_loadout == null && _stateProv != null) _loadout = _stateProv.Loadout;
        if (_loadout == null) return false;

        var points = GetComponentInParent<PlayerPoints>();
        if (!points) return false;

#if FUSION_WEAVER
        // CLIENT MP → gửi RPC lên host
        if (_net && _net.Object && _net.Object.HasInputAuthority && !_net.HasStateAuthority)
        {
            var mp = _loadout as PlayerLoadoutStateMP;
            var shopNo = shop.GetComponent<NetworkObject>();
            if (mp != null && shopNo != null)
            {
                Debug.Log("[PlayerPickup] CLIENT requesting buy from shop via RPC.");
                mp.RPC_RequestBuyFromShop(shopNo);

                return true; // consume interact
            }

            return false;
        }
#endif

        // HOST / SINGLEPLAYER: gọi trực tiếp
#if FUSION_WEAVER
        if (_loadout is PlayerLoadoutStateMP mpHost)
        {
            Debug.Log("[PlayerPickup] HOST buying from shop directly (MP).");
            bool ok = mpHost.TryBuyFromShop(shop, points, this, gameObject);

            if (ok)
            {
                if (_net != null && _net.Runner != null && _net.HasStateAuth)
                {
                    string wName = shop.weaponDef ? shop.weaponDef.weaponName : shop.DisplayName;
                    _net.RPC_AnnounceWallBuy(wName);
                }

                // 🔊 Host local (chỉ người host nghe, không broadcast)
                PlayLocalBuySfx();
            }

            return ok;
        }
#endif

        if (_loadout is PlayerLoadoutStateSP sp)
        {
            Debug.Log("[PlayerPickup] SP buying from shop directly.");
            bool ok = sp.TryBuyFromShop(shop, points, this, gameObject);

            if (ok)
            {
                string wName = shop.weaponDef ? shop.weaponDef.weaponName : shop.DisplayName;
                EventFeed.Push($"You bought {wName}", EventFeedType.Action);

                PlayLocalBuySfx();
            }

            return ok;
        }

        return false;
    }

    /// <summary>Raycast local để tìm RandomWeaponBoxSpot đang nhìn.</summary>
    /// 
    /// <summary>
    /// Thử tương tác với random box.
    /// Trả về true nếu đã xử lý (đã gửi RPC/đã roll).
    /// </summary>
    public bool TryInteractRandomBox()
    {
        var box = GetLookedRandomBox();
        if (!box) return false;

        // Check khoảng cách local
        float dist = Vector3.Distance(transform.position, box.transform.position);
        if (dist > box.interactRange + 0.5f)
            return false;

        var points = GetComponentInParent<PlayerPoints>();
        if (!points) return false;

#if FUSION_WEAVER
        // CLIENT MP → gửi RPC lên host
        if (_net && _net.Object && _net.Object.HasInputAuthority && !_net.HasStateAuthority)
        {
            var boxNo = box.GetComponent<NetworkObject>();
            if (boxNo != null)
            {
                Debug.Log("[RandomBox] CLIENT sending RPC_RequestBuyRandomBox to host.");
                _net.RPC_RequestBuyRandomBox(boxNo);

                return true;
            }

            Debug.LogWarning("[RandomBox] CLIENT cannot send RPC: missing NetworkObject on box.");
            return false;
        }
#endif

        // HOST / SINGLEPLAYER: roll trực tiếp
        Debug.Log("[RandomBox] HOST/SP rolling random weapon.");
        bool ok = HostRollRandomBox(box, points);

        if (ok)
        {
            // 🔊 Local SFX cho người đang dùng hòm (host hoặc SP)
            PlayLocalBuySfx();
        }

        return ok;
    }

    public RandomWeaponBoxSpot PeekLookedRandomBoxLocalOnly(float maxDistanceOverride = -1f)
    {
        if (!playerCamera) return null;

        float dist = maxDistanceOverride > 0f ? maxDistanceOverride : pickupRange;

        if (Physics.Raycast(playerCamera.transform.position,
                            playerCamera.transform.forward,
                            out var hit, dist, pickupMask, triggerQuery))
        {
            return hit.collider.GetComponentInParent<RandomWeaponBoxSpot>();
        }

        return null;
    }

    /// <summary>Chỉ trả về random box nếu đúng local owner (MP) và còn dùng được.</summary>
    public RandomWeaponBoxSpot GetLookedRandomBox()
    {
#if FUSION_WEAVER
        if (_net && _net.Object && !_net.Object.HasInputAuthority)
            return null;
#endif
        var box = PeekLookedRandomBoxLocalOnly();
        if (box != null && box.CanUse())
            return box;
        return null;
    }

    public WorldWeapon SpawnRandomBoxWeapon(WeaponDef def, int mag, int reserve, string runtimeGuid, Transform anchor)
    {
        if (!def || !def.worldPrefab || anchor == null)
        {
            Debug.LogWarning("[RandomBox] Spawn failed: missing def/worldPrefab/anchor");
            return null;
        }

        Vector3 pos = anchor.position;
        Quaternion rot = anchor.rotation;

#if FUSION_WEAVER
        var netObjPrefab = def.worldPrefab.GetComponent<NetworkObject>();
        if (_net != null && _net.Runner != null && netObjPrefab != null)
        {
            var no = _net.Runner.Spawn(
                netObjPrefab, pos, rot, inputAuthority: null,
                onBeforeSpawned: (runner, obj) =>
                {
                    var ww = obj.GetComponent<WorldWeapon>();
                    ww.InitFromDrop(
                        string.IsNullOrEmpty(runtimeGuid) ? System.Guid.NewGuid().ToString() : runtimeGuid,
                        def, mag, reserve);
                    ww.BlockPickupFor(0.2f);
                });
            return no ? no.GetComponent<WorldWeapon>() : null;
        }
#endif

        var go = Instantiate(def.worldPrefab, pos, rot);
        var w = go.GetComponent<WorldWeapon>();
        w.InitFromDrop(
            string.IsNullOrEmpty(runtimeGuid) ? System.Guid.NewGuid().ToString() : runtimeGuid,
            def, mag, reserve);
        w.BlockPickupFor(0.2f);
        return w;
    }

    public void AnnounceRandomBoxResultDeferred(string weaponName)
    {
        if (string.IsNullOrWhiteSpace(weaponName))
            weaponName = "a weapon";

#if FUSION_WEAVER
        // Host sẽ broadcast cho tất cả khi "reveal" xảy ra
        if (_net != null && _net.Runner != null && _net.HasStateAuth)
        {
            _net.RPC_AnnounceRandomBoxResult(weaponName);
            return;
        }
#endif

        // SP / không có runner: local feed
        EventFeed.Push($"You bought a random weapon and got {weaponName}", EventFeedType.Action);
    }


    public bool HostRollRandomBox(RandomWeaponBoxSpot box, PlayerPoints points)
    {
        if (!box || points == null)
        {
            Debug.Log("[RandomBox] HostRollRandomBox aborted: missing box/points");
            return false;
        }

        box.RefreshPendingStatus();

        if (box.HasPendingWeapon)
        {
            Debug.Log("[RandomBox] Pending weapon not picked yet → block new roll.");
            return false;
        }

        if (!box.CanUse())
        {
            Debug.Log("[RandomBox] Box cannot be used anymore.");
            return false;
        }

        // Chọn vũ khí random
        WeaponDef def = box.GetRandomWeapon();
        if (!def)
        {
            Debug.LogWarning("[RandomBox] No WeaponDef in candidates.");
            return false;
        }

        // Trừ điểm trước
        if (!points.TrySpend(box.cost, PointReason.Purchase, box.gameObject))
        {
            Debug.Log("[RandomBox] Not enough points.");
            return false;
        }

        // Tính ammo / runtime guid như cũ
        int mag = def.magSize;
        int reserve = def.startReserve; // giống mua súng từ wall
        string runtimeGuid = System.Guid.NewGuid().ToString();

        // Bắt đầu phiên random box trên chính cái box
        bool started = box.BeginRoll(this, def, mag, reserve, runtimeGuid);
        if (!started)
        {
            Debug.LogWarning("[RandomBox] BeginRoll failed after spending points. (Không refund)");
            return false;
        }

        Debug.Log($"[RandomBox] BeginRoll started for {def.weaponName}. mag={mag} reserve={reserve}");
        return true;
    }


    // ==== PACK-A-PUNCH HELPER ====

    public PackAPunchSpot PeekLookedPackAPunchLocalOnly(float maxDistanceOverride = -1f)
    {
        if (!playerCamera) return null;

        float dist = maxDistanceOverride > 0f ? maxDistanceOverride : pickupRange;

        if (Physics.Raycast(playerCamera.transform.position,
                            playerCamera.transform.forward,
                            out var hit, dist, pickupMask, triggerQuery))
        {
            return hit.collider.GetComponentInParent<PackAPunchSpot>();
        }

        return null;
    }

    public PackAPunchSpot GetLookedPackAPunch()
    {
#if FUSION_WEAVER
        if (_net && _net.Object && !_net.Object.HasInputAuthority)
            return null;
#endif
        var pap = PeekLookedPackAPunchLocalOnly();
        if (pap != null && !pap.IsBusy)
            return pap;
        return null;
    }

    /// <summary>
    /// Thử tương tác với Pack-a-Punch.
    /// Trả về true nếu đã xử lý (đã gửi RPC/đã bắt đầu upgrade).
    /// </summary>
    /// 
    public bool TryInteractPerkMachine()
    {
        var machine = GetLookedPerkMachine();
        if (!machine)
        {
       //     Debug.Log("[Perk][BUY] FAIL: machine == null");
            return false;
        }

        // Quick Revive: SP thuần -> block
#if FUSION_WEAVER
        var runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
        bool isFusionRunning = runner != null && runner.IsRunning;
#else
bool isFusionRunning = false;
#endif

        if (machine.perk != null && machine.perk.IsValid && machine.perk.perkId == "perk_quickrevive" && !isFusionRunning)
        {
            // optional: feed local
        //    EventFeed.Push("Quick Revive is out of order in Solo.", EventFeedType.Action);
            return false;
        }

        // Double Tap: MP -> block (consume input to avoid falling through to Shop)
        if (machine.perk != null && machine.perk.IsValid &&
            machine.perk.perkId == "perk_doubletap" && isFusionRunning)
        {
         //   EventFeed.Push("Double Tap is out of order in Multiplayer.", EventFeedType.Action);
            return true; // IMPORTANT: consume input in MP chain
        }

        // NOTE: UI raycast theo camera, nhưng distance nên đo theo player root (net) cho chuẩn.
        var origin = (_net != null) ? _net.transform.position : transform.root.position;
        float dist = Vector3.Distance(origin, machine.transform.position);

        if (dist > machine.interactRange + 0.5f)
        {
     //       Debug.Log($"[Perk][BUY] FAIL: too far dist={dist:F2} range={machine.interactRange:F2} origin={origin}");
            return false;
        }

        // Robust find (đừng chỉ InParent, vì PlayerPickup có thể nằm ở nhánh camera/weapon)
        var points = GetComponentInParent<PlayerPoints>(true) ?? GetComponentInChildren<PlayerPoints>(true);
        if (!points && _net) points = _net.GetComponentInChildren<PlayerPoints>(true);

        if (!points)
        {
     //       Debug.Log("[Perk][BUY] FAIL: PlayerPoints not found");
            return false;
        }

        var perkMgr = GetComponentInParent<TT.PerkManager>(true) ?? GetComponentInChildren<TT.PerkManager>(true);
        if (!perkMgr && _net) perkMgr = _net.GetComponentInChildren<TT.PerkManager>(true);

        if (!perkMgr)
        {
       //     Debug.Log("[Perk][BUY] FAIL: PerkManager not found");
            return false;
        }

        //    Debug.Log($"[Perk][BUY] TRY: id={machine.perk?.perkId} cost={machine.perk?.cost} points={points.Current}");

#if FUSION_WEAVER
        // ===== MP CLIENT: gửi request lên HOST, KHÔNG mua local =====
        if (_net != null && _net.Runner != null && _net.Object != null
            && _net.Object.HasInputAuthority && !_net.HasStateAuth)
        {
            var machineNo = machine.GetComponentInParent<NetworkObject>();
            if (machineNo == null) return false;

            _net.RPC_RequestBuyPerk(machineNo);
            return true; // consume input
        }
#endif

        // ===== HOST / SP: xử lý trực tiếp =====
        bool ok = perkMgr.TryPurchase(machine.perk, points, machine.gameObject);

        if (ok)
        {
            PlayLocalBuySfx();

#if FUSION_WEAVER
            // HOST đang chạy Fusion -> broadcast feed luôn tại host
            if (_net != null && _net.Runner != null && _net.HasStateAuth)
            {
                _net.RPC_AnnouncePerkBought(machine.DisplayName); // (nếu method đang private thì gọi qua wrapper public)
            }
            else
#endif
            {
                EventFeed.Push($"You bought {machine.DisplayName}", EventFeedType.Action);
            }
        }

        return ok;
    }


    public bool TryInteractPackAPunch()
    {
        var pap = GetLookedPackAPunch();
        if (!pap) return false;

        // Lấy loadout + points
        if (_stateProv == null) _stateProv = GetComponentInParent<PlayerStateProvider>(true);
        if (_loadout == null && _stateProv != null) _loadout = _stateProv.Loadout;
        if (_loadout == null)
        {
            Debug.Log("[PaP] No ILoadoutState.");
            return false;
        }

        var points = GetComponentInParent<PlayerPoints>();
        if (!points)
        {
            Debug.Log("[PaP] No PlayerPoints.");
            return false;
        }

        int activeIndex = _loadout.ActiveSlot;
        if (activeIndex < 0 || activeIndex >= _loadout.SlotCount)
        {
            Debug.Log("[PaP] Active slot out of range.");
            return false;
        }

        var slot = _loadout.GetSlot(activeIndex);
        if (slot.weaponKey == 0)
        {
            Debug.Log("[PaP] Active slot has no weapon.");
            return false;
        }

        var baseDef = WeaponIdRegistry.GetDef(slot.weaponKey);
        if (!baseDef || !baseDef.upgradedVersion)
        {
            Debug.Log("[PaP] This weapon cannot be upgraded.");
            return false;
        }

#if FUSION_WEAVER
        // MP: gửi RPC cho host (đã xử lý riêng)
        if (_net && _net.Object)
        {
            var papNo = pap.GetComponent<NetworkObject>();
            if (papNo)
            {
                Debug.Log("[PaP] Sending RPC_RequestPackAPunch to host.");
                _net.RPC_RequestPackAPunch(papNo);

                return true;
            }
        }
#endif

        // 🔸 SINGLEPLAYER: clear slot + gọi PaP trực tiếp
        Debug.Log("[PaP] Singleplayer TryStartUpgrade directly.");
        bool started = pap.TryStartUpgrade(baseDef, points, gameObject);
        if (started)
        {
            PlayLocalBuySfx();
            // Clear súng gốc khỏi slot active
            slot.weaponKey = 0;
            slot.mag = 0;
            slot.reserve = 0;

            if (_loadout is PlayerLoadoutStateSP spLoad)
            {
                spLoad.ClearSlot(activeIndex);
            }

            // 🔹 Event Feed SP
            if (baseDef.upgradedVersion != null)
            {
                EventFeed.Push(
                    $"You upgraded your {baseDef.weaponName} to {baseDef.upgradedVersion.weaponName}",
                    EventFeedType.Action
                );
            }
        }

        return started;
    }

    // ==== BARRICADE HELPER ====

    public BarricadeWindow PeekLookedBarricadeLocalOnly(float maxDistanceOverride = -1f)
    {
        if (!playerCamera) return null;

        float dist = maxDistanceOverride > 0f ? maxDistanceOverride : pickupRange;

        if (Physics.Raycast(playerCamera.transform.position,
                            playerCamera.transform.forward,
                            out var hit, dist, pickupMask, triggerQuery))
        {
            return hit.collider.GetComponentInParent<BarricadeWindow>();
        }

        return null;
    }

    public BarricadeWindow GetLookedBarricade()
    {
#if FUSION_WEAVER
        // Trong MP: chỉ máy có InputAuthority mới được quyết định UI / tương tác
        if (_net && _net.Object && !_net.Object.HasInputAuthority)
            return null;
#endif

        var window = PeekLookedBarricadeLocalOnly();
        // Chỉ cho tương tác nếu còn slot trống
        if (window != null && window.HasEmptySlot())
            return window;

        return null;
    }

    // ==== BARRICADE HELPER ====
    /// <summary>
    /// Thử tương tác với Barricade (repair).
    /// Dùng cửa mà HUD BarricadeRepairUI đang hiển thị.
    /// </summary>
    public bool TryInteractBarricade()
    {
        // 1) Lấy cửa đang active từ HUD
        BarricadeWindow window = null;
        if (BarricadeRepairUI.Instance != null)
            window = BarricadeRepairUI.Instance.GetCurrentWindow();

        if (window == null)
            return false;

        if (!window.HasEmptySlot())
            return false;

        float maxDist = pickupRange + 1.0f;
        if (Vector3.Distance(transform.position, window.transform.position) > maxDist)
            return false;

#if FUSION_WEAVER
        // 2) CLIENT MP → chỉ gửi request
        if (_net != null && _net.IsLocalOwner && !_net.HasStateAuth)
        {
            var netWindow = window.GetComponent<BarricadeWindowNet>();
            if (netWindow != null)
            {
                netWindow.RPC_RequestRepair();
                return true;
            }
            return false;
        }
#endif

        // 3) HOST + SINGLEPLAYER (CHUNG 1 NHÁNH)
        if (!window.CanStartRebuild(out int slotIndex))
            return false;

        if (!window.StartRebuildAtIndex(slotIndex))
            return false;

        // 🎯 PLAYER LÀ OWNER LOGIC
        RewardBarricadeRepair(window, slotIndex);

        // 🔊 BUY SFX: MP chỉ người bấm nghe (InputAuthority), SP thì local nghe luôn
#if FUSION_WEAVER
        if (_net != null && _net.Runner != null && _net.Runner.IsRunning && _net.HasStateAuth)
        {
            // StateAuthority -> InputAuthority (buyer) => chỉ buyer nghe
            _net.RPC_BarricadeBuySucceeded();
        }
        else
        {
            // SP thuần => local nghe
            PlayLocalBarricadeBuySfx();
        }
#else
PlayLocalBarricadeBuySfx();
#endif


#if FUSION_WEAVER
        // Sync cho client
        var netW = window.GetComponent<BarricadeWindowNet>();
        if (netW != null && _net != null && _net.HasStateAuth)
        {
            netW.RPC_StartRebuildClient(slotIndex);
        }
#endif

        return true;
    }

    // ==== BARRICADE REWARD HELPER ====

    private void RewardBarricadeRepair(BarricadeWindow window, int slotIndex)
    {
        if (window == null) return;

        // 1) Cộng points cho player
        var points = GetComponentInParent<PlayerPoints>();
        if (points != null && window.pointsPerRepair > 0)
        {
            points.Add(window.pointsPerRepair, PointReason.BarricadeRepair, window.gameObject);
        }

        // 2) Bắn Observer event để sau này gắn audio/VFX
        // Payload là tuple: (player, window, slotIndex)
        TT.Observer.Instance?.NotifyWithData(
            "barricade.repair.started",
            (player: this.gameObject, window: window, slotIndex: slotIndex)
        );

    }

    // Chỉ dùng khi HOST xử lý request từ CLIENT
    public bool TryInteractBarricade_FromNet(BarricadeWindow window)
    {
        if (window == null)
            return false;

        if (!window.HasEmptySlot())
            return false;

        if (!window.CanStartRebuild(out int slotIndex))
            return false;

        if (!window.StartRebuildAtIndex(slotIndex))
            return false;

        // 🎯 Player là owner logic
        RewardBarricadeRepair(window, slotIndex);

        // 🔊 Client nghe (reuse RPC đã có)
#if FUSION_WEAVER
        if (_net != null && _net.HasStateAuth)
        {
            _net.RPC_BarricadeBuySucceeded();
        }
#endif



#if FUSION_WEAVER
        // Sync cho tất cả client
        var netW = window.GetComponent<BarricadeWindowNet>();
        if (netW != null && _net != null && _net.HasStateAuth)
        {
            netW.RPC_StartRebuildClient(slotIndex);
        }
#endif

        return true;
    }
}