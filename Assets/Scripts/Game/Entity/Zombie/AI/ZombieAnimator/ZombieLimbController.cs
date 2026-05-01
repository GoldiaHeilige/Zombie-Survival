using System;
using System.Collections.Generic;
using Fusion;
using TT;
using UnityEngine;

[DisallowMultipleComponent]
public class ZombieLimbController : NetworkBehaviour, IPoolable
{
    [Header("Thresholds (Option A gauge)")]
    public float leftArmDetachThreshold = 35f;
    public float rightArmDetachThreshold = 35f;
    public float headDetachThreshold = 25f;

    [Header("Threshold Scaling (from current MaxHP)")]
    public bool scaleDetachThresholdFromMaxHp = true;

    [Range(0.05f, 1f)] public float leftArmDetachPct = 0.40f;
    [Range(0.05f, 1f)] public float rightArmDetachPct = 0.40f;

    // Head detach = insta-kill -> nên cao (0.80..0.90)
    [Range(0.05f, 1f)] public float headDetachPct = 0.90f;

    DamageableHealth _hp;
    float _baseLeftArmThreshold, _baseRightArmThreshold, _baseHeadThreshold;


    [Header("Audio (world 3D)")]
    public int limbBreakAudioEventId; // kéo AudioEventSO id của tiếng limb vỡ vào đây

    [Header("Limb VFX (local only)")]
    public ParticleSystem bloodFxPrefab;

    [Tooltip("Rigidbody limb prefab (arm). Spawn local only, no networking.")]
    public GameObject leftArmLimbPrefab;
    public GameObject rightArmLimbPrefab;

    [Header("VFX Anchors (optional, preferred)")]
    public string headAnchorName = "FX_Head";
    public string leftArmAnchorName = "FX_LeftArm";
    public string rightArmAnchorName = "FX_RightArm";

    Transform _headAnchor;
    Transform _leftArmAnchor;
    Transform _rightArmAnchor;

    [Min(0f)] public float limbDespawnAfter = 8f;
    public Vector2 limbEjectSpeed = new Vector2(2f, 5f);
    public Vector2 limbUpSpeed = new Vector2(1f, 3f);
    public bool spawnBloodOnArmDetach = true;
    public bool spawnBloodOnHeadDetach = true; // head gib không cần, nhưng máu tuỳ bạn

    [Header("Bones to scale=0 when detached (include child chains)")]
    public Transform[] leftArmBones;
    public Transform[] rightArmBones;
    public Transform[] headBones;

    // Gauge (authority only)
    float _leftGauge, _rightGauge, _headGauge;

    byte _spawnedFxMask;

    Animator _boundAnimator;

    // MP networked state (late-join safe)
    [Networked] public byte DetachedMaskNet { get; private set; }

    // SP / non-network fallback
    byte _detachedMaskLocal;

    // Cache original scales so pooling reset is safe
    readonly Dictionary<Transform, Vector3> _origScale = new();

    // Track what we last applied (not strictly required but helps avoid extra work)
    byte _lastAppliedMask = 0xFF;

    const byte HEAD = 1 << 0;
    const byte LARM = 1 << 1;
    const byte RARM = 1 << 2;

    void Awake()
    {
        CacheOriginal(leftArmBones);
        CacheOriginal(rightArmBones);
        CacheOriginal(headBones);

        _baseLeftArmThreshold = leftArmDetachThreshold;
        _baseRightArmThreshold = rightArmDetachThreshold;
        _baseHeadThreshold = headDetachThreshold;

        _hp = GetComponentInParent<DamageableHealth>();
    }

    void CacheOriginal(Transform[] arr)
    {
        if (arr == null) return;
        for (int i = 0; i < arr.Length; i++)
        {
            var t = arr[i];
            if (!t) continue;
            if (!_origScale.ContainsKey(t))
                _origScale.Add(t, t.localScale);
        }
    }

    bool HasAuthorityForLogic
    {
        get
        {
            // MP: only StateAuthority should mutate state/gauge
            if (Object != null && Object.IsValid)
                return Object.HasStateAuthority;

            // SP / no NetworkObject: local authority
            return true;
        }
    }

