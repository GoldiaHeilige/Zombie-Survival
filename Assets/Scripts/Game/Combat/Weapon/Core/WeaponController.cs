using System;
using System.Collections;
using UnityEngine;
using Game.Combat.Weapon.Recoil;
using TT;

public class WeaponController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform weaponViewRoot;
    [SerializeField] private Camera aimCamera;
    [SerializeField] private PlayerNetworkAnimator playerNetworkAnimator;
    public Transform owner;

    [Header("Sub-Controllers")]
    [SerializeField] private WeaponViewController viewCtrl;
    [SerializeField] private FireController fireCtrl;
    [SerializeField] private ReloadController reloadCtrl;

    [Header("Recoil")]
    [SerializeField] private RecoilController recoil;
    [SerializeField] private CameraRecoilDriver cameraDriver;
    [SerializeField] private Transform viewKickPivot;

    [Header("Recoil & ADS")]
    [SerializeField] private WeaponSwayBob sway;
    [SerializeField] private CameraADSFOVDriver fovDriver;

    private PlayerMovementController move;
    private IMovementState moveState;

    [Header("Runtime State")]
    public WeaponDef def { get; private set; }
    public AmmoModule ammo { get; private set; }
    public bool CanFireNow { get; private set; } = true;

    string _runtimeGuid;
    public int _slotIndex = -1;
    // --- Reload visual timer ---
    private bool _wasReloadingVisual = false;
    private float _reloadVisualTime = 0f;
    private float _reloadVisualDuration = 0f;
    float _actionLockUntil = -999f;

    ILoadoutState _load;
    bool _wasReloading;
    private bool _isReloadTimelineRunning = false;

    public event Action<AmmoChangedArgs> AmmoChanged;
    public event Action OnShot;
    public event Action OnReloadStart;
    public event Action OnReloadEnd;
    public event Action OnFPViewReady;

    public WeaponFSM fsm { get; private set; }
    Coroutine _firingWatcher;
    float _lastShotTime = -999f;
    PlayerRefs _refs;

    private PlayerStateProvider _stateProv;

    private FusionNetBridge _net;
    private bool IsLocalOwner =>
     _net == null ||
     _net.Object == null ||
     !_net.Object.IsValid ||
     _net.IsLocalOwner;

    private Coroutine _adsRebindRoutine;   // nếu bạn chưa có thì có thể bỏ, không bắt buộc
    private bool _lastADSState = false;

    void Awake()
    {
        //      Debug.Log($"[WC] Awake called, enabled={enabled}, GO active={gameObject.activeInHierarchy}");


        _net = GetComponentInParent<FusionNetBridge>(true);
        _refs = GetComponentInParent<PlayerRefs>();
        move = GetComponentInParent<PlayerMovementController>(true);
        var prov = GetComponentInParent<PlayerStateProvider>(true);
        _load = prov ? prov.Loadout : null;
        if (_load != null)
        {
            _load.OnSlotChanged += HandleLoadoutChanged;
            _load.OnActiveSlotChanged += HandleLoadoutChanged;
        }
        _stateProv = GetComponentInParent<PlayerStateProvider>(true);

        if (playerNetworkAnimator == null)
            playerNetworkAnimator = GetComponentInParent<PlayerNetworkAnimator>(true);

        StartCoroutine(BindCamerasWhenReady());

        IEnumerator BindCamerasWhenReady()
        {

            if (_refs != null)
                yield return new WaitUntil(() =>
                {
                    return _refs.cameraReady;
                });

            // Camera ngắm dùng MainCam thực
            if (_refs && _refs.mainCam) aimCamera = _refs.mainCam;

            // Driver FOV của VCam
            if (_refs && _refs.fovDriver) fovDriver = _refs.fovDriver;
        }

        if (aimCamera == null) aimCamera = Camera.main;
        if (viewCtrl == null) viewCtrl = GetComponent<WeaponViewController>();
        if (fireCtrl == null) fireCtrl = GetComponent<FireController>();
        /*        if (fireCtrl != null)
                {
                    // đảm bảo chỉ đăng ký 1 lần
                    fireCtrl.OnBulletFired -= HandleBulletFired;
                    fireCtrl.OnBulletFired += HandleBulletFired;
                }*/

        if (reloadCtrl == null) reloadCtrl = GetComponent<ReloadController>();

        if (recoil == null) recoil = GetComponent<RecoilController>();
        if (cameraDriver == null) cameraDriver = FindFirstObjectByType<CameraRecoilDriver>(FindObjectsInactive.Exclude);
        if (viewKickPivot == null) viewKickPivot = weaponViewRoot;
        if (sway == null) sway = GetComponentInChildren<WeaponSwayBob>(true);
        if (fovDriver == null) fovDriver = FindFirstObjectByType<CameraADSFOVDriver>(FindObjectsInactive.Exclude);

        fsm = new WeaponFSM();
    }

    /*    void OnDestroy()
        {
            if (fireCtrl != null)
                fireCtrl.OnBulletFired -= HandleBulletFired;
        }
    */

    void LateUpdate()
    {
        float dt = Time.deltaTime;

        if (recoil != null)
        {
            bool isADS = IsADS();
            bool isFiring = (fsm != null && fsm.State == WeaponState.Firing);
            recoil.Tick(dt, isFiring, isADS);

            if (sway != null)
                sway.SetIsFiring(isFiring);
        }

        // Cập nhật reload bob cho FP view
        UpdateReloadVisual(dt);
    }

    public void Equip(WeaponDef newDef, AmmoModule runtimeAmmo = null, bool fullMagFirst = true, int? initialReserve = null)
    {
        if (!IsLocalOwner)
        {
            Debug.Log($"[WeaponController.Equip] Skip on remote ({gameObject.name})");
            return;
        }

        fireCtrl?.Abort();                 // dừng burst/coro bắn :contentReference[oaicite:3]{index=3}
        recoil?.SnapToNeutralPose();       // 🔥 trả KickRig về neutral NGAY

        StopAllCoroutines();

        if (viewCtrl != null)
            viewCtrl.ResetReloadVisualImmediate();

        //     Debug.Log($"[WeaponController.Equip] START - Weapon: {newDef?.weaponName}, Object: {gameObject.name}");

        def = newDef;
        if (def == null)
        {
            Debug.Log("[WeaponController.Equip] No weapon def, unequipping");
            Unequip();
            return;
        }

        ammo = runtimeAmmo ?? new AmmoModule();
        if (runtimeAmmo == null) ammo.ResetFromDef(def, fullMagFirst, initialReserve);

        viewCtrl?.Apply(def, weaponViewRoot);
        //     Debug.Log($"[WeaponController.Equip] ViewCtrl.Apply called for: {def.weaponName}");
        viewCtrl?.RecacheBaseRootLocalPos();
        viewCtrl?.ResetReloadVisualImmediate();

        viewCtrl?.SetADSDesired(false);
        fireCtrl?.Reset(def);
        reloadCtrl?.ForceStop();


        if (fovDriver != null && def != null)
        {
            fovDriver.SetADSFOV(def.adsFOV);   // chỉ set ADS FOV
            fovDriver.Bind(viewCtrl);
            viewCtrl?.ResetADS();              // tuỳ UX, có thể giữ ADS ở bước 3
        }


        // --- Bind recoil profile từ WeaponDef ---
        if (recoil != null)
        {
            // nếu chưa set pivot, thử tìm trong view instance
            if (viewKickPivot == null && viewCtrl != null && viewCtrl.CurrentInstance != null)
            {
                var t = viewCtrl.CurrentInstance.transform.Find("ViewKickPivot");
                if (t != null) viewKickPivot = t;
            }

            // NOTE: thuộc tính profile của SO súng – bạn đang đặt là "RecoilProfile"
            recoil.Bind(def.RecoilProfile, cameraDriver, viewKickPivot);
            _lastShotTime = -999f;
        }

        var adsDriver = FindFirstObjectByType<WeaponADSAlignDriver>(FindObjectsInactive.Exclude);
        if (adsDriver != null)
        {
            // 1) áp offset HIP từ WeaponDef
            adsDriver.ApplyHipFromDef(def);

            // 2) bind lại ADS ref từ Sight_Aim như cũ
            if (viewCtrl != null && viewCtrl.CurrentInstance != null)
            {
                var aimRef = viewCtrl.CurrentInstance.transform.Find("Sight_Aim");
                if (aimRef != null)
                    adsDriver.BindADSRef(aimRef); // bind 1 phát tĩnh khi equip

            }
        }

        // Sau khi viewPrefab FP được spawn, áp lại tay FPS theo skin hiện tại
        var appearance = GetComponentInParent<PlayerAppearance>();
        if (appearance != null)
        {
            StartCoroutine(ApplyFPArmsNextFrame(appearance));
        }

        IEnumerator ApplyFPArmsNextFrame(PlayerAppearance a)
        {
            yield return null; // sang frame sau, prefab cũ chắc chắn đã Destroy xong
            a.ApplyCurrentFPArms();
        }


        fsm.OnEquipping();
        StartCoroutine(FinishEquipNextFrame());
        RaiseAmmoEvent();
    }

    IEnumerator FinishEquipNextFrame() { yield return null; fsm.OnEquippedIdle(); }

    public void Unequip()
    {
        if (!IsLocalOwner) return;

        if (_wasReloadingVisual)
        {
            OnReloadEnd?.Invoke();
            _wasReloadingVisual = false;
        }

        fireCtrl?.Abort();                 // dừng burst/coro bắn :contentReference[oaicite:3]{index=3}
        recoil?.SnapToNeutralPose();       // 🔥 trả KickRig về neutral NGAY

        StopAllCoroutines();
        fireCtrl?.Reset(null);
        reloadCtrl?.ForceStop();

        viewCtrl?.ResetReloadVisualImmediate();
        viewCtrl?.Clear(weaponViewRoot);

        def = null;
        ammo = null;

        if (recoil != null) { recoil.Bind(null, cameraDriver, viewKickPivot); recoil.ResetState(); }

        fovDriver?.Bind(null);
        viewCtrl?.ResetADS();

        // NEW: reset hip về base khi không cầm súng
        var adsDriver = FindFirstObjectByType<WeaponADSAlignDriver>(FindObjectsInactive.Exclude);
        if (adsDriver != null)
            adsDriver.ResetHipToBase();
    }


    public void PlayLocalShotFXImmediate()
    {
        // Muzzle flash + recoil (như cũ)
        viewCtrl?.PlayMuzzle();
        recoil?.AuthorizeShotThisFrame();
        recoil?.OnShot(IsADS(), 0f);
    }

    public void GetFireRay(out Vector3 origin, out Vector3 dir)
    {
        if (aimCamera != null)
        {
            origin = aimCamera.transform.position;
            dir = aimCamera.transform.forward;
            return;
        }

        if (viewCtrl != null && viewCtrl.Muzzle != null)
        {
            origin = viewCtrl.Muzzle.position;
            dir = viewCtrl.Muzzle.forward;
            return;
        }

        if (weaponViewRoot != null)
        {
            origin = weaponViewRoot.position;
            dir = weaponViewRoot.forward;
            return;
        }

        origin = transform.position + Vector3.up * 1.6f;
        dir = transform.forward;
    }

    public void Tick(bool fireHeld)
    {
        CanFireNow = true;

        if (IsLocalOwner)
        {
            var hub = InputHub.Instance;
            if (hub != null)
            {
                var snap = hub.GetSnapshotForTick();
                if (snap.Sprint)
                    CanFireNow = false;
            }
        }

        bool isADS = IsADS();

        if (def == null || ammo == null || fireCtrl == null || reloadCtrl == null)
        {
            return;
        }

        float rofMult = 1f;
        var perk = GetComponentInParent<TT.PerkManager>(true);
        if (perk != null) rofMult = perk.GetFireRateMultiplier();


        // ─────────────────────────────
        // CHẶN BẮN KHI ĐANG SPRINTING
        // ─────────────────────────────

        if (_stateProv != null && _stateProv.Movement != null)
        {
            // MovementStateId.Sprinting được map từ MoveState.Sprinting trong MovementStateSP/MP
            // => dùng được cho cả SP + MP thông qua PlayerStateProvider
            if (_stateProv.Movement.Current == MovementStateId.Sprinting)
            {
                // ép tắt cò bắn nếu đang chạy
                fireHeld = false;
            }
        }

        bool isReloading =
            (_load != null && _load.IsReloading) ||
            (reloadCtrl != null && reloadCtrl.IsReloading);

        if (reloadCtrl.IsReloading)
        {
            fsm.BeginReload();
            return;
        }

        bool cadenceReady = fireCtrl.CanFire(Time.time, def, rofMult);

        ReadAmmo(out var magCur, out var reserveCur, out var _);
        bool hasMag = magCur > 0;

        bool canShootNow = cadenceReady && hasMag;

        bool isFiringNow = fireHeld && canShootNow;

        // FSM biết được có thể bắn + đang giữ cò
        fsm.FireTick(canShootNow, fireHeld);

        // Đang giữ cò nhưng chưa bắn được (hết đạn / chưa hết cadence) -> thôi luôn
        if (fireHeld && !canShootNow)
            return;

        // Có thể bắn và đang giữ cò
        if (isFiringNow)
            TryFire();
    }


    public void TryReload()
    {
        if (def == null) return;

        // chỉ đổi desired, KHÔNG reset cứng weight.
        if (IsADS())
        {
            // Nếu bạn có FSM ADS thì có thể báo cho nó ở đây
            // fsm?.SetADS(false);   // (nếu WeaponFSM có hàm này)
            if (viewCtrl != null)
                viewCtrl.SetADSDesired(false);
        }

        //   Debug.Log($"[Reload] TryReload() CALLED — slot={_slotIndex}, loadIsReloading={_load?.IsReloading}, ctrlReloading={reloadCtrl.IsReloading}, ADS={IsADS()}");

        // 🔹 Reload qua Loadout (SP + MP), không dính audio / timeline
        if (_load != null)
        {
            if (_load.TryStartReloadOnActive(out var defStarted))
            {
                // SP: PlayerLoadoutStateSP sẽ tự CompleteReload trong Update()
                // MP: PlayerLoadoutStateMP dùng TickTimer + FusionNetBridge để Complete.
                // Ở đây KHÔNG còn audio, KHÔNG còn timeline.
                return;
            }
        }

        // 🔹 Fallback legacy (chỉ dùng khi chưa có Loadout – gần như không dùng nữa)
        if (_load == null && ammo != null && reloadCtrl.CanReload(ammo))
        {
            StartCoroutine(ReloadRoutine());   // Reload thuần đạn, không audio
        }
    }

    void TryFire()
    {
        if (def == null || ammo == null) return;

        if (_load != null && _load.IsReloading)
            return;

        float rofMult = 1f;
        var perk = GetComponentInParent<TT.PerkManager>();
        if (perk != null) rofMult = perk.GetFireRateMultiplier();

        if (!fireCtrl.CanFire(Time.time, def, rofMult)) return;

        // Nếu mag trống
        ReadAmmo(out var magCur, out var reserveCur, out var _);
        if (magCur <= 0)
        {
            if (reserveCur > 0) { TryReload(); }
            // Không đổi ADS ở đây để tránh nháy
            return;
        }

        // 🔧 PATCH — Lấy movement từ PlayerStateProvider
        bool isMoving = false;
        bool isAir = false;
        bool isCrouch = false;

        if (_stateProv != null && _stateProv.Movement != null)
        {
            var mv = _stateProv.Movement;

            isMoving =
                mv.Current == MovementStateId.Walking ||
                mv.Current == MovementStateId.Sprinting;

            isAir =
                mv.Current == MovementStateId.Jumping ||
                mv.Current == MovementStateId.Falling;

            isCrouch =
                mv.Current == MovementStateId.Crouching;
        }


        bool isADS = IsADS();
        var ctx = new WeaponContext
        {
            wc = this,
            def = def,
            aimCam = aimCamera,
            muzzle = viewCtrl != null ? viewCtrl.Muzzle : null,
            hitMask = def.hitMask,
            OnShotFX = () => viewCtrl?.PlayMuzzle(),
            isADS = isADS,

            // 🔧 PATCH — movement flags
            isMoving = isMoving,
            isAirborne = isAir,
            isCrouching = isCrouch,

            // 🔧 PATCH — truyền spread theo movement
            RequestHipfireSpreadDeg = () =>
            {
                return recoil.GetHipfireSpreadDeg(isMoving, isAir, isCrouch);
            }
        };

        // === SP commit: nếu không có network authority thì tự trừ đạn ở state ===
        bool isSP = (_net == null || _net.Object == null || !_net.Object.IsValid);

        if (isSP && _load != null)
        {
            // Nếu WeaponController có _slotIndex của khẩu này thì tạm ép ActiveSlot về nó
            /*            int desired = (_slotIndex >= 0) ? _slotIndex : _load.ActiveSlot;
                        if (_load.ActiveSlot != desired)
                            _load.SelectActiveSlot(desired);*/

            // Trừ đạn tại Loadout → phát sinh Notify (log)
            if (!_load.TryConsumeOneOnActive(out var def)) return; // KHÔNG abort, KHÔNG đổi ADS

            // Sau khi tiêu đạn thành công mới nổ súng (FX/đạn bay)
            fireCtrl.Fire(
     ref ctx,
     ammo,
     def,
     onAmmoChanged: RaiseAmmoEvent,

         onCommittedShot: () =>
         {
             if (recoil != null)
             {
                 float dtSince = (_lastShotTime < 0f) ? 999f : Time.time - _lastShotTime;
                 recoil.AuthorizeShotThisFrame();
                 recoil.OnShot(isADS, dtSince);
             }

             if (playerNetworkAnimator != null)
             {
                 playerNetworkAnimator.NotifyShotFired();
             }

             _lastShotTime = Time.time;

             OnShot?.Invoke();
             fsm.NotifyFiredOnce();

             int slotForLog = (_load != null)
    ? ((_slotIndex >= 0) ? _slotIndex : _load.ActiveSlot)
    : -1;

             ReadAmmo(out var magNow, out var reserveNow, out _);

             TT.Observer.Instance?.NotifyWithData(
                 TT.WeaponTopics.Fired,
                 (owner ? owner.gameObject : gameObject, slotForLog, magNow, reserveNow));
         },

         rofMult: rofMult
 );
        }
        else
        {
            // MP: giữ nguyên (Bridge/Authority đã đảm bảo trừ đạn & log)
            fireCtrl.Fire(
     ref ctx,
     ammo,
     def,
     onAmmoChanged: RaiseAmmoEvent,

         onCommittedShot: () =>
         {
             if (recoil != null)
             {
                 float dtSince = (_lastShotTime < 0f) ? 999f : Time.time - _lastShotTime;
                 recoil.AuthorizeShotThisFrame();
                 recoil.OnShot(isADS, dtSince);
             }

             if (playerNetworkAnimator != null)
             {
                 playerNetworkAnimator.NotifyShotFired();
             }

             _lastShotTime = Time.time;

             OnShot?.Invoke();
             fsm.NotifyFiredOnce();

             int slotForLog = (_load != null)
    ? ((_slotIndex >= 0) ? _slotIndex : _load.ActiveSlot)
    : -1;

             ReadAmmo(out var magNow, out var reserveNow, out _);

             TT.Observer.Instance?.NotifyWithData(
                 TT.WeaponTopics.Fired,
                 (owner ? owner.gameObject : gameObject, slotForLog, magNow, reserveNow));

         },
         rofMult: rofMult
 );

        }

        if (_firingWatcher != null) StopCoroutine(_firingWatcher);
        _firingWatcher = StartCoroutine(WatchFiringWindow(rofMult));
    }

    void HandleLoadoutChanged(int _)
    {
        // Cập nhật HUD đạn
        RaiseAmmoEvent();

        if (ammo != null)
        {
            ReadAmmo(out var mag, out var reserve, out var magSize);
            ammo.SetCounts(mag, reserve, magSize); // giữ AmmoModule khớp Loadout
        }

        // Nếu vừa kết thúc reload -> chắc chắn bỏ trạng thái bắn/burst
        bool now = _load != null && _load.IsReloading;
        if (_wasReloading && !now)
            //  fireCtrl?.Abort(); // đủ để clear coroutine & busy

            _wasReloading = now;
    }

    IEnumerator WatchFiringWindow(float rofMult)
    {
        while (fireCtrl != null && (fireCtrl.IsBusy || !fireCtrl.CanFire(Time.time, def, rofMult)))
            yield return null;

        _firingWatcher = null;
    }

    IEnumerator ReloadRoutine()
    {
        fsm.BeginReload();
        OnReloadStart?.Invoke();

        float reloadMult = 1f;
        var perk = GetComponentInParent<TT.PerkManager>(true);
        if (perk != null) reloadMult = perk.GetReloadDurationMultiplier();

        float t = (def != null ? def.reloadTime : 2f) * reloadMult;
        reloadCtrl.Reload(ammo, Mathf.Max(0.01f, t), _ => { });

        while (reloadCtrl.IsReloading) yield return null;

        RaiseAmmoEvent();
        OnReloadEnd?.Invoke();
        fsm.EndReload();

        //    _isReloadTimelineRunning = false;
    }

    void RaiseAmmoEvent()
    {
        if (AmmoChanged == null || def == null) return;

        int mag, reserve, magSize;
        ReadAmmo(out mag, out reserve, out magSize);

        AmmoChanged.Invoke(new AmmoChangedArgs(
            def.weaponId, _runtimeGuid, _slotIndex, mag, reserve, magSize
        ));
    }


    void ReadAmmo(out int mag, out int reserve, out int magSize)
    {
        if (_load != null)
        {
            int slotToRead = (_slotIndex >= 0) ? _slotIndex : _load.ActiveSlot; // 🔧 dùng slotIndex nếu đã biết
            var s = _load.GetSlot(slotToRead);
            var wdef = (s.weaponKey != 0) ? WeaponIdRegistry.GetDef(s.weaponKey) : null;
            mag = s.mag;
            reserve = s.reserve;
            magSize = wdef != null ? wdef.magSize : (ammo != null ? ammo.magSize : 0);
        }
        else
        {
            // SP fallback: giữ hành vi cũ
            mag = ammo != null ? ammo.mag : 0;
            reserve = ammo != null ? ammo.reserve : 0;
            magSize = ammo != null ? ammo.magSize : 0;
        }
    }

    public void SetRuntimeIdentity(string guid, int slot) { _runtimeGuid = guid; _slotIndex = slot; }

    public AmmoModule GetCurrentAmmoRuntime() => ammo;

    public void RaiseAmmoChanged(string weaponId, string runtimeGuid, int slotIndex, AmmoModule ammoRef, int magSize)
    {
        AmmoChanged?.Invoke(new AmmoChangedArgs(
            weaponId, runtimeGuid, slotIndex, ammoRef?.mag ?? 0, ammoRef?.reserve ?? 0, magSize));
    }

    public void EquipFromDef(WeaponDef newDef)
    {
        if (newDef == null) { Unequip(); return; }
        Equip(newDef, null, fullMagFirst: true, initialReserve: null);
    }

    public void Equip(WeaponDef newDef, AmmoModule runtimeAmmo, bool initIfNew)
    {
        Equip(newDef, runtimeAmmo, fullMagFirst: initIfNew, initialReserve: null);
    }

    // ========== ADS ==========
    void EnsureFSM() { if (fsm == null) fsm = new WeaponFSM(); }

    public void SetADS(bool ads)
    {
        EnsureFSM();

        if (def == null)
        {
            // Không có súng thì luôn coi như hipfire
            fsm.SetADS(false);
            viewCtrl?.SetADSDesired(false);
            return;
        }

        // 👉 Đang reload thì không được ADS
        bool reloading =
            (_load != null && _load.IsReloading) ||
            (reloadCtrl != null && reloadCtrl.IsReloading);

        if (reloading && ads)
            ads = false;

        fsm.SetADS(ads);
        viewCtrl?.SetADSDesired(ads);

        if (viewCtrl != null && viewCtrl.CurrentInstance != null)
        {
            // Vẫn giữ logic rebind sight khi vào ADS
            var aimRef = viewCtrl.CurrentInstance.transform.Find("Sight_Aim");
            if (aimRef != null)
                StartCoroutine(RebindSightAimStabilized(aimRef));
        }
    }



    public bool IsADS() => fsm != null && fsm.IsADS;
    public WeaponState GetState() => fsm != null ? fsm.State : WeaponState.None;

    IEnumerator RebindSightAimStabilized(Transform aimRef)
    {
        if (aimRef == null) yield break;
        var adsDriver = FindFirstObjectByType<WeaponADSAlignDriver>(FindObjectsInactive.Exclude);
        if (adsDriver == null) yield break;

        // 1) gọi ngay (để có target tạm)
        adsDriver.BindADSRef(aimRef);

        // 2) đợi tới cuối frame (CM đổi FOV/pose xong)
        yield return new WaitForEndOfFrame();

        // 3) gọi lại một lần
        adsDriver.BindADSRef(aimRef);

        // 4) OPTIONAL: đợi thêm 1 LateUpdate rồi chốt lần nữa cho chắc
        yield return null;
        adsDriver.BindADSRef(aimRef);
    }

    private void UpdateReloadVisual(float dt)
    {
        if (viewCtrl == null) return;
        if (!IsLocalOwner) return;   // chỉ local FP mới cần

        bool usesLoadout = (_load != null);

        bool isReloading =
            (usesLoadout && _load.IsReloading) ||
            (!usesLoadout && reloadCtrl != null && reloadCtrl.IsReloading);

        // Bắt đầu reload → reset timer & snap progress về 0
        if (isReloading && !_wasReloadingVisual)
        {
            _reloadVisualTime = 0f;

            float reloadMult = 1f;
            var perk = GetComponentInParent<TT.PerkManager>(true);
            if (perk != null) reloadMult = perk.GetReloadDurationMultiplier();

            float baseDur = (def != null && def.reloadTime > 0f) ? def.reloadTime : 1.0f;
            _reloadVisualDuration = Mathf.Max(0.01f, baseDur * reloadMult);


            // Đảm bảo progress = 0, không đi từ 1 → 0 gây double bob
            viewCtrl.ResetReloadVisualImmediate();
            viewCtrl.SetReloadProgress(0f);

            // Gửi event audio reload cho path dùng Loadout (SP + MP)
            if (usesLoadout)
                OnReloadStart?.Invoke();
        }

        if (isReloading)
        {
            _reloadVisualTime += dt;

            float t;
            if (_reloadVisualDuration > 0f)
                t = Mathf.Clamp01(_reloadVisualTime / _reloadVisualDuration);
            else
                t = 1f;

            // gửi progress sang view (0→1)
            viewCtrl.SetReloadProgress(t);
        }
        else
        {
            // vừa kết thúc / vừa bị hủy reload (ví dụ đổi súng giữa chừng)
            if (_wasReloadingVisual)
            {
                // Gửi event audio cho path dùng Loadout
                if (usesLoadout)
                    OnReloadEnd?.Invoke();

                // Snap về base
                viewCtrl.ResetReloadVisualImmediate();
                _reloadVisualTime = 0f;
                _reloadVisualDuration = 0f;
            }
        }

        _wasReloadingVisual = isReloading;
    }

    public void NotifyLocalShotPredicted()
    {
        OnShot?.Invoke();
    }

    public bool IsReloadingNow
    {
        get
        {
            // Ưu tiên Loadout (SP/MP thống nhất), fallback reloadCtrl
            bool loadReload = (_load != null && _load.IsReloading);
            bool ctrlReload = (reloadCtrl != null && reloadCtrl.IsReloading);
            return loadReload || ctrlReload;
        }
    }
}