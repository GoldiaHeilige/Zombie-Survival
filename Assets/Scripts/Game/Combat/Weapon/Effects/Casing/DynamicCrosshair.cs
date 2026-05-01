using UnityEngine;
using UnityEngine.UI;

public class DynamicCrosshair : MonoBehaviour
{
    [Header("Refs")]
    public WeaponController weapon;         // kéo WeaponController local vào
    public PlayerStateProvider stateProvider;
    public Camera aimCamera;               // main cam

    [Header("UI Parts")]
    public RectTransform left;
    public RectTransform right;
    public RectTransform top;
    public RectTransform bottom;
    public Graphic[] graphicsToToggle;

    [Header("Tuning (pixels)")]
    public float baseGapPx = 6f;
    public float maxGapPx = 80f;

    [Header("Bloom (shot kick)")]
    public float bloomKickPx = 10f;
    public float bloomMaxPx = 40f;
    public float bloomReturnSpeed = 16f;

    [Header("Shot Impulse (visual only)")]
    public float shotImpulsePx = 6f;      // độ nảy mỗi viên
    public float shotImpulseReturn = 40f; // tốc độ hồi

    float _shotSnapTimer;
    public float shotSnapTime = 0.06f; // 60ms

    float _shotImpulse;
    float _shotImpulseReturn;

    [Header("Smoothing")]
    public float gapLerpSpeed = 18f;

    WeaponController _subscribedWeapon;

    float _bloom;
    float _gapCurrent;

    void OnEnable()
    {
        EnsureSubscribed();
    }

    void OnDisable()
    {
        if (_subscribedWeapon != null)
            _subscribedWeapon.OnShot -= HandleShot;

        _subscribedWeapon = null;
    }

    void HandleShot()
    {
        if (weapon != null && weapon.IsADS()) return;

        GetShotImpulseFromWeapon(out float kick, out float ret);

        // overwrite, không cộng dồn
        _shotImpulse = kick;
        _shotImpulseReturn = ret;
        _shotSnapTimer = shotSnapTime;
    }

    void Update()
    {
        if (weapon == null || aimCamera == null)
            return;

        EnsureSubscribed();

        bool ads = weapon.IsADS(); // :contentReference[oaicite:3]{index=3}
        SetVisible(!ads);

        if (ads)
        {
            _bloom = 0f;
            _gapCurrent = 0f;
            ApplyGap(0f);
            return;
        }

        _shotImpulse = Mathf.MoveTowards(_shotImpulse, 0f, _shotImpulseReturn * Time.deltaTime);


        // movement flags từ enum Current :contentReference[oaicite:4]{index=4}
        var movementState = stateProvider.Movement; // IMovementState
        if (movementState == null) return;

        // Nếu Movement là MP network state -> phải chắc chắn nó còn Spawned/Valid
#if FUSION_WEAVER
        if (movementState is PlayerMovementStateMP mmp)
        {
            if (mmp.Object == null || !mmp.Object.IsValid || mmp.Runner == null || !mmp.Runner.IsRunning)
                return;
        }
#endif

        var st = movementState.Current; // MovementStateId 
        bool isMoving = (st == MovementStateId.Walking || st == MovementStateId.Sprinting);
        bool isAir = (st == MovementStateId.Jumping || st == MovementStateId.Falling);
        bool isCrouch = (st == MovementStateId.Crouching);

        // weapon def là weapon.def :contentReference[oaicite:5]{index=5}
        var def = weapon.def;
        float spreadDeg = 0f;

        if (def != null && def.RecoilProfile != null)
        {
            var yawPitch = def.RecoilProfile.GetHipfireSpreadDeg(isMoving, isAir, isCrouch); // :contentReference[oaicite:6]{index=6}
            spreadDeg = Mathf.Max(yawPitch.x, yawPitch.y);
        }

        float spreadPx = DegreesToPixels(spreadDeg, aimCamera);

        float targetGap = Mathf.Clamp(baseGapPx + spreadPx + _shotImpulse, 0f, maxGapPx);

        if (_shotSnapTimer > 0f)
        {
            _shotSnapTimer -= Time.deltaTime;

            // ✅ snap để thấy "tạch" rõ ràng
            _gapCurrent = targetGap;
        }
        else
        {
            // ✅ smooth lại cho movement
            _gapCurrent = Mathf.Lerp(_gapCurrent, targetGap, 1f - Mathf.Exp(-gapLerpSpeed * Time.deltaTime));
        }

        ApplyGap(_gapCurrent);
    }

    float DegreesToPixels(float degrees, Camera cam)
    {
        float theta = Mathf.Deg2Rad * Mathf.Max(0f, degrees);
        float focalPx = (Screen.height * 0.5f) / Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * 0.5f);
        return Mathf.Tan(theta) * focalPx;
    }

    void ApplyGap(float gapPx)
    {
        if (left) left.anchoredPosition = new Vector2(-gapPx, 0f);
        if (right) right.anchoredPosition = new Vector2(gapPx, 0f);
        if (top) top.anchoredPosition = new Vector2(0f, gapPx);
        if (bottom) bottom.anchoredPosition = new Vector2(0f, -gapPx);
    }

    void SetVisible(bool v)
    {
        if (graphicsToToggle == null) return;
        for (int i = 0; i < graphicsToToggle.Length; i++)
            if (graphicsToToggle[i]) graphicsToToggle[i].enabled = v;
    }

    void GetShotImpulseFromWeapon(out float kickPx, out float returnSpeed)
    {
        // default fallback
        kickPx = shotImpulsePx;
        returnSpeed = shotImpulseReturn;

        var def = weapon != null ? weapon.def : null;
        if (def == null) return;

        // RPM factor: rpm càng cao => kick nhỏ hơn, return nhanh hơn
        float rpm01 = Mathf.InverseLerp(300f, 900f, def.rpm);            // 0..1
        float rpmKickMul = Mathf.Lerp(1.15f, 0.75f, rpm01);              // 300rpm -> 1.15, 900rpm -> 0.75
        float rpmReturnMul = Mathf.Lerp(0.85f, 1.30f, rpm01);            // 300rpm -> chậm hơn, 900rpm -> nhanh hơn

        // Pellet factor: shotgun/pellets => kick mạnh hơn, return chậm hơn
        int pellets = Mathf.Max(1, def.pelletCount);
        float pellet01 = Mathf.InverseLerp(1f, 12f, pellets);
        float pelletKickMul = Mathf.Lerp(1.0f, 1.85f, pellet01);         // 1 pellet -> 1.0, 12 pellets -> ~1.85
        float pelletReturnMul = Mathf.Lerp(1.0f, 0.75f, pellet01);       // nhiều pellet -> hồi chậm hơn

        kickPx = shotImpulsePx * rpmKickMul * pelletKickMul;
        returnSpeed = shotImpulseReturn * rpmReturnMul * pelletReturnMul;
    }

    void EnsureSubscribed()
    {
        if (weapon == _subscribedWeapon) return;

        if (_subscribedWeapon != null)
            _subscribedWeapon.OnShot -= HandleShot;

        _subscribedWeapon = weapon;

        if (_subscribedWeapon != null)
            _subscribedWeapon.OnShot += HandleShot;
    }
}