    byte CurrentMask
    {
        get
        {
            if (Object != null && Object.IsValid)
                return DetachedMaskNet;
            return _detachedMaskLocal;
        }
        set
        {
            if (Object != null && Object.IsValid)
            {
                if (Object.HasStateAuthority)
                    DetachedMaskNet = value;
            }
            else
            {
                _detachedMaskLocal = value;
            }
        }
    }

    public bool IsHeadDetached => (CurrentMask & HEAD) != 0;
    public bool IsLeftArmDetached => (CurrentMask & LARM) != 0;
    public bool IsRightArmDetached => (CurrentMask & RARM) != 0;

    /// <summary>
    /// Called by LimbDetachProcessor (authority gated inside).
    /// Can modify DamageEvent (e.g., force fatal on head detach).
    /// </summary>
    public void ProcessLimbDamage(ref DamageEvent e, float limbMul)
    {
        if (!HasAuthorityForLogic) return;

        float dmg = Mathf.Max(0f, e.baseDamage) * Mathf.Max(0f, limbMul);
        if (dmg <= 0f) return;

        var mask = CurrentMask;

        switch (e.hitboxId)
        {
            case HitboxId.LeftArm:
            case HitboxId.Arm: // fallback if you still use old Arm on some colliders
                if ((mask & LARM) == 0)
                {
                    _leftGauge += dmg;
                    if (_leftGauge >= leftArmDetachThreshold)
                        mask |= LARM;
                }
                break;

            case HitboxId.RightArm:
                if ((mask & RARM) == 0)
                {
                    _rightGauge += dmg;
                    if (_rightGauge >= rightArmDetachThreshold)
                        mask |= RARM;
                }
                break;

            case HitboxId.Head:
                if ((mask & HEAD) == 0)
                {
                    _headGauge += dmg;
                    if (_headGauge >= headDetachThreshold)
                    {
                        mask |= HEAD;

                        // HEAD DETACH => FORCE FATAL
                        // Make damage big enough to kill via existing DamageableHealth logic
                        var hp = e.victimGO ? e.victimGO.GetComponentInParent<DamageableHealth>() : null;
                        if (hp != null)
                        {
                            float need = hp.currentHealth + 0.01f;
                            if (e.baseDamage < need)
                                e.baseDamage = need;
                        }
                        else
                        {
                            // fallback: just make it huge
                            e.baseDamage = Mathf.Max(e.baseDamage, 999999f);
                        }
                    }
                }
                break;
        }

        if (mask != CurrentMask)
            CurrentMask = mask;
    }

    // ===== Visual enforcement (Animator can fight scale) =====
    void LateUpdate()
    {
        // Always enforce if any detached, or if state changed
        ApplyVisualFromState(force: (CurrentMask != _lastAppliedMask) || CurrentMask != 0);
    }

    public override void Render()
    {
        // MP proxies: Render is a good place to enforce too
        ApplyVisualFromState(force: (CurrentMask != _lastAppliedMask) || CurrentMask != 0);
    }

    void ApplyVisualFromState(bool force)
    {
        if (!force) return;

        byte mask = CurrentMask;

        SpawnFxOnTransitions(_lastAppliedMask, mask);

        ApplyBones(headBones, (mask & HEAD) != 0);
        ApplyBones(leftArmBones, (mask & LARM) != 0);
        ApplyBones(rightArmBones, (mask & RARM) != 0);

        _lastAppliedMask = mask;
    }

    void ApplyBones(Transform[] bones, bool detached)
    {
        if (bones == null) return;

        for (int i = 0; i < bones.Length; i++)
        {
            var t = bones[i];
            if (!t) continue;

            if (detached)
            {
                t.localScale = Vector3.zero;
            }
            else
            {
                if (_origScale.TryGetValue(t, out var s))
                    t.localScale = s;
                else
                    t.localScale = Vector3.one;
            }
        }
    }

    // ===== Pool reset =====
    public void OnSpawned()
    {
        _spawnedFxMask = 0;
        // reset gauge
        _leftGauge = _rightGauge = _headGauge = 0f;

        // reset state (authority only in MP)
        if (HasAuthorityForLogic)
            CurrentMask = 0;

        RecomputeDetachThresholds();

        // reset visuals
        _lastAppliedMask = 0xFF;
        ApplyVisualFromState(force: true);
    }

