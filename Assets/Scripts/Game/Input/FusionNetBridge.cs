#if FUSION_WEAVER
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Fusion;
using TT;
using UnityEngine;

[DisallowMultipleComponent]
public class FusionNetBridge : NetworkBehaviour
{
    [SerializeField] private CameraBinder _binder;   // gán sẵn hoặc auto-find
    [SerializeField] private PlayerRefs _refs;     // cache anchor từ prefab
    private bool _cameraBound;

    [Networked] public NetworkString<_16> DisplayName { get; private set; }

    [Networked] public float NetViewYaw { get; private set; }
    [Networked] public float NetViewPitch { get; private set; }

    public PlayerInputData LastInput { get; private set; }
    private PlayerInputData _prevInput;

    private bool _interactHeld;
    private bool _interactEdgeThisTick;
    private bool _interactEdgeConsumed;
    private float _pickupRetryUntil = 0f;

    private bool _fireHeld;
    private bool _fireEdgeThisTick;
    private bool _fireEdgeConsumed;

    private int _buyCooldownTicks = 0;

    [SerializeField] private FusionMovementDriver movementDriver;
    [SerializeField] private FusionWeaponDriver weaponDriver;

    [Networked] public TickTimer FireCD { get; set; }
    [Networked] public TickTimer ActionCD { get; set; }

    public bool IsLocalOwner => Object && Object.HasInputAuthority;
    public bool HasStateAuth => Object && Object.HasStateAuthority;

#if UNITY_EDITOR
    private const bool DEBUG_SERVER_SHOT_RAYS = true;
#else
private const bool DEBUG_SERVER_SHOT_RAYS = false;
#endif

