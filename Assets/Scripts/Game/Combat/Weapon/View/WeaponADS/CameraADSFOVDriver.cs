using UnityEngine;
using Unity.Cinemachine;
using System.Collections;

[DisallowMultipleComponent]
public class CameraADSFOVDriver : MonoBehaviour
{
    [Header("Provider (đọc ADSWeight)")]
    [SerializeField] private WeaponViewController provider;

    [Header("Targets")]
    [Tooltip("Cinemachine 3: CinemachineCamera (không còn VirtualCamera).")]
    [SerializeField] private CinemachineCamera cmCamera;
    [SerializeField] private Camera weaponOverlayCam;


    [Header("FOV Params")]
    [Tooltip("FOV khi hipfire (base). Nếu =0 sẽ lấy từ camera lúc Awake.")]
    [SerializeField] private float baseFOV = 0f;

    [Tooltip("FOV khi ADS (lấy từ WeaponDef).")]
    [SerializeField] private float adsFOV = 60f;

    [Tooltip("Clamp FOV tránh giá trị bất thường.")]
    [SerializeField] private Vector2 clampRange = new Vector2(30f, 120f);

    [Header("Options")]
    [Tooltip("Chạy ở LateUpdate để mượt với các driver khác.")]
    [SerializeField] private bool runInLateUpdate = true;

    PlayerRefs _refs;

    /// <summary>Cho WeaponController gọi khi Equip để set base/ads FOV theo từng súng.</summary>
    public void SetADSFOV(float adsFov)
    {
        adsFOV = Mathf.Clamp(adsFov, clampRange.x, clampRange.y);
    }

    void Awake()
    {

        if (provider == null)
            provider = FindFirstObjectByType<WeaponViewController>(FindObjectsInactive.Exclude);

        if (baseFOV <= 0.0001f && cmCamera != null)
            baseFOV = Mathf.Clamp(cmCamera.Lens.FieldOfView, clampRange.x, clampRange.y);
    }


    void Update()
    {
        if (!runInLateUpdate) Drive();
    }

    void LateUpdate()
    {
        if (runInLateUpdate) Drive();
    }

    void Drive()
    {
        float w = CurrentWeight();
        float targetFov = Mathf.Clamp(Mathf.Lerp(baseFOV, adsFOV, w), clampRange.x, clampRange.y);

        // 1) CM (Main)
        if (cmCamera != null)
        {
            var lens = cmCamera.Lens;
            if (!Mathf.Approximately(lens.FieldOfView, targetFov))
            {
                lens.FieldOfView = targetFov;
                cmCamera.Lens = lens;
            }
        }

        // 2) Overlay (Weapon)
        if (weaponOverlayCam != null && !Mathf.Approximately(weaponOverlayCam.fieldOfView, targetFov))
            weaponOverlayCam.fieldOfView = targetFov;
    }

    float CurrentWeight()
    {
        if (!provider || !provider.isActiveAndEnabled) return 0f;
        return Mathf.Clamp01(provider.ADSWeight);
    }

    void OnDisable()
    {
        if (cmCamera)
        {
            var lens = cmCamera.Lens;
            lens.FieldOfView = Mathf.Clamp(baseFOV, clampRange.x, clampRange.y);
            cmCamera.Lens = lens;
        }
        if (weaponOverlayCam)
            weaponOverlayCam.fieldOfView = Mathf.Clamp(baseFOV, clampRange.x, clampRange.y);
    }

    // Cho WeaponController gọi khi equip/unequip
    public void Bind(WeaponViewController newProvider)
    {
        provider = newProvider;
        // nếu bind null thì về base ngay
        if (!provider && cmCamera)
        {
            var lens = cmCamera.Lens;
            lens.FieldOfView = Mathf.Clamp(baseFOV, clampRange.x, clampRange.y);
            cmCamera.Lens = lens;
        }
    }
}