    public void OnDespawned()
    {
        _spawnedFxMask = 0;
        // reset everything back for pool safety
        _leftGauge = _rightGauge = _headGauge = 0f;

        if (HasAuthorityForLogic)
            CurrentMask = 0;

        _lastAppliedMask = 0xFF;
        ApplyVisualFromState(force: true);
    }

    public void Rebind(Animator anim)
    {
        _boundAnimator = anim;

        // Clear old refs + cache (skin cũ bị Destroy)
        _origScale.Clear();

        // Build chains from the NEW skin animator
        leftArmBones = BuildArmChain(anim, isLeft: true);
        rightArmBones = BuildArmChain(anim, isLeft: false);
        headBones = BuildHeadChain(anim);

        // Cache original scales for pool reset / restore
        CacheOriginal(leftArmBones);
        CacheOriginal(rightArmBones);
        CacheOriginal(headBones);

        // Cache anchors once per skin bind (no per-frame search)
        _headAnchor = FindChildByName(anim.transform, headAnchorName);
        _leftArmAnchor = FindChildByName(anim.transform, leftArmAnchorName);
        _rightArmAnchor = FindChildByName(anim.transform, rightArmAnchorName);

        // Force apply current state immediately (in case late-join / already detached)
        _lastAppliedMask = 0xFF;
        ApplyVisualFromState(force: true);
    }

    public void Unbind()
    {
        _headAnchor = _leftArmAnchor = _rightArmAnchor = null;

        _boundAnimator = null;

        leftArmBones = null;
        rightArmBones = null;
        headBones = null;

        _origScale.Clear();
        _lastAppliedMask = 0xFF;
    }

    Transform[] BuildArmChain(Animator anim, bool isLeft)
    {
        if (!anim) return null;

        // Humanoid bones
  //      var upper = anim.GetBoneTransform(isLeft ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        var lower = anim.GetBoneTransform(isLeft ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var hand = anim.GetBoneTransform(isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);

        // If rig isn't humanoid, you can fallback later by name search
        if (!lower && !hand)
            return null;

        // You only need main chain bones (children will follow via skin weights),
        // but if your mesh still shows leftovers, you can add more bones here.
        var list = new List<Transform>(3);
        if (lower) list.Add(lower);
        if (hand) list.Add(hand);
        return list.ToArray();
    }

    Transform[] BuildHeadChain(Animator anim)
    {
        if (!anim) return null;

        var head = anim.GetBoneTransform(HumanBodyBones.Head);

        if (!head)
            return null;

        var list = new List<Transform>(2);
        if (head) list.Add(head);
        return list.ToArray();
    }

    void SpawnFxOnTransitions(byte prevMask, byte newMask)
    {
        // Dedicated server / headless: skip visuals
        if (Runner != null && Runner.Mode == SimulationModes.Server)
            return;

        // Head detached
        if (((prevMask & HEAD) == 0) && ((newMask & HEAD) != 0))
            TrySpawnHeadDetachFx();

        // Left arm detached
        if (((prevMask & LARM) == 0) && ((newMask & LARM) != 0))
            TrySpawnArmDetachFx(isLeft: true);

        // Right arm detached
        if (((prevMask & RARM) == 0) && ((newMask & RARM) != 0))
            TrySpawnArmDetachFx(isLeft: false);
    }

    void TrySpawnHeadDetachFx()
    {
        if ((_spawnedFxMask & HEAD) != 0) return;
        _spawnedFxMask |= HEAD;

        if (!spawnBloodOnHeadDetach) return;

        var t = GetHeadSpawn();
        SpawnBloodAt(t);
        PlayLimbBreakAudio(t);
    }

    void TrySpawnArmDetachFx(bool isLeft)
    {
        byte bit = isLeft ? LARM : RARM;
        if ((_spawnedFxMask & bit) != 0) return;
        _spawnedFxMask |= bit;

        // Spawn limb rigidbody prefab (arm only)
        var prefab = isLeft ? leftArmLimbPrefab : rightArmLimbPrefab;
        if (prefab != null)
        {
            var t = GetArmSpawn(isLeft);
            SpawnLimbGib(prefab, t);
            SpawnBloodAt(t);
            PlayLimbBreakAudio(t);
        }
    }

