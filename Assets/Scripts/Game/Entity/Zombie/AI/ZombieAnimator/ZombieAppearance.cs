using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class ZombieAppearance : MonoBehaviour, IPoolable
{
    [Header("Setup")]
    [Tooltip("Nơi để gắn model Synty vào. Nếu trống sẽ dùng chính transform này.")]
    public Transform visualRoot;

    [Tooltip("Danh sách prefab skin Synty (chỉ chứa model + Animator).")]
    public List<GameObject> skinPrefabs = new List<GameObject>();

    [Header("Runtime (debug)")]
    public Animator currentAnimator;

    GameObject _currentSkinInstance;
    ZombieNetworkAnimator _netAnim;   // có nghĩa là đang chạy trong MP
    bool _isMultiplayer;

    void Reset()
    {
        if (!visualRoot)
        {
            var child = transform.Find("VisualRoot");
            if (child != null) visualRoot = child;
        }
    }

    void Awake()
    {
        if (!visualRoot)
            visualRoot = transform;

        _netAnim = GetComponent<ZombieNetworkAnimator>();

        // Phân biệt SP / MP bằng GameSession, không dùng _netAnim nữa
        _isMultiplayer = GameSession.Mode != AppPlayMode.Single;
    }

    void Start()
    {
        // MP: skin do network quyết định, không random local
        if (_isMultiplayer)
            return;

        // SP / offline: tự random skin lần đầu
        if (_currentSkinInstance == null)
            ApplyRandomSkin();
    }

    // IPoolable — SP có dùng pool, MP hiện tại thì không
    public void OnSpawned()
    {
        // MP: skin do network quyết định
        if (_isMultiplayer)
            return;

        ApplyRandomSkin();
    }

    public void OnDespawned()
    {
        ClearSkin();
    }

    // ================== PUBLIC API cho network ==================

    /// <summary>Được gọi bởi ZombieNetworkAnimator khi đã sync index.</summary>
    public void ApplySkinByIndex(int index)
    {
        ClearSkin();

        if (skinPrefabs == null || skinPrefabs.Count == 0)
            return;

        if (index < 0 || index >= skinPrefabs.Count)
        {
            index = Mathf.Clamp(index, 0, skinPrefabs.Count - 1);
        }

        var prefab = skinPrefabs[index];
        if (!prefab) return;

        SpawnSkinInstance(prefab);
    }

    // ================== INTERNAL ==================

    void ApplyRandomSkin()
    {
        if (skinPrefabs == null || skinPrefabs.Count == 0)
            return;

        var prefab = skinPrefabs[Random.Range(0, skinPrefabs.Count)];
        if (!prefab) return;

        SpawnSkinInstance(prefab);
    }

    void SpawnSkinInstance(GameObject prefab)
    {
        ClearSkin();

        var parent = visualRoot ? visualRoot : transform;
        _currentSkinInstance = Instantiate(prefab, parent);
        _currentSkinInstance.transform.localPosition = Vector3.zero;
        _currentSkinInstance.transform.localRotation = Quaternion.identity;
        _currentSkinInstance.transform.localScale = Vector3.one;

        // 1) Lấy Animator từ skin
        currentAnimator = _currentSkinInstance.GetComponentInChildren<Animator>();

        // 2) Đẩy animator sang các hệ thống cần nó
        if (currentAnimator != null)
        {
            // 🔹 GẮN RELAY CHO ANIM EVENTS
            var relay = currentAnimator.GetComponent<ZombieAnimEventRelay>();
            if (!relay)
            {
                relay = currentAnimator.gameObject.AddComponent<ZombieAnimEventRelay>();
            }

            var animCtrl = GetComponent<ZombieAnimatorCtrl>();
            if (animCtrl) animCtrl.animator = currentAnimator;

            var deathHandler = GetComponent<ZombieDeathHandler>();
            if (deathHandler) deathHandler.animator = currentAnimator;

            var melee = GetComponent<ZombieMeleeExecutor>();
            if (melee) melee.animator = currentAnimator;

            var netAnim = GetComponent<ZombieNetworkAnimator>();
            if (netAnim) netAnim.animator = currentAnimator;

            var limb = GetComponent<ZombieLimbController>();
            if (limb) limb.Rebind(currentAnimator);
        }
        else
        {
            Debug.LogWarning($"[ZombieAppearance] Không tìm thấy Animator trong skin '{prefab.name}'", this);
        }
    }


    void ClearSkin()
    {
        if (_currentSkinInstance)
        {
            var limb = GetComponent<ZombieLimbController>();
            if (limb) limb.Unbind();

            Destroy(_currentSkinInstance);
            _currentSkinInstance = null;
        }
    }
}
