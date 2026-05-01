using System;
using System.Reflection;
using UnityEngine;
using Unity.Cinemachine;

public class CameraBinder : MonoBehaviour
{
    public static CameraBinder Instance { get; private set; }

    [Header("Scene Cameras / VCams")]
    public Camera mainCam;                  // Main Camera trong scene
    public Camera weaponCam;                // Camera render FPS overlay
    public CinemachineCamera vcam;          // VCam FPS
    public CameraADSFOVDriver fovDriver;    // (optional) driver FOV theo ADS

    [Header("Spectator VCam (Downed / Spec)")]
    public CinemachineCamera spectatorVcam;

    [Header("Spectator Orbit Root")]
    public Transform spectatorOrbitRoot;

    [Header("Scene Weapon View Rig")]
    public Transform weaponRoot;            // gốc view rig (chứa các controller)
    public Transform weaponModel;           // object hiển thị súng FPS
    public Transform weaponKickPivot;       // pivot kick
    public WeaponViewController viewCtrl;   // controller điều khiển view/sight/ads
    public Transform sceneWeaponCamAnchor;

    [Header("Scene Sway Rig")]
    public WeaponSwayBob sway;              // component sway/bob thực

    [Header("Auto-find if empty")]
    public bool autoFindIfNull = true;

    // ======================================================================

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CameraBinder] Multiple instances, dùng instance cũ.", this);
            return;
        }
        Instance = this;
    }


    public void OnPlayerSpawned(PlayerRefs refs)
    {
        // 0) Auto-find các tham chiếu scene nếu đang để trống
        if (autoFindIfNull)
            AutoFindSceneRefs();

        if (!refs)
        {
            Debug.LogWarning("[CameraBinder] PlayerRefs null, skip bind.");
            return;
        }

        // 1) Gán VCam follow/lookAt theo local player
        if (vcam)
        {
            vcam.Follow = refs.camFollowTarget;
     //       vcam.LookAt = refs.camFollowTarget;
        }

/*        if (refs.weaponCamAnchor == null && sceneWeaponCamAnchor != null)
            refs.weaponCamAnchor = sceneWeaponCamAnchor;*/

        // 2) Căn weapon-cam bám theo main-cam & anchor của player
        if (weaponCam)
        {
            // đặt weaponCam về anchor (nếu có)
 /*           if (refs.weaponCamAnchor)
            {
                weaponCam.transform.SetPositionAndRotation(
                    refs.weaponCamAnchor.position, refs.weaponCamAnchor.rotation);
                weaponCam.transform.SetParent(refs.weaponCamAnchor, worldPositionStays: true);
            }*/

            // đảm bảo weaponCam copy pose từ mainCam mỗi frame (không parent nhầm)
            var copy = weaponCam.GetComponent<CopyMainCamPose>();
            if (!copy) copy = weaponCam.gameObject.AddComponent<CopyMainCamPose>();
            copy.Init(mainCam);
        }

        // 3) Điền runtime refs về player
        refs.mainCam = mainCam;
        refs.weaponCam = weaponCam;
        refs.fovDriver = fovDriver;

        // 4) Bind các BRIDGE trong scene tới sway / provider
        var swayLookBridge = refs.GetComponentInChildren<SwayLookDeltaBridge>(true);
        if (swayLookBridge) swayLookBridge.sway = sway;

        var adsWeightBridge = refs.GetComponentInChildren<ADSWeightBridge>(true);
        if (adsWeightBridge)
        {
            adsWeightBridge.sway = sway;
            if (!adsWeightBridge.provider && viewCtrl) adsWeightBridge.provider = viewCtrl;
        }

        var movementCtrl = refs.GetComponentInChildren<PlayerMovementController>(true);
        if (movementCtrl != null)
        {
            // SwayMovementBridge nằm trên camera rig trong scene
            var swayMoveBridge = FindFirstObjectByType<SwayMovementBridge>(FindObjectsInactive.Exclude);
            if (swayMoveBridge != null)
            {
                if (sway != null)
                    swayMoveBridge.sway = sway;      // sway rig scene
                swayMoveBridge.BindPlayer(movementCtrl);
            }
            else
            {
                Debug.LogWarning("[CameraBinder] Không tìm thấy SwayMovementBridge trong scene để bind movement.");
            }
        }

        // 5) Bind view rig trong scene cho WeaponController ở player
        var wpn = refs.GetComponentInChildren<WeaponController>(true);
        if (wpn)
        {
            // a) aimCamera = mainCam
            SetPrivateFieldIfExists(wpn, "aimCamera", mainCam);

            // b) weaponViewRoot = weaponModel (object chứa model FPS)
            if (weaponModel)
                SetPrivateFieldIfExists(wpn, "weaponViewRoot", weaponModel);

            // c) viewCtrl & recoilCtrl
            if (viewCtrl)
                SetPrivateFieldIfExists(wpn, "viewCtrl", viewCtrl);

            if (sway) SetPrivateFieldIfExists(wpn, "sway", sway);

            // d) ViewKick pivot: field có thể đặt tên khác nhau tuỳ version => thử nhiều key
            if (weaponKickPivot)
            {
                TrySetFieldMultiNames(viewCtrl, new[] { "viewKickPivot", "kickPivot", "kickRoot" }, weaponKickPivot);
                TrySetFieldMultiNames(wpn, new[] { "viewKickPivot", "kickPivot", "kickRoot" }, weaponKickPivot);
            }
        }
        else
        {
            Debug.LogWarning("[CameraBinder] Không tìm thấy WeaponController trong player để bind view rig.");
        }

        var appearance = refs.GetComponentInChildren<PlayerAppearance>(true);
        var fpArmView = FindFirstObjectByType<PlayerFPArmView>(FindObjectsInactive.Exclude);
        if (appearance != null && fpArmView != null)
        {
            appearance.SetLocalFPArmView(fpArmView);
            appearance.ApplyCurrentFPArms();
        }
        else
        {
            if (appearance == null)
                Debug.LogWarning("[CameraBinder] Không tìm thấy PlayerAppearance trên player local: " + refs.gameObject.name);
            if (fpArmView == null)
                Debug.LogWarning("[CameraBinder] Không tìm thấy PlayerFPArmView trong scene.");
        }

        // 6) Đánh dấu cameraReady để các script khác đợi xong rồi mới chạy
        refs.cameraReady = true;
        //    Debug.Log("[CameraBinder] Bind hoàn tất cho player: " + refs.gameObject.name);

        // 7) Bind DynamicCrosshair (HIPFIRE only)
        var cross = FindFirstObjectByType<DynamicCrosshair>(FindObjectsInactive.Include);
        if (cross != null)
        {
            // weapon local
            var wpnn = refs.GetComponentInChildren<WeaponController>(true);
            if (wpnn) cross.weapon = wpnn;

            // provider local (tự chọn SP/MP Movement)
            var provider = refs.GetComponent<PlayerStateProvider>();
            if (provider) cross.stateProvider = provider;

            // camera
            cross.aimCamera = mainCam;

            // (optional) nếu mày muốn crosshair đợi cameraReady:
            // cross.enabled = refs.cameraReady;
        }
        else
        {
            Debug.LogWarning("[CameraBinder] Không tìm thấy DynamicCrosshair trong scene.");
        }

    }

    // ======================================================================
    #region Helpers

    /// <summary>
    /// Đặt target cho spectator VCam (third-person).
    /// Chỉ set Follow/LookAt, không động tới priority.
    /// </summary>
    public void SetSpectatorTarget(Transform target)
    {
        if (!spectatorVcam) return;

        if (!target)
        {
            spectatorVcam.Follow = null;
            spectatorVcam.LookAt = null;
            return;
        }

        spectatorVcam.Follow = target;
        spectatorVcam.LookAt = target;
    }

    void AutoFindSceneRefs()
    {
        if (!mainCam) mainCam = Camera.main;
        if (!vcam) vcam = FindFirstObjectByType<CinemachineCamera>(FindObjectsInactive.Include);
        if (!fovDriver) fovDriver = FindFirstObjectByType<CameraADSFOVDriver>(FindObjectsInactive.Include);

        if (!weaponRoot)
        {
            var t = transform;
            weaponRoot = t.Find("WeaponRoot") ?? t;
        }

        if (!weaponModel) weaponModel = FindChildByName(weaponRoot, "WeaponModel");
        if (!weaponKickPivot) weaponKickPivot = FindChildByName(weaponRoot, "WeaponKickRig");

        if (!viewCtrl) viewCtrl = FindFirstObjectByType<WeaponViewController>(FindObjectsInactive.Include);
        if (!sway) sway = FindFirstObjectByType<WeaponSwayBob>(FindObjectsInactive.Include);
        if (!sceneWeaponCamAnchor) sceneWeaponCamAnchor = FindChildByName(transform, "WeaponCamAnchor");

        if (!spectatorOrbitRoot)
        {
            spectatorOrbitRoot = FindChildByName(transform, "SpectatorOrbitRoot");
            if (!spectatorOrbitRoot)
            {
                var go = new GameObject("SpectatorOrbitRoot");
                spectatorOrbitRoot = go.transform;
                spectatorOrbitRoot.SetParent(transform, false);
            }
        }
    }

    static Transform FindChildByName(Transform root, string name)
    {
        if (!root) return null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    static void SetPrivateFieldIfExists(object target, string fieldName, object value)
    {
        if (target == null) return;
        var f = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (f != null) f.SetValue(target, value);
    }

    static void TrySetFieldMultiNames(object target, string[] names, object value)
    {
        if (target == null) return;
        foreach (var n in names)
        {
            var f = target.GetType().GetField(n, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f != null) { f.SetValue(target, value); return; }
        }
    }

    public void SetSpectatorActive(bool active)
    {
        if (!spectatorVcam)
            return;

        // Nếu không có vcam FPS thì dùng số cứng
        if (!vcam)
        {
            spectatorVcam.Priority = active ? 20 : 0;
            return;
        }

        if (active)
        {
            // Cao hơn FPS một chút để Cinemachine blend sang
            spectatorVcam.Priority = vcam.Priority + 10;
        }
        else
        {
            // Thấp hơn FPS để quay lại cam thứ nhất
            spectatorVcam.Priority = vcam.Priority - 10;
        }
    }

    #endregion
}