    Transform GetArmSpawn(bool isLeft)
    {
        var a = isLeft ? _leftArmAnchor : _rightArmAnchor;
        if (a) return a;
        return GetBestArmSpawn(isLeft); // fallback xương (như hiện tại)
    }

    Transform GetHeadSpawn()
    {
        if (_headAnchor) return _headAnchor;
        return GetBestHeadSpawn(); // fallback head bone
    }


    Transform GetBestArmSpawn(bool isLeft)
    {
        var bones = isLeft ? leftArmBones : rightArmBones;
        if (bones == null || bones.Length == 0) return transform;

        // ưu tiên bone cuối (thường là hand)
        for (int i = bones.Length - 1; i >= 0; i--)
            if (bones[i]) return bones[i];

        return transform;
    }

    Transform GetBestHeadSpawn()
    {
        if (headBones != null)
        {
            for (int i = headBones.Length - 1; i >= 0; i--)
                if (headBones[i]) return headBones[i];
        }
        return transform;
    }

    void SpawnBloodAt(Transform at)
    {
        if (bloodFxPrefab == null || at == null) return;

        var ps = Instantiate(bloodFxPrefab, at.position, at.rotation);
        ps.Play(true);

        // auto destroy theo duration gần đúng
        float life = 3f;
        try
        {
            var main = ps.main;
            life = main.duration;
            if (main.startLifetime.mode == ParticleSystemCurveMode.TwoConstants)
                life += main.startLifetime.constantMax;
            else if (main.startLifetime.mode == ParticleSystemCurveMode.Constant)
                life += main.startLifetime.constant;
        }
        catch { /* ignore */ }

        Destroy(ps.gameObject, Mathf.Max(1f, life));
    }

    void SpawnLimbGib(GameObject prefab, Transform at)
    {
        if (!prefab || !at) return;

        var go = Instantiate(prefab, at.position, at.rotation);

        // push force if it has rigidbody
        var rb = go.GetComponentInChildren<Rigidbody>();
        if (rb != null)
        {
            Vector3 dir = (transform.forward + transform.right * UnityEngine.Random.Range(-0.6f, 0.6f)).normalized;
            float sp = UnityEngine.Random.Range(limbEjectSpeed.x, limbEjectSpeed.y);
            float up = UnityEngine.Random.Range(limbUpSpeed.x, limbUpSpeed.y);

            rb.linearVelocity = dir * sp + Vector3.up * up;
            rb.angularVelocity = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(4f, 10f);
        }

        if (limbDespawnAfter > 0f)
            Destroy(go, limbDespawnAfter);
    }

    static Transform FindChildByName(Transform root, string name)
    {
        if (!root || string.IsNullOrEmpty(name)) return null;

        // DFS - chỉ chạy lúc Rebind nên không sao
        var stack = new Stack<Transform>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
                stack.Push(t.GetChild(i));
        }
        return null;
    }

    void PlayLimbBreakAudio(Transform at)
    {
        if (limbBreakAudioEventId == 0) return;

        // Dedicated server/headless thì khỏi phát
        if (Runner != null && Runner.Mode == SimulationModes.Server) return;

        // CHỈ authority gọi để tránh spam/duplicate ở MP
        if (!HasAuthorityForLogic) return;

        AudioEvents.PlayWorld3D(limbBreakAudioEventId, at.position /*, shooterNO: null */);
    }

    void RecomputeDetachThresholds()
    {
        if (!scaleDetachThresholdFromMaxHp) return;
        if (_hp == null) _hp = GetComponentInParent<DamageableHealth>();
        if (_hp == null) return;

        // lấy MaxHP hiện tại (đã qua EnemyHealthScaler)
        float maxHp = Mathf.Max(1f, _hp.maxHealth);

        leftArmDetachThreshold = Mathf.Max(1f, maxHp * leftArmDetachPct);
        rightArmDetachThreshold = Mathf.Max(1f, maxHp * rightArmDetachPct);
        headDetachThreshold = Mathf.Max(1f, maxHp * headDetachPct);
    }

}
