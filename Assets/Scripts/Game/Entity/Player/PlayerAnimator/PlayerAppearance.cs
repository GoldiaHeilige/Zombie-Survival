using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;

[DisallowMultipleComponent]

[System.Serializable]
public class PlayerSkinSlot
{
    [Header("World Model (3rd person)")]
    [Tooltip("Prefab Synty full body (model + Animator + rig...).")]
    public GameObject worldPrefab;

    [Header("First Person Arms (FP)")]
    [Tooltip("Mesh tay FPS dùng cho skin này.")]
    public Mesh fpArmMesh;

    [Tooltip("Material tay FPS dùng cho skin này.")]
    public Material fpArmMaterial;
}

public class PlayerAppearance : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("Nơi để gắn model Synty vào. Nếu trống sẽ dùng chính transform này.")]
    [SerializeField] private Transform visualRoot;

    [Header("Skin Slots (World + FP)")]
    [Tooltip("Mỗi slot = 1 skin: world prefab + mesh & material tay FPS.")]
    [SerializeField] private List<PlayerSkinSlot> skins = new List<PlayerSkinSlot>();

    [Tooltip("View tay FPS local, được CameraBinder gán khi player này là local.")]
    [SerializeField] private PlayerFPArmView localFpArmView;


    [Header("Socket names (theo rig Synty của bạn)")]
    [SerializeField] private string handSocketName = "socket_Hand_r";
    [SerializeField] private string primaryHolsterSocketName = "";
    [SerializeField] private string secondaryHolsterSocketName = "";

    [Header("Upper body aim rig (nằm trong skin)")]
    [SerializeField] private string upperBodyRigRootName = "UpperBodyAimRig";
    [SerializeField] private string upperBodyAimTargetName = "AimTarget";

    [Header("Local Visibility")]
    [SerializeField] private bool hideWorldModelForLocal = true;

    [Header("Runtime (debug)")]
    [SerializeField] private Animator currentAnimator;

    public System.Action<GameObject> OnSkinSpawned;
    public GameObject CurrentSkinInstance => _currentSkinInstance;
    GameObject _currentSkinInstance;
    bool _isMultiplayer;
    bool _isLocalPlayer;
    Renderer[] _cachedWorldRenderers;

    // === NEW: lưu lại skin hiện tại ===
    int _currentSkinIndex = -1;

    // Các hệ thống cần được bind lại khi skin thay đổi
    PlayerAnimatorCtrl _animCtrl;
    PlayerWeaponWorldView _weaponWorldView;
    UpperBodyAimRigDriver _upperBodyAimDriver;
    RigBuilder _rigBuilder;
    PlayerNetworkAnimator _netAnimator;
    WeaponEquipAnimBridge _equipBridge;
    ReloadAnimBridge _reloadBridge;
    WeaponLayerWeightController _weaponLayerCtrl;
    ADSWeightBridge _adsBridge;

#if FUSION_WEAVER
    FusionPlayerRevive _revive;