    public override void Spawned()
    {
        if (!_refs) _refs = GetComponentInChildren<PlayerRefs>(true);
        if (!movementDriver)
            movementDriver = GetComponentInChildren<FusionMovementDriver>(true);
        if (!weaponDriver)
            weaponDriver = GetComponentInChildren<FusionWeaponDriver>(true);

        // NEW: chỉ local owner set DisplayName lúc spawn
        if (Object != null && Object.HasInputAuthority)
        {
            var name = PlayerProfileManager.Data.playerName;

            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"Player{Object.InputAuthority.PlayerId}";
            }

            // Gửi cho host set Networked DisplayName
            RPC_SendNameToHost(name);

            // Announce join cho tất cả (host + các client)
            RPC_AnnounceJoined(name);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (Runner == null || !Runner.IsRunning) return;

        // 1) Lấy input
        PlayerInputData inputData;
        if (IsLocalOwner)
        {
            var provider = FusionInputProvider.Instance;
            inputData = provider != null ? provider.GetInputData() : default;
        }
        else if (GetInput<PlayerInputData>(out var netInput))
        {
            inputData = netInput;
        }
        else
        {
            inputData = default;
        }

        LastInput = inputData;

        if (HasStateAuth)
        {
            NetViewYaw = LastInput.viewYaw;
            NetViewPitch = LastInput.viewPitch;
        }

        bool interactNow = LastInput.interact;
        _interactEdgeThisTick = interactNow && !_interactHeld;
        _interactHeld = interactNow;
        _interactEdgeConsumed = false;

        bool fireNow = LastInput.fire;
        _fireEdgeThisTick = fireNow && !_fireHeld;
        _fireHeld = fireNow;
        _fireEdgeConsumed = false;

        if (!movementDriver)
        {
            movementDriver = GetComponentInChildren<FusionMovementDriver>(true);
            if (!movementDriver) return;
        }

        if (!weaponDriver)                                 // <— THÊM
            weaponDriver = GetComponentInChildren<FusionWeaponDriver>(true);

        if (!_cameraBound && Object && Object.HasInputAuthority)
        {
            if (!_binder) _binder = UnityEngine.Object.FindFirstObjectByType<CameraBinder>();
            if (_binder && _refs && _refs.camFollowTarget)
            {
                _binder.OnPlayerSpawned(_refs);                          // gọi method gán Follow/LookAt và fill MainCam/WeaponCam/FovDriver
                _cameraBound = true;
                Debug.Log("[NetBridge] Camera bound for local player.");
            }
            else
            {
                Debug.LogWarning("[NetBridge] Missing binder/refs/camFollowTarget → cannot bind camera.");
            }
        }


        float dt = (float)Runner.DeltaTime;
        int tick = Runner.Tick;

        // 2) Quy tắc tick:
        // - StateAuthority: luôn simulate (authoritative)
        // - LocalOwner: simulate PREDICTION CHỈ khi KHÔNG phải resimulation
        // StateAuthority: luôn simulate
        // LocalOwner: chỉ simulate khi KHÔNG phải resimulation
        if (HasStateAuth || (IsLocalOwner && !Runner.IsResimulation))
        {
            movementDriver.NetworkTick(Runner, tick, dt, LastInput);

            if (weaponDriver)
                weaponDriver.NetworkTick(Runner, tick, dt, LastInput, ConsumeFireEdgeOnce());
        }



        // 1) Interact = pickup (KHÔNG dùng cho shop nữa)
        if (_interactEdgeThisTick && !_interactEdgeConsumed)
        {
            var pickup = GetComponentInChildren<PlayerPickup>(true);
            if (pickup != null)
            {
                //    Debug.Log("[NET][INTERACT] Edge → TryPickupLooked()");
                pickup.TryPickupLooked();
                _pickupRetryUntil = Runner.SimulationTime + 0.25f; // retry trong 0.25s
            }
            _interactEdgeConsumed = true;
        }

        // 2) Buy = Barricade -> Pack-a-Punch -> RandomBox -> Perk -> Shop
        if (LastInput.buy && IsLocalOwner)
        {
            var pickup = GetComponentInChildren<PlayerPickup>(true);
            if (pickup)
            {
                if (!pickup.TryInteractBarricade() &&
                    !pickup.TryInteractPackAPunch() &&
                    !pickup.TryInteractRandomBox() &&
                    !pickup.TryInteractPerkMachine())
                {
                    pickup.TryInteractShop();
                }
            }
        }

        // 2) Retry trong 0.25s miễn là người chơi vẫn nhìn vào WW
        if (Runner.SimulationTime < _pickupRetryUntil)
        {
            var pickup = GetComponentInChildren<PlayerPickup>(true);
            if (pickup)
                pickup.TryPickupLooked(); // retry nhẹ
        }


        // ===== Weapon authority (fire / reload) =====
        if (Object.HasStateAuthority)
        {
            var provider = GetComponent<PlayerStateProvider>();
            var load = provider ? provider.Loadout : null;
            if (load != null)
            {
                // Hoàn tất reload khi timer hết (MP)
                var mp = load as PlayerLoadoutStateMP;
                if (mp != null && mp.IsReloading && mp.ReloadTimer.Expired(Runner))
                {
                    if (mp.CompleteReloadOnActive())
                    {
                        TT.Observer.Instance?.NotifyWithData("weapon.reload.finished",
                            (gameObject, load.ActiveSlot));
                    }
                }

                // Xử lý bắt đầu reload
                if (LastInput.reload && !load.IsReloading)
                {
                    if (load.TryStartReloadOnActive(out var defStarted))
                    {
                        TT.Observer.Instance?.NotifyWithData("weapon.reload.started",
                            (gameObject, load.ActiveSlot, defStarted?.weaponId));
                    }
                }
            }
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SendNameToHost(string name)
    {
        if (!HasStateAuth)
            return;

        if (string.IsNullOrWhiteSpace(name))
            name = $"Player{Object.InputAuthority.PlayerId}";

        DisplayName = name;

        //     Debug.Log($"[NetBridge] Host received DisplayName='{DisplayName}' for player {Object.InputAuthority.PlayerId}");
    }


    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestPickup(NetworkObject wwNO, RpcInfo info = default)
    {
        if (!HasStateAuthority || wwNO == null) return;

        var ww = wwNO.GetComponent<WorldWeapon>();
        if (!ww || !ww.weaponDef) return;

        //    Debug.Log($"[RPC_Pickup] From {info.Source}, WW: {ww.weaponDef?.name}");

        // validate khoảng cách đơn giản
        Vector3 eye = (_refs && _refs.camFollowTarget) ? _refs.camFollowTarget.position : transform.position + Vector3.up * 1.6f;
        Vector3 target = ww.transform.position; // Dùng center thay vì closest point

        const float RANGE = 5.0f; // Tăng từ 4.0 lên 5.0
        if (Vector3.Distance(eye, target) > RANGE)
        {
            //    Debug.Log($"[RPC_Pickup] Out of range: {Vector3.Distance(eye, target):F2}");
            return;
        }

        // if ((target - eye).sqrMagnitude > RANGE * RANGE) return;

        var prov = GetComponent<PlayerStateProvider>();
        var load = prov ? prov.Loadout : null;
        if (load == null) return;

        var pickup = GetComponentInChildren<PlayerPickup>(true);
        if (!pickup || !pickup.CanPickupNow(ww)) return;

        // Trong RPC_RequestPickup, sau khi TryPickup thành công:
        if (load.TryPickup(ww))
        {
            int weaponKey = WeaponIdRegistry.GetKey(ww.weaponDef.weaponId);
            int slotIdx = load.ActiveSlot;

            // Đảm bảo tìm đúng slot
            if (slotIdx < 0 || load.GetSlot(slotIdx).weaponKey != weaponKey)
            {
                slotIdx = -1;
                for (int i = 0; i < load.SlotCount; i++)
                {
                    if (load.GetSlot(i).weaponKey == weaponKey)
                    {
                        slotIdx = i;
                        break;
                    }
                }
            }

            if (slotIdx >= 0)
            {
                var slotState = load.GetSlot(slotIdx);

                // GÁN GUID cho host bridge NGAY LẬP TỨC
                var hostBridge = GetComponentInChildren<PlayerWeaponBridge>(true);
                if (hostBridge != null)
                {
                    hostBridge.SetRuntimeGuid(slotIdx, ww.runtimeGuid);
                }

                // GỬI RPC đến client để equip visual TỨC THÌ
                RPC_ClientEquipAfterPickup(slotIdx, weaponKey,
                                           (ushort)Mathf.Clamp(ww.magOnGround, 0, ww.weaponDef.magSize),
                                           (ushort)Mathf.Clamp(ww.reserveOnGround, 0, ww.weaponDef.maxReserve),
                                           ww.runtimeGuid);

                //      Debug.Log($"[NetBridge] RPC_ClientEquipAfterPickup called for slot {slotIdx}");
            }

            ww.OnPickedUp();
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestReplace(NetworkObject wwNO, RpcInfo info = default)
    {
        if (!HasStateAuthority || wwNO == null) return;
        var ww = wwNO.GetComponent<WorldWeapon>();
        if (!ww || !ww.weaponDef) return;

        // (giữ kiểm tra khoảng cách nếu bạn muốn)

        var prov = GetComponent<PlayerStateProvider>();
        var load = prov ? prov.Loadout : null;
        if (load == null) return;

        // 1) Drop active: spawn world weapon + clear state (authoritative)
        var pickup = GetComponentInChildren<PlayerPickup>(true);
        if (pickup != null)
        {
            pickup.ServerSpawnDropActive();
        }
        else
        {
            load.TryDropActive(); // fallback nếu thiếu PlayerPickup (khó xảy ra)
        }


        // 2) Pickup cái mới qua state
        if (load.TryPickup(ww))
        {
            // Giống như RPC_RequestPickup:
            int key = WeaponIdRegistry.GetKey(ww.weaponDef.weaponId);
            int slotIdx = load.ActiveSlot;

            // đảm bảo tìm đúng slot chứa loại mới
            if (slotIdx < 0 || load.GetSlot(slotIdx).weaponKey != key)
            {
                slotIdx = -1;
                for (int i = 0; i < load.SlotCount; i++)
                {
                    if (load.GetSlot(i).weaponKey == key) { slotIdx = i; break; }
                }
            }

            if (slotIdx >= 0)
            {
                // gán GUID cho host-bridge NGAY LẬP TỨC
                var hostBridge = GetComponentInChildren<PlayerWeaponBridge>(true);
                if (hostBridge != null) hostBridge.SetRuntimeGuid(slotIdx, ww.runtimeGuid);

                // gửi RPC equip tức thì về client
                var def = WeaponIdRegistry.GetDef(key);
                ushort mag = (ushort)Mathf.Clamp(ww.magOnGround, 0, def.magSize);
                ushort res = (ushort)Mathf.Clamp(ww.reserveOnGround, 0, def.maxReserve);
                RPC_ClientEquipAfterPickup(slotIdx, key, mag, res, ww.runtimeGuid);
            }

            ww.OnPickedUp();
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestDrop(RpcInfo info = default)
    {
        if (!HasStateAuthority) return;

        var pickup = GetComponentInChildren<PlayerPickup>(true);
        if (pickup)
        {
            // server spawn đồ rơi + clear state
            pickup.ServerSpawnDropActive();
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    public void RPC_SetSlotRuntimeGuid(int slotIndex, string guid, RpcInfo info = default)
    {
        var bridge = GetComponentInChildren<PlayerWeaponBridge>(true);
        if (bridge != null)
        {
            bridge.SetRuntimeGuid(slotIndex, guid);
            Debug.Log($"[NetBridge] RPC_SetSlotRuntimeGuid slot={slotIndex} guid={guid}");
        }
    }

    // FusionNetBridge.cs
    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    public void RPC_ClientEquipAfterPickup(int slotIndex, int weaponKey, ushort mag, ushort reserve, string guid, RpcInfo info = default)
    {
        Debug.Log($"[RPC_ClientEquipAfterPickup] START - Slot: {slotIndex}, WeaponKey: {weaponKey}, GUID: {guid}");

        StartCoroutine(Co_EquipWhenReady());

        IEnumerator Co_EquipWhenReady()
        {
            // đợi vài frame cho Awake/refs sẵn
            for (int i = 0; i < 10; i++) yield return null;

            var bridge = GetComponentInChildren<PlayerWeaponBridge>(true);
            var inv = GetComponentInChildren<PlayerInventory>(true);

            if (bridge == null || inv == null)
            {
                Debug.LogError("[RPC_ClientEquipAfterPickup] Bridge or Inventory is NULL!");
                yield break;
            }

            // ensure refs trong bridge đã có
            bridge.ForceResolveRefs();   // mình sẽ thêm hàm này ở bước 2

            // chờ đến khi có WeaponController thật sự
            for (int i = 0; i < 30 && bridge.GetCurrentWeapon() == null; i++)
                yield return null;

            var def = WeaponIdRegistry.GetDef(weaponKey);
            if (def == null)
            {
       //         Debug.LogError($"[RPC_ClientEquipAfterPickup] Cannot find WeaponDef for key: {weaponKey}");
                yield break;
            }

            var runtime = new AmmoModule
            {
                mag = mag,
                reserve = reserve,
                magSize = def.magSize
            };

      //      Debug.Log($"[RPC_ClientEquipAfterPickup] Equipping {def.weaponName} in slot {slotIndex}");

            bridge.BeginInventoryTransaction();
            try
            {
                inv.SetSlot(slotIndex, def);
                bridge.EquipIntoSlot(slotIndex, def, runtime, guid);
          //      Debug.Log($"[RPC_ClientEquipAfterPickup] SUCCESS - Weapon equipped locally");
            }
            finally
            {
                bridge.EndInventoryTransaction();
            }
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_SpawnImpact(Vector3 pos, Vector3 normal, int weaponKey, int surfaceType, RpcInfo info = default)
    {
        var def = WeaponIdRegistry.GetDef(weaponKey);
        if (def == null) return;

        SurfaceType surf = (SurfaceType)surfaceType;

        var fx = ImpactHelper.GetVFX(def, surf);
        if (fx != null)
            ImpactPool.Instance?.Spawn(fx, pos, normal, -1f, 0.002f);

        var sfx = ImpactHelper.GetSFX(def, surf);
        if (sfx != null)
        {
            // ✅ Cho shooter cũng nghe (vì hiện tại shooter không tự play impact local)
            AudioEvents.PlayWorld3D(sfx.eventId, pos);
        }

    }


    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestFire(Vector3 origin, Vector3 dir, bool isADS, RpcInfo info = default)
    {
        if (!HasStateAuth) return;

        var provider = GetComponent<PlayerStateProvider>();
        var load = provider ? provider.Loadout : null;
        if (load == null || load.IsReloading) return;

        // ===== BLOCK FIRE WHILE SPRINT (server-side authority) =====
        var mov = provider ? provider.Movement : null;
        if (mov != null && mov.Current == MovementStateId.Sprinting)
            return;


        // Cadence (ROF) – dùng cùng timer với server
        if (!FireCD.ExpiredOrNotRunning(Runner)) return;

        // Xác định vũ khí active
        int slot = load.ActiveSlot;
        if (slot < 0 || slot >= load.SlotCount) return;
        var s = load.GetSlot(slot);
        if (s.IsEmpty) return;

        var defActive = WeaponIdRegistry.GetDef(s.weaponKey);
        if (defActive == null) return;

        // Trừ đạn server-side
        if (!load.TryConsumeOneOnActive(out var defFired)) return;

        RPC_SpawnShotFX(s.weaponKey);

        var netAnim = GetComponentInChildren<PlayerNetworkAnimator>(true);
        if (netAnim != null)
        {
            netAnim.NotifyShotFired();
        }

        // Đặt cooldown
        float interval = (defActive != null && defActive.rpm > 0f) ? (60f / defActive.rpm) : 0.1f;
        FireCD = TickTimer.CreateFromSeconds(Runner, interval);

            Vector3 centerDir = dir.normalized;

            int pellets = Mathf.Max(1, defActive.pelletCount);
            bool isShotgun = pellets > 1;

            // movement flags giống WeaponController (SP) đang làm
            // (đã có mov ở trên rồi) => dùng lại
            bool isMoving =
                mov != null && (mov.Current == MovementStateId.Walking || mov.Current == MovementStateId.Sprinting);

            bool isAir =
                mov != null && (mov.Current == MovementStateId.Jumping || mov.Current == MovementStateId.Falling);

            bool isCrouch =
                mov != null && (mov.Current == MovementStateId.Crouching);


            // (A) Hipfire cone CHUNG (theo RecoilProfile) — chỉ áp dụng khi HIPFIRE
            Vector2 baseYawPitch = Vector2.zero;
            if (!isADS && defActive.RecoilProfile != null)
            {
                baseYawPitch = defActive.RecoilProfile.GetHipfireSpreadDeg(isMoving, isAir, isCrouch);
            }

            // (B) Pellet scatter RIÊNG cho shotgun (mỗi pellet lệch thêm)
            // ADS shotgun vẫn có scatter => dùng pelletSpreadDegADS
            float pelletScatter = isADS ? defActive.pelletSpreadDegADS : defActive.pelletSpreadDegHip;
            Vector2 extraYawPitch = (isShotgun && pelletScatter > 0f) ? new Vector2(pelletScatter, pelletScatter) : Vector2.zero;

            // Tổng spread cho mỗi pellet
            Vector2 totalYawPitch = baseYawPitch + extraYawPitch;

        if (DEBUG_SERVER_SHOT_RAYS)
        {
            Debug.DrawRay(origin, centerDir * 25f, Color.white, 0.25f); // trắng = tâm
        }


        for (int i = 0; i < pellets; i++)
            {
                Vector3 shotDir = centerDir;

                if (totalYawPitch.x > 0.001f || totalYawPitch.y > 0.001f)
                {
                    shotDir = ApplyYawPitchCone(centerDir, totalYawPitch.x, totalYawPitch.y);
                }

            if (DEBUG_SERVER_SHOT_RAYS && i == 0)
            {
                Debug.DrawRay(origin, shotDir * 25f, Color.yellow, 0.25f); // vàng = pellet 0
            }

            Ray ray = new Ray(origin, shotDir);
                int maxPen = (defActive.enablePenetration) ? Mathf.Max(0, defActive.maxPenetrations) : 0;

                float remaining = defActive.maxDistance;
                Vector3 curOrigin = origin;
                int penCount = 0;

            var ignored = new System.Collections.Generic.HashSet<Collider>();
            var hitVictimRoots = new System.Collections.Generic.HashSet<int>();

            while (remaining > 0.001f)
                {
                    if (!Physics.Raycast(curOrigin, shotDir, out var hit, remaining, defActive.hitMask, QueryTriggerInteraction.Ignore))
                        break;

                    if (hit.collider != null && ignored.Contains(hit.collider))
                    {
                        curOrigin = hit.point + shotDir * 0.02f;
                        remaining -= hit.distance + 0.02f;
                        continue;
                    }

                    var hb = hit.collider ? hit.collider.GetComponent<Hitbox>() : null;

                    // ArmorPlate chặn = stop bullet
                    var armor = hit.collider ? hit.collider.GetComponentInParent<ArmorPlate>() : null;
                    bool isHeadHit = hb != null && hb.hitboxId == HitboxId.Head;
                    if (armor != null && armor.blocksDamage && !(armor.allowHeadshot && isHeadHit))
                    {
                        // Impact network: để đỡ spam, chỉ pellet đầu tiên (i==0) mới RPC impact
                        if (i == 0)
                        {
                            SurfaceType surface = ImpactHelper.DetectSurface(hit, isFlesh: false);
                            RPC_SpawnImpact(hit.point, hit.normal, s.weaponKey, (int)surface);
                        }
                        break;
                    }

                    IDamageable victim = hb ? hb.GetDamageable()
                                            : (hit.collider ? hit.collider.GetComponentInParent<IDamageable>() : null);

                    // Impact: (tuỳ bạn) mình vẫn giữ rule “chỉ pellet đầu tiên” nhưng cho xuyên nhiều hit
                    if (i == 0)
                    {
                        bool isFlesh = victim != null;
                        SurfaceType surface = ImpactHelper.DetectSurface(hit, isFlesh);
                        RPC_SpawnImpact(hit.point, hit.normal, s.weaponKey, (int)surface);
                    }

                    // hit world => stop
                    if (victim == null)
                        break;

                    GameObject vicGO = null;
                    if (victim is MonoBehaviour mb) vicGO = mb.gameObject;
                    else if (hit.collider) vicGO = hit.collider.gameObject;

                // ✅ NEW: chặn 1 pellet hit nhiều hitbox của cùng 1 victim (double points)
                int victimRootId = 0;
                if (vicGO != null)
                {
                    var root = vicGO.transform.root;
                    victimRootId = root ? root.gameObject.GetInstanceID() : vicGO.GetInstanceID();
                }
                else if (hit.collider)
                {
                    var root = hit.collider.transform.root;
                    victimRootId = root ? root.gameObject.GetInstanceID() : hit.collider.gameObject.GetInstanceID();
                }

                if (victimRootId != 0 && hitVictimRoots.Contains(victimRootId))
                {
                    // đã hit victim này rồi => bỏ qua hit này, tiến origin lên để ray đi tiếp
                    if (hit.collider != null) ignored.Add(hit.collider);

                    curOrigin = hit.point + shotDir * 0.02f;
                    remaining -= hit.distance + 0.02f;
                    continue;
                }

                if (victimRootId != 0)
                    hitVictimRoots.Add(victimRootId);


                // falloff theo số lần xuyên
                float mul = (penCount > 0)
                        ? Mathf.Pow(Mathf.Clamp01(defActive.damageMultiplierPerPenetration), penCount)
                        : 1f;

                    float scaledDamage = defActive.baseDamage * mul;
                    if (defActive.minDamageAfterPenetration > 0f)
                        scaledDamage = Mathf.Max(defActive.minDamageAfterPenetration, scaledDamage);

                    var e = new DamageEvent
                    {
                        attacker = this.gameObject,
                        victimGO = vicGO,
                        victim = victim,
                        weaponId = defActive.weaponId,
                        baseDamage = scaledDamage,
                        damageType = defActive.damageType,
                        distance = hit.distance,
                        penetrationCount = penCount,
                        hitPoint = hit.point,
                        hitNormal = hit.normal,
                        shotDirection = shotDir,
                        hitCollider = hit.collider,
                        hitboxId = hb ? hb.hitboxId : HitboxId.Default,
                        time = Time.time
                    };

                    DamageRouter.Apply(e);

                    if (penCount >= maxPen)
                        break;

                    if (hit.collider != null) ignored.Add(hit.collider);

                    curOrigin = hit.point + shotDir * 0.02f;
                    remaining -= hit.distance + 0.02f;
                    penCount++;
                }


            }
        

        // (Tuỳ chọn) log fired cho analytics/UI
        TT.Observer.Instance?.NotifyWithData("weapon.fired",
            (gameObject, slot, defActive != null ? defActive.weaponId : null));
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestUnlockZone(NetworkObject zoneObject, RpcInfo info = default)
    {
        Debug.Log($"[Unlock][Host] RPC_RequestUnlockZone RECEIVED from {info.Source}");

        if (!zoneObject) { Debug.Log("[Unlock][Host] zoneObject NULL"); return; }

        var zone = zoneObject.GetComponent<ZoneUnlockablePoints>();
        if (!zone) { Debug.Log("[Unlock][Host] NO ZoneUnlockablePoints"); return; }

        var wallet = GetComponentInChildren<PlayerPoints>();
        if (!wallet) { Debug.Log("[Unlock][Host] NO PlayerPoints"); return; }

        Debug.Log("[Unlock][Host] TRY UNLOCK");
        bool success = zone.TryUnlock(wallet);

        if (success)
        {
            // Host thông báo cho tất cả máy
            RPC_AnnounceZoneUnlocked(zoneObject);
            RPC_BuySucceeded();
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_AnnounceZoneUnlocked(NetworkObject zoneObject, RpcInfo info = default)
    {
        string playerName = GetDisplayNameSafe();
        string zoneName = "Unknown zone";

        if (zoneObject)
        {
            var z = zoneObject.GetComponent<ZoneUnlockablePoints>();
            if (z != null && !string.IsNullOrEmpty(z.displayName))
                zoneName = z.displayName;
        }

        EventFeed.Push($"{playerName} unlocked {zoneName}", EventFeedType.Action);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestBuyPerk(NetworkObject perkMachineObject, RpcInfo info = default)
    {
        if (!HasStateAuthority) return;
        if (perkMachineObject == null) return;

        var machine = perkMachineObject.GetComponent<TT.PerkMachineSpot>();
        if (machine == null || !machine.CanUse()) return;

        // distance validate (giống PlayerPickup)
        float dist = Vector3.Distance(transform.position, machine.transform.position);
        if (dist > machine.interactRange + 0.75f) return;

        var points = GetComponentInChildren<PlayerPoints>(true) ?? GetComponentInParent<PlayerPoints>(true);
        var perkMgr = GetComponentInParent<TT.PerkManager>(true); // (đừng GetComponentInChildren)

        if (points == null || perkMgr == null) return;

        bool ok = perkMgr.TryPurchase(machine.perk, points, machine.gameObject);
        if (!ok) return;

        // ✅ GRANT networked perk state ở đây
        var netState = GetComponentInParent<TT.PerkNetState>(true);
        if (netState != null)
        {
            // map perkId -> enum (tùy id của bạn)
            switch (machine.perk.perkId)
            {
                case "perk_doubletap":
                    netState.GrantOnServer(TT.PerkId.DoubleTap);
                    break;
                case "perk_speedcola":
                    netState.GrantOnServer(TT.PerkId.SpeedCola);
                    break;
            }
        }

        RPC_BuySucceeded();
        RPC_AnnouncePerkBought(machine.DisplayName);
    }


    // Client (InputAuthority) -> Host (StateAuthority)
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestAnnouncePerkBought(string perkName, RpcInfo info = default)
    {
        if (!HasStateAuthority) return;

        if (string.IsNullOrWhiteSpace(perkName))
            perkName = "a perk";

        // Host broadcast cho tất cả
        RPC_AnnouncePerkBought(perkName);
    }

    // Host -> All
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_AnnouncePerkBought(string perkName, RpcInfo info = default)
    {
        if (string.IsNullOrWhiteSpace(perkName))
            perkName = "a perk";

        // Object này là của người mua. Chỉ máy người mua mới có InputAuthority trên object này.
        if (Object != null && Object.HasInputAuthority)
        {
            EventFeed.Push($"You bought {perkName}", EventFeedType.Action);
        }
        else
        {
            string buyerName = GetDisplayNameSafe();
            EventFeed.Push($"{buyerName} bought {perkName}", EventFeedType.Action);
        }
    }


    private string GetDisplayNameSafe()
    {
        try
        {
            // Chỉ đọc Networked khi object còn hợp lệ + runner đang chạy
            if (Object != null && Object.IsValid && Runner != null && Runner.IsRunning)
            {
                var s = DisplayName.ToString();
                if (!string.IsNullOrEmpty(s))
                    return s;
            }
        }
        catch (System.InvalidOperationException)
        {
            // Bị gọi sau khi despawn → bỏ qua, trả fallback ở dưới
        }

        // Fallback an toàn
        return gameObject != null ? gameObject.name : "Player";
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_AnnouncePowerUpCollected(PowerUpType type, RpcInfo info = default)
    {
        string playerName = GetDisplayNameSafe();

        string msg = type switch
        {
            PowerUpType.MaxAmmo => $"{playerName} picked up MAX AMMO!",
            PowerUpType.DoublePoints => $"{playerName} picked up DOUBLE POINTS!",
            PowerUpType.InstaKill => $"{playerName} picked up INSTA-KILL!",
            PowerUpType.Nuke => $"{playerName} picked up NUKE!",
            _ => $"{playerName} picked up a power-up!"
        };

        var feedType = type switch
        {
            PowerUpType.Nuke => EventFeedType.Danger,
            PowerUpType.MaxAmmo => EventFeedType.Success,
            PowerUpType.DoublePoints => EventFeedType.Action,
            PowerUpType.InstaKill => EventFeedType.Action,
            _ => EventFeedType.Info
        };

        EventFeed.Push(msg, feedType);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_HudPowerUpTimedStarted(PowerUpType type, float durationSeconds, RpcInfo info = default)
    {
        TT.Observer.Instance?.NotifyWithData("hud.powerup.timed.started", (type, durationSeconds));
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_HudPowerUpTimedEnded(PowerUpType type, RpcInfo info = default)
    {
        TT.Observer.Instance?.NotifyWithData("hud.powerup.timed.ended", type);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_AnnounceRandomBoxResult(string weaponName, RpcInfo info = default)
    {
        string playerName = GetDisplayNameSafe();
        if (string.IsNullOrWhiteSpace(weaponName))
            weaponName = "a weapon";

        EventFeed.Push($"{playerName} has bought random weapon and got {weaponName}", EventFeedType.Action);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_AnnounceWallBuy(string weaponName, RpcInfo info = default)
    {
        string playerName = GetDisplayNameSafe();
        if (string.IsNullOrWhiteSpace(weaponName))
            weaponName = "something";

        EventFeed.Push($"{playerName} has bought {weaponName}", EventFeedType.Action);
    }



    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestBuyRandomBox(NetworkObject boxObject, RpcInfo info = default)
    {
        Debug.Log($"[RandomBox][Host] RPC_RequestBuyRandomBox from {info.Source}");

        if (!HasStateAuth)
        {
            Debug.LogWarning("[RandomBox][Host] Not StateAuthority, abort.");
            return;
        }

        if (!boxObject)
        {
            Debug.LogWarning("[RandomBox][Host] boxObject NULL");
            return;
        }

        var box = boxObject.GetComponent<RandomWeaponBoxSpot>();
        if (!box)
        {
            Debug.LogWarning("[RandomBox][Host] No RandomWeaponBoxSpot on object.");
            return;
        }

        var pickup = GetComponentInChildren<PlayerPickup>(true);
        var wallet = GetComponentInChildren<PlayerPoints>(true);
        if (!pickup || !wallet)
        {
            Debug.LogWarning("[RandomBox][Host] Missing PlayerPickup/PlayerPoints on player.");
            return;
        }

        bool ok = pickup.HostRollRandomBox(box, wallet);

        if (ok)
        {
            RPC_BuySucceeded(); // ✅ CHỈ khi mua thành công
        }
        Debug.Log($"[RandomBox][Host] HostRollRandomBox result = {ok}");
    }


    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestPackAPunch(NetworkObject papObject, RpcInfo info = default)
    {
        Debug.Log($"[PaP][Host] RPC_RequestPackAPunch from {info.Source}");

        if (!HasStateAuthority)
        {
            Debug.LogWarning("[PaP][Host] Not StateAuthority.");
            return;
        }

        if (!papObject)
        {
            Debug.LogWarning("[PaP][Host] papObject is null.");
            return;
        }

        var pap = papObject.GetComponent<PackAPunchSpot>();
        if (!pap)
        {
            Debug.LogWarning("[PaP][Host] No PackAPunchSpot on object.");
            return;
        }

        var stateProv = GetComponentInChildren<PlayerStateProvider>(true);
        var loadout = stateProv != null ? stateProv.Loadout : null;
        var points = GetComponentInChildren<PlayerPoints>(true);

        if (loadout == null || points == null)
        {
            Debug.LogWarning("[PaP][Host] Missing Loadout or PlayerPoints on player.");
            return;
        }

        int activeIndex = loadout.ActiveSlot;
        if (activeIndex < 0 || activeIndex >= loadout.SlotCount)
        {
            Debug.LogWarning("[PaP][Host] Active slot invalid.");
            return;
        }

        var slot = loadout.GetSlot(activeIndex);
        if (slot.weaponKey == 0)
        {
            Debug.LogWarning("[PaP][Host] Active slot has no weapon.");
            return;
        }

        var baseDef = WeaponIdRegistry.GetDef(slot.weaponKey);
        if (!baseDef)
        {
            Debug.LogWarning("[PaP][Host] WeaponDef not found for key " + slot.weaponKey);
            return;
        }

        if (!baseDef.upgradedVersion)
        {
            Debug.LogWarning("[PaP][Host] Weapon has no upgradedVersion.");
            return;
        }

        // 🟡 GỠ SÚNG GỐC RA KHỎI SLOT ACTIVE TRƯỚC KHI NÂNG CẤP
        if (loadout is PlayerLoadoutStateMP mpLoad)
        {
            // chỉ clear state, không spawn drop
            if (!mpLoad.TryDropActive())
            {
                Debug.LogWarning("[PaP][Host] TryDropActive failed, abort upgrade.");
                return;
            }
        }
        else
        {
            Debug.LogWarning("[PaP][Host] Loadout is not MP type, abort.");
            return;
        }

        // Bắt đầu animation + spawn PaP weapon (WorldWeapon)
        bool ok = pap.TryStartUpgrade(baseDef, points, gameObject);
        //     Debug.Log($"[PaP][Host] TryStartUpgrade result = {ok}");

        if (ok)
        {
            // 🔹 Broadcast event feed cho tất cả
            var upgraded = baseDef.upgradedVersion;
            string oldName = baseDef.weaponName;
            string newName = upgraded ? upgraded.weaponName : "upgraded weapon";

            RPC_BuySucceeded();
            RPC_AnnouncePackAPunch(oldName, newName);
        }
        else
        {
            // Nếu vì lý do gì đó PaP fail, có thể rollback nếu bạn muốn
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_AnnouncePackAPunch(string baseWeaponName, string upgradedWeaponName, RpcInfo info = default)
    {
        string playerName = GetDisplayNameSafe();

        if (string.IsNullOrWhiteSpace(baseWeaponName))
            baseWeaponName = "weapon";
        if (string.IsNullOrWhiteSpace(upgradedWeaponName))
            upgradedWeaponName = "an upgraded weapon";

        EventFeed.Push(
            $"{playerName} has upgraded their {baseWeaponName} to {upgradedWeaponName}",
            EventFeedType.Action
        );
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_AnnounceDowned(RpcInfo info = default)
    {
        string playerName = GetDisplayNameSafe();

        if (IsLocalOwner)
        {
            // Máy nào sở hữu thằng bị gục sẽ thấy "You..."
            EventFeed.Push("You have been downed!", EventFeedType.Danger);
        }
        else
        {
            // Các máy khác thấy tên player
            EventFeed.Push($"{playerName} has been downed!", EventFeedType.Danger);
        }
    }

    // Player JOIN
    [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
    public void RPC_AnnounceJoined(string displayName, RpcInfo info = default)
    {
        // Luôn hiện tên, không dùng "You"
        string name = string.IsNullOrWhiteSpace(displayName)
            ? GetDisplayNameSafe()
            : displayName;

        EventFeed.Push($"{name} joined the game.", EventFeedType.Info);
    }

    // Player LEAVE
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_AnnounceLeft(string displayName, RpcInfo info = default)
    {
        // Luôn hiện tên, không dùng "You"
        string name = string.IsNullOrWhiteSpace(displayName)
            ? GetDisplayNameSafe()
            : displayName;

        EventFeed.Push($"{name} left the game.", EventFeedType.Warning);
    }

    public bool ConsumeFireEdgeOnce()
    {
        if (_fireEdgeConsumed) return false;
        _fireEdgeConsumed = true;
        return _fireEdgeThisTick;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    public void RPC_BuySucceeded()
    {
        if (Object == null || !Object.HasInputAuthority) return;

        var pickup = GetComponentInChildren<PlayerPickup>(true);
        if (pickup != null)
            pickup.PlayLocalBuySfx_Authoritative();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    public void RPC_BarricadeBuySucceeded()
    {
        if (Object == null || !Object.HasInputAuthority) return;

        var pickup = GetComponentInChildren<PlayerPickup>(true);
        if (pickup != null)
            pickup.PlayLocalBarricadeBuySfx_Authoritative();
    }


    static Vector3 ApplyCone(Vector3 dir, float spreadDeg)
    {
        if (spreadDeg <= 0.0001f) return dir.normalized;
        dir = dir.normalized;

        Vector3 right = Vector3.Cross(Vector3.up, dir);
        if (right.sqrMagnitude < 1e-6f)
            right = Vector3.Cross(Vector3.forward, dir);
        right.Normalize();

        Vector3 up = Vector3.Cross(dir, right).normalized;

        Vector2 off = Random.insideUnitCircle * spreadDeg;
        Quaternion q = Quaternion.AngleAxis(off.x, up) * Quaternion.AngleAxis(off.y, right);
        return (q * dir).normalized;
    }

    static Vector3 ApplyYawPitchCone(Vector3 baseDir, float yawDeg, float pitchDeg)
    {
        baseDir = baseDir.normalized;

        // random trong rectangle yaw/pitch (đủ tốt, dễ hiểu)
        float yaw = UnityEngine.Random.Range(-yawDeg, yawDeg);
        float pitch = UnityEngine.Random.Range(-pitchDeg, pitchDeg);

        // giống logic trong recoil: Quaternion.Euler(-pitch, yaw, 0)
        Quaternion q = Quaternion.Euler(-pitch, yaw, 0f);
        return (q * baseDir).normalized;
    }


    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_SpawnShotFX(int weaponKey, RpcInfo info = default)
    {
        if (weaponKey == 0) return;

        var worldView = GetComponentInChildren<PlayerWeaponWorldView>(true);
        if (worldView != null)
            worldView.PlayWorldMuzzleForWeaponKey(weaponKey);

        // ✅ Shooter machine: KHÔNG phát world gunshot (shooter đã nghe local từ WeaponAudioDriver)
        if (Object != null && Object.HasInputAuthority && Object.InputAuthority == info.Source)
            return;

        var def = WeaponIdRegistry.GetDef(weaponKey);
        if (def != null && def.fireAudio != null)
        {
            // Khuyến nghị: phát thẳng, KHÔNG đi qua AudioEvents (vì AudioEvents có thể lại bắn RPC)
            AudioManager.Instance?.Play3DAtPoint(def.fireAudio.eventId, transform.position);
            // hoặc nếu bạn muốn 3D spatial đúng hơn theo emitter weapon world:
            // AudioManager.Instance?.Play3DAttached(def.fireAudio.eventId, worldView.transform);
        }
    }

}
#endif