using UnityEngine;
using TT;
using Fusion;

[DisallowMultipleComponent]
[RequireComponent(typeof(WeaponController))]
public class WeaponAudioDriver : MonoBehaviour
{
    [Header("Bindings")]
    [SerializeField] private WeaponController weapon;
    [SerializeField] private WeaponViewController viewCtrl;

    [Tooltip("Nếu set thì dùng transform này làm emitter cho tiếng bắn.")]
    [SerializeField] private Transform fireEmitterOverride;

    [Tooltip("Nếu set thì dùng transform này làm emitter cho tiếng reload. Nếu để trống sẽ dùng cùng emitter với fire.")]
    [SerializeField] private Transform reloadEmitterOverride;

    private FusionNetBridge _net;
    private AudioHandle _reloadHandle;

    private AudioManager _am;


    void Awake()
    {
        _am = AudioManager.Instance; // cache 1 lần (lúc game còn “sống”)

        if (!weapon)
            weapon = GetComponent<WeaponController>();

        _net = GetComponentInParent<FusionNetBridge>(true);

        // Thử lấy viewCtrl trên cùng GO
        if (!viewCtrl)
            viewCtrl = GetComponent<WeaponViewController>();

        // Nếu vẫn chưa có: bind từ CameraBinder cho player local
        if (!viewCtrl && CameraBinder.Instance != null)
        {
            // SP (_net == null) hoặc player local trong MP
            if (_net == null || _net.IsLocalOwner)
                viewCtrl = CameraBinder.Instance.viewCtrl;
        }

        if (weapon != null)
        {
            weapon.OnShot += HandleShot;
            weapon.OnReloadStart += HandleReloadStart;
            weapon.OnReloadEnd += HandleReloadEnd;
        }
    }

    void OnDestroy()
    {
        if (weapon != null)
        {
            weapon.OnShot -= HandleShot;
            weapon.OnReloadStart -= HandleReloadStart;
            weapon.OnReloadEnd -= HandleReloadEnd;
        }

        StopReloadSoundImmediate();
    }

    void OnDisable()
    {
        // Khi đổi súng / vứt súng object thường bị disable → dừng reload sound
        StopReloadSoundImmediate();
    }

    // ================== EMITTER HELPER ==================

    Transform GetFireEmitter()
    {
        if (fireEmitterOverride) return fireEmitterOverride;

        // Nếu có viewCtrl (FP local) thì ưu tiên muzzle
        if (viewCtrl != null)
        {
            if (viewCtrl.Muzzle != null)
                return viewCtrl.Muzzle;

            if (viewCtrl.CurrentInstance != null)
                return viewCtrl.CurrentInstance.transform;
        }

        if (weapon != null)
            return weapon.transform;

        return transform;
    }

    Transform GetReloadEmitter()
    {
        if (reloadEmitterOverride) return reloadEmitterOverride;
        return GetFireEmitter();
    }

    // ================== EVENT HANDLERS ==================

    // BẮN ĐẠN
    private void HandleShot()
    {
        if (!weapon || weapon.def == null)
            return;

        var def = weapon.def;
        if (def.fireAudio == null)
            return;

        var emitter = GetFireEmitter();
        if (!emitter)
            return;

    //    Debug.Log("[WeaponAudioDriver] HandleShot");

        // MP: chỉ local owner mới được phát local gunshot
        if (_net != null && _net.Object != null && _net.Object.IsValid)
        {
            if (!_net.IsLocalOwner) // không phải thằng sở hữu input => không play local
                return;
        }


        if (_am != null)
            _am.Play3DAttached(def.fireAudio.eventId, emitter);
    }


    // BẮT ĐẦU RELOAD
    void HandleReloadStart()
    {
        if (weapon == null || weapon.def == null) return;
        var def = weapon.def;
        if (def.reloadAudio == null) return;
        if (AudioManager.Instance == null) return;

        Transform emitter = GetReloadEmitter();
        if (!emitter) return;

        // Nếu đang phát reload cũ (spam reload / cancel rồi reload lại) → dừng cái cũ
        StopReloadSoundImmediate();

        if (_am == null) return;
        _reloadHandle = _am.Play3DAttachedHandle(def.reloadAudio.eventId, emitter);
    }

    // KẾT THÚC / HỦY RELOAD
    void HandleReloadEnd()
    {
        StopReloadSoundSmooth();
    }

    // ================== STOP HELPERS ==================

    private void StopReloadSoundSmooth()
    {
        if (!_reloadHandle.IsValid)
            return;

        if (_am == null)
        {
            _reloadHandle = default;
            return;
        }

        _am.FadeOutAndStop(_reloadHandle, 0.05f); // duration tuỳ bạn
        _reloadHandle = default;
    }


    private void StopReloadSoundImmediate()
    {
        if (!_reloadHandle.IsValid)
            return;

        // Không được gọi AudioManager.Instance ở đây nữa
        if (_am == null)
        {
            _reloadHandle = default;
            return;
        }

        _am.Stop(_reloadHandle);          // hoặc _am.StopHandle(...) tuỳ API bạn
        _reloadHandle = default;
    }

}