#endif
    public int SkinCount => skins != null ? skins.Count : 0;

    void Awake()
    {
        if (!visualRoot)
            visualRoot = transform;

        _animCtrl = GetComponent<PlayerAnimatorCtrl>();
        _weaponWorldView = GetComponentInChildren<PlayerWeaponWorldView>(true);
        _upperBodyAimDriver = GetComponentInChildren<UpperBodyAimRigDriver>(true);
        _rigBuilder = GetComponentInChildren<RigBuilder>(true);
        _netAnimator = GetComponent<PlayerNetworkAnimator>();

        _equipBridge = GetComponentInChildren<WeaponEquipAnimBridge>(true);
        _reloadBridge = GetComponentInChildren<ReloadAnimBridge>(true);
        _weaponLayerCtrl = GetComponentInChildren<WeaponLayerWeightController>(true);
        _adsBridge = GetComponentInChildren<ADSWeightBridge>(true);

#if FUSION_WEAVER
        _revive = GetComponent<FusionPlayerRevive>();
#endif
        _isMultiplayer = GameSession.Mode != AppPlayMode.Single;
    }


    void Start()
    {
        if (!_isMultiplayer && SkinCount > 0)
        {
          //  Debug.Log("[SP] FORCING ApplySkinByIndex");
            ApplySkinByIndex(-1);

            // QUAN TRỌNG — gọi lại cho FP view nếu weapon đã spawn
            ApplyCurrentFPArms();
        }
    }


    /// <summary>
    /// SP hoặc debug: random 1 skin local.
    /// MP: host có thể dùng hàm này trước khi RPC_SetSkin.
    /// </summary>
    public void ApplyRandomSkin()
    {
        if (SkinCount == 0) return;
        int index = Random.Range(0, SkinCount);
        ApplySkinByIndex(index);
    }

    /// <summary>
    /// Được gọi bởi PlayerNetworkAnimator (MP) hoặc dùng tay.
    /// </summary>
    /// <summary>
    /// Được gọi bởi PlayerNetworkAnimator (MP) hoặc dùng tay.
    /// </summary>
    public void ApplySkinByIndex(int index = -1)  // Thêm default value = -1
    {
        if (SkinCount == 0) return;

        // Nếu index = -1 -> random skin
        if (index == -1)
        {
            index = Random.Range(0, SkinCount);
        }

        index = Mathf.Clamp(index, 0, SkinCount - 1);

        var slot = skins[index];
        if (slot == null || !slot.worldPrefab)
        {
            Debug.LogWarning($"[PlayerAppearance] skins[{index}] không có worldPrefab trên {name}", this);
            return;
        }

        _currentSkinIndex = index;

        // World model
        SpawnSkinInstance(slot.worldPrefab);

        /*        // FP arms (local-only)
                ApplyFPArms(index);*/
    }

    void SpawnSkinInstance(GameObject prefab)
    {
        // Clear instance cũ
        if (_currentSkinInstance)
        {
            Destroy(_currentSkinInstance);
            _currentSkinInstance = null;
        }

        // Instantiate model mới
        _currentSkinInstance = Instantiate(prefab, visualRoot);
        var t = _currentSkinInstance.transform;
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;

        // Cache local flag (để chắc chắn đúng cả SP lẫn MP)
        _isLocalPlayer = DetermineIsLocalPlayer();

        // Local: ẩn world model render để FPS không thấy body mình
        if (hideWorldModelForLocal && _isLocalPlayer)
        {
            _cachedWorldRenderers = null; // reset cache theo skin mới
            SetWorldModelVisible(false);
        }
        else
        {
            _cachedWorldRenderers = null;
            SetWorldModelVisible(true);
        }

        // Lấy Animator từ skin
        var anim = _currentSkinInstance.GetComponentInChildren<Animator>();
        if (anim)
        {
            currentAnimator = anim;

            if (_animCtrl != null)
                _animCtrl.SetAnimator(anim);

            if (_netAnimator != null)
                _netAnimator.SetAnimator(anim);

            if (_weaponWorldView != null)
                _weaponWorldView.SetWorldAnimator(anim);

            if (_equipBridge != null)
                _equipBridge.SetAnimator(anim);

            if (_reloadBridge != null)
                _reloadBridge.SetAnimator(anim);

            if (_weaponLayerCtrl != null)
                _weaponLayerCtrl.Initialize(anim);

            if (_adsBridge != null)
                _adsBridge.SetWorldAnimator(anim);
        }

        else
        {
            currentAnimator = anim;

            // Bơm Animator cho các hệ thống khác
            if (_animCtrl != null)
                _animCtrl.SetAnimator(anim);

            if (_netAnimator != null)
                _netAnimator.SetAnimator(anim);

            if (_weaponWorldView != null)
                _weaponWorldView.SetWorldAnimator(anim);
        }

        // ONLY local: vẫn update xương/hand socket dù world model bị ẩn
        if (_isLocalPlayer && anim != null)
        {
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        // Bind socket weapon / holster
        BindWeaponSockets();

        // Bind upper body rig + aim driver
        BindUpperBodyRig();

#if FUSION_WEAVER
        BindReviveAnchor();
#endif
        OnSkinSpawned?.Invoke(_currentSkinInstance);
    }

    void BindWeaponSockets()
    {
        if (_weaponWorldView == null || _currentSkinInstance == null)
            return;

        Transform root = _currentSkinInstance.transform;

        Transform hand = null;
        Transform holsterPrimary = null;
        Transform holsterSecondary = null;

        if (!string.IsNullOrEmpty(handSocketName))
            hand = FindChildRecursive(root, handSocketName);

        if (!string.IsNullOrEmpty(primaryHolsterSocketName))
            holsterPrimary = FindChildRecursive(root, primaryHolsterSocketName);

        if (!string.IsNullOrEmpty(secondaryHolsterSocketName))
            holsterSecondary = FindChildRecursive(root, secondaryHolsterSocketName);

        if (!hand)
        {
            Debug.LogWarning($"[PlayerAppearance] Không tìm thấy hand socket '{handSocketName}' trong skin '{_currentSkinInstance.name}'", this);
        }

        _weaponWorldView.SetSockets(hand, holsterPrimary, holsterSecondary);
    }

    void BindUpperBodyRig()
    {
        if (_currentSkinInstance == null)
            return;

        Transform skinRoot = _currentSkinInstance.transform;

        // Tìm rig root trong skin
        Rig rig = null;
        Transform rigRoot = null;

        if (!string.IsNullOrEmpty(upperBodyRigRootName))
        {
            rigRoot = FindChildRecursive(skinRoot, upperBodyRigRootName);
            if (rigRoot)
                rig = rigRoot.GetComponent<Rig>();
        }

        // Cập nhật RigBuilder để dùng rig mới
        if (_rigBuilder != null && rig != null)
        {
            var layers = _rigBuilder.layers;
            bool replaced = false;

            for (int i = 0; i < layers.Count; i++)
            {
                if (layers[i].rig != null && layers[i].rig.name == rig.name)
                {
                    layers[i] = new RigLayer(rig, layers[i].active);
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                layers.Add(new RigLayer(rig, true));
            }

            _rigBuilder.layers = layers;
        }

        // Cập nhật UpperBodyAimRigDriver
        if (_upperBodyAimDriver != null)
        {
            // 1) Inject Rig cho driver để nó control weight
            if (rig != null)
            {
                _upperBodyAimDriver.SetUpperBodyRig(rig);
            }

            // 2) Bind lại origin/target cho aim
            Transform aimTarget = null;
            if (!string.IsNullOrEmpty(upperBodyAimTargetName))
            {
                if (rigRoot)
                    aimTarget = FindChildRecursive(rigRoot, upperBodyAimTargetName);
                if (!aimTarget)
                    aimTarget = FindChildRecursive(skinRoot, upperBodyAimTargetName);
            }

            Transform aimOrigin = null;
            if (currentAnimator != null)
            {
                // Lấy bone Spine (hoặc Chest tuỳ rig)
                aimOrigin = currentAnimator.GetBoneTransform(HumanBodyBones.Spine);
            }

            _upperBodyAimDriver.SetAimRig(aimOrigin, aimTarget);
        }
    }

#if FUSION_WEAVER
    void BindReviveAnchor()
    {
        if (_revive == null || _currentSkinInstance == null)
            return;

        Transform anchor = null;

        // Ưu tiên bone Hips – trung tâm body
        if (currentAnimator != null)
        {
            anchor = currentAnimator.GetBoneTransform(HumanBodyBones.Hips);
        }

        // Fallback: nếu không có Hips thì dùng root skin
        if (!anchor)
        {
            anchor = _currentSkinInstance.transform;
        }

        _revive.SetReviveAnchor(anchor);
    }
#endif

    Transform FindChildRecursive(Transform root, string childName)
    {
        if (!root || string.IsNullOrEmpty(childName))
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            var found = FindChildRecursive(child, childName);
            if (found) return found;
        }

        return null;
    }

    /// <summary>
    /// Được CameraBinder gọi khi player này là local.
    /// Nối PlayerAppearance với tay FP trong CameraRig.
    /// </summary>
    public void SetLocalFPArmView(PlayerFPArmView fpView)
    {
        localFpArmView = fpView;

        // Nếu đã có skin hiện tại rồi thì áp luôn
        if (localFpArmView != null && _currentSkinIndex >= 0)
        {
            ApplyFPArms(_currentSkinIndex);
        }
    }

    /// <summary>
    /// Áp tay FPS theo skin index (mesh + materials).
    /// </summary>
    void ApplyFPArms(int skinIndex)
    {
        if (localFpArmView == null)
        {
            Debug.LogWarning("[PlayerAppearance] localFpArmView is NULL, skip FP");
            return;
        }      

        if (skinIndex < 0 || skinIndex >= skins.Count)
        {
            Debug.LogWarning($"[PlayerAppearance] skinIndex {skinIndex} ngoài range skins trên {name}", this);
            return;
        }

        var slot = skins[skinIndex];
        if (slot == null)
        {
            Debug.LogWarning($"[PlayerAppearance] skins[{skinIndex}] null trên {name}", this);
            return;
        }

        var mesh = slot.fpArmMesh;
        var mat = slot.fpArmMaterial;

        if (mesh == null)
        {
            Debug.LogWarning($"[PlayerAppearance] fpArmMesh null cho skin {skinIndex} trên {name}", this);
            return;
        }

        localFpArmView.Apply(mesh, mat);
    }

    /// <summary>
    /// Được WeaponController gọi sau khi FP view spawn (equip súng).
    /// </summary>
    // Trong PlayerAppearance.cs
    public void ApplyCurrentFPArms()
    {
        // Debug để biết ai gọi
        var stackTrace = new System.Diagnostics.StackTrace();
        var callerMethod = stackTrace.GetFrame(1).GetMethod();
        var callerClass = callerMethod.DeclaringType.Name;

      //  Debug.Log($"[PlayerAppearance] ApplyCurrentFPArms called from {callerClass}.{callerMethod.Name}");

        if (_currentSkinIndex >= 0)
        {
            ApplyFPArms(_currentSkinIndex);
        }
    }

    bool DetermineIsLocalPlayer()
    {
        // SP: luôn là local
        if (GameSession.Mode == AppPlayMode.Single) return true;

#if FUSION_WEAVER
        // MP: local = có InputAuthority
        var no = GetComponent<Fusion.NetworkObject>();
        if (no != null && no.Runner != null && no.IsValid)
            return no.HasInputAuthority;
#endif
        return false;
    }

    void SetWorldModelVisible(bool visible)
    {
        if (_currentSkinInstance == null) return;

        if (_cachedWorldRenderers == null || _cachedWorldRenderers.Length == 0)
            _cachedWorldRenderers = _currentSkinInstance.GetComponentsInChildren<Renderer>(true);

        foreach (var r in _cachedWorldRenderers)
        {
            if (!r) continue;

            // Skinned: giữ enabled=true để rig/anim không bị "đơ", nhưng không render
            if (r is SkinnedMeshRenderer smr)
            {
                smr.enabled = true;                 // luôn bật
                smr.forceRenderingOff = !visible;   // ẩn/hiện bằng forceRenderingOff
                                                    // smr.updateWhenOffscreen = true;  // chỉ bật nếu vẫn còn đơ (optional)
            }
            else
            {
                // Mesh thường: bật/tắt như cũ
                r.enabled = visible;
            }
        }
    }


    // Cho local player: FPS = hide body, Spectator/Downed = show body
    public void SetLocalWorldModelHidden(bool hidden)
    {
        // đảm bảo đúng local flag (skin mới spawn đã set, nhưng gọi lại cho chắc)
        if (!_isLocalPlayer)
            _isLocalPlayer = DetermineIsLocalPlayer();

        // Chỉ áp cho local (remote players không đụng)
        if (!_isLocalPlayer) return;

        // hidden=true -> tắt renderer, hidden=false -> bật renderer
        SetWorldModelVisible(!hidden);
    }

}
