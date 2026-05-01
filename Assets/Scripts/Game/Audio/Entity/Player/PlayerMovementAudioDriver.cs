using UnityEngine;
using TT; // AudioEvents, AudioEventSO, SurfaceType, SurfaceMaterial

#if FUSION_WEAVER
using Fusion;
#endif

[DisallowMultipleComponent]
public class PlayerMovementAudioDriver : MonoBehaviour
{
    [Header("Profile")]
    public PlayerMovementAudioProfile profile;

    [Header("Refs")]
    [Tooltip("StateProvider của player này (để đọc MovementStateId). Nếu trống sẽ auto-find.")]
    public PlayerStateProvider stateProvider;

    [Tooltip("Optional: override origin raycast footstep. Nếu null sẽ dùng transform.position + up * height.")]
    public Transform footRayOriginOverride;

    [Tooltip("Optional: emitter để attach các SFX (crouch, rattle, jump start). Nếu null sẽ dùng transform.")]
    public Transform audioEmitterOverride;

    [Header("Debug")]
    public bool debugDraw = false;

    // per-player footstep cooldown
    float _nextLocalFootstepTime;

    // Listener cache
    static Transform s_listener;
    static float s_nextListenerSearchTime = 0f;

    // for approximating vertical velocity
    Vector3 _prevPos;
    float _lastVerticalVel = 0f;

#if FUSION_WEAVER
    NetworkObject _no;
#endif

    void Awake()
    {
        if (!stateProvider)
            stateProvider = GetComponent<PlayerStateProvider>();

#if FUSION_WEAVER
        _no = GetComponentInParent<NetworkObject>();
#endif
        _prevPos = transform.position;
    }

    void Update()
    {
        // sample vertical velocity
        float dy = (transform.position.y - _prevPos.y);
        float dt = Mathf.Max(Time.deltaTime, 1e-6f);
        _lastVerticalVel = dy / dt;
        _prevPos = transform.position;
    }

    // ================= Animation event API =================

    /// <summary>Call from AnimEvent when jump begins (player leaves ground).</summary>
    public void AnimEvent_JumpStart()
    {
        if (!CanPlayLocalSfx) return;
        var ev = profile.jumpStartSfx;
        if (ev == null) return;

        if (!IsWithinDistance(profile.maxFootstepDistance))
            return;

        Transform emitter = GetAudioEmitter();
        if (!emitter) return;

        // Play attached so the sound follows player
        AudioEvents.PlayWorld3DAttached(ev.eventId, emitter);
    }

    /// <summary>Call from AnimEvent when landing (anim's landing frame).</summary>
    public void AnimEvent_Land()
    {
        if (!CanPlayLocalSfx) return;

        if (!IsWithinDistance(profile.maxFootstepDistance))
            return;

        // Use last sampled vertical velocity to decide soft/hard landing
        float verticalVel = _lastVerticalVel; // positive = moving up, negative = falling
        bool isHard = verticalVel <= profile.landHardVelocity;

        AudioEventSO toPlay = isHard ? profile.landHardSfx : profile.landSoftSfx;
        if (toPlay == null) return;

        // Try to determine hit point using robust spherecast similar to footstep
        Vector3 origin = GetFootRayOrigin();
        Vector3 hitPoint = origin;
        RaycastHit hitInfo;
        float sphereRadius = 0.18f;
        float maxDist = profile.footRaycastDistance;
        bool hit = Physics.SphereCast(origin, sphereRadius, Vector3.down, out hitInfo, maxDist, profile.footstepMask, QueryTriggerInteraction.Ignore);
        if (!hit)
        {
            float fallbackDist = Mathf.Max(maxDist, 2.5f);
            hit = Physics.Raycast(origin, Vector3.down, out hitInfo, fallbackDist, profile.footstepMask, QueryTriggerInteraction.Ignore);
        }

        if (hit)
        {
            hitPoint = hitInfo.point;
            // Play at point so the foot impact is spatialized at ground contact
            AudioEvents.PlayWorld3D(toPlay.eventId, hitPoint);
        }
        else
        {
            // fallback: attach to emitter
            Transform emitter = GetAudioEmitter();
            if (emitter != null)
                AudioEvents.PlayWorld3DAttached(toPlay.eventId, emitter);
        }

#if UNITY_EDITOR
        if (debugDraw)
        {
            Debug.Log($"[PlayerAudio] Land detected vel={verticalVel:F2} hard={isHard} play={(toPlay != null ? toPlay.name : "NULL")}");
        }
#endif
    }

    /// <summary>Được gọi bởi AnimationEvent khi chân chạm đất.</summary>
    public void AnimEvent_Footstep()
    {
        if (!CanPlayLocalSfx) return;
        if (profile == null) return;

        if (!IsWithinDistance(profile.maxFootstepDistance))
            return;

        // per-player footstep cooldown
        if (Time.time < _nextLocalFootstepTime)
            return;

        if (Random.value > profile.footstepPlayChance)
            return;

        // Robust surface detection
        Vector3 origin = GetFootRayOrigin();
        SurfaceType surface = SurfaceType.Default;
        Vector3 hitPoint = origin;

        float sphereRadius = 0.18f; // tweakable
        float maxDist = profile.footRaycastDistance;

        RaycastHit hitInfo;
        bool hit = Physics.SphereCast(origin, sphereRadius, Vector3.down, out hitInfo, maxDist, profile.footstepMask, QueryTriggerInteraction.Ignore);
        if (!hit)
        {
            float fallbackDist = Mathf.Max(maxDist, 2.5f);
            hit = Physics.Raycast(origin, Vector3.down, out hitInfo, fallbackDist, profile.footstepMask, QueryTriggerInteraction.Ignore);
        }

        if (hit)
        {
            hitPoint = hitInfo.point;

            // FIND SurfaceMaterial on the collider or any parent
            SurfaceMaterial surfMat = hitInfo.collider != null ? hitInfo.collider.GetComponentInParent<SurfaceMaterial>() : null;
            if (surfMat != null)
                surface = surfMat.type;
            else
            {
                // optional fallback from PhysicMaterial name
                var phys = hitInfo.collider != null ? hitInfo.collider.sharedMaterial : null;
                if (phys != null)
                {
                    string nm = phys.name.ToLower();
                    if (nm.Contains("metal")) surface = SurfaceType.Metal;
                    else if (nm.Contains("wood")) surface = SurfaceType.Wood;
                    else if (nm.Contains("concrete") || nm.Contains("stone")) surface = SurfaceType.Concrete;
                    else if (nm.Contains("dirt") || nm.Contains("soil")) surface = SurfaceType.Dirt;
                    else if (nm.Contains("glass")) surface = SurfaceType.Glass;
                }
            }
        }
        else
        {
#if UNITY_EDITOR
            if (debugDraw)
                Debug.Log($"[Footstep] Ray/SphereCast hit NOTHING. origin={origin} dist={profile.footRaycastDistance} (set footRayOriginOverride near the feet).");
#endif
        }

        var ev = profile.GetFootstepForSurface(surface);
        if (ev != null)
        {
            _nextLocalFootstepTime = Time.time + Mathf.Max(profile.minFootstepInterval, 0.05f);
            AudioEvents.PlayWorld3D(ev.eventId, hitPoint);
        }

        // After footstep, try play rattle based on movement state
        TryPlayRattle();

#if UNITY_EDITOR
        if (debugDraw)
        {
            Debug.DrawLine(origin, origin + Vector3.down * maxDist, Color.cyan, 0.5f);
            if (hit) Debug.DrawLine(hitInfo.point, hitInfo.point + Vector3.up * 0.25f, Color.yellow, 1f);
        }
#endif
    }

    /// <summary>Được gọi bởi AnimationEvent khi NGỒI xuống (crouch).</summary>
    public void AnimEvent_CrouchDown()
    {
        if (!CanPlayLocalSfx) return;
        if (profile == null) return;
        if (profile.crouchDownSfx == null) return;

        if (!IsWithinDistance(profile.maxFootstepDistance))
            return;

        Transform emitter = GetAudioEmitter();
        if (!emitter) return;

        AudioEvents.PlayWorld3DAttached(profile.crouchDownSfx.eventId, emitter);
    }

    /// <summary>Được gọi bởi AnimationEvent khi ĐỨNG dậy.</summary>
    public void AnimEvent_StandUp()
    {
        if (!CanPlayLocalSfx) return;
        if (profile == null) return;
        if (profile.standUpSfx == null) return;

        if (!IsWithinDistance(profile.maxFootstepDistance))
            return;

        Transform emitter = GetAudioEmitter();
        if (!emitter) return;

        AudioEvents.PlayWorld3DAttached(profile.standUpSfx.eventId, emitter);
    }

    // ============================================================
    // RATTLE
    // ============================================================
    void TryPlayRattle()
    {
        if (profile == null) return;

        var movement = stateProvider != null ? stateProvider.Movement : null;
        if (movement == null) return;

        // best-effort: try to read MovementStateId from movement (if type known)
        // We assume movement exposes "Current" or "MovementStateId" or similar — but to keep loose coupling,
        // attempt to use available APIs. If stateProvider.Movement has "Current" property as enum MovementStateId we'll read it,
        // otherwise fallback to small heuristic (speed-based) - but for now assume standard API exposed in project.
        bool isSprint = false;
        bool isWalk = false;

        try
        {
            // common pattern in this project: Movement has property "Current" or "MovementStateId" of enum type
            var mv = movement.GetType().GetProperty("Current");
            if (mv != null)
            {
                var cur = mv.GetValue(movement);
                if (cur != null && cur.ToString().ToLower().Contains("sprint"))
                    isSprint = true;
                else if (cur != null && cur.ToString().ToLower().Contains("walk"))
                    isWalk = true;
            }
            else
            {
                // fallback to reading a "MovementStateId" int field or property
                var prop = movement.GetType().GetProperty("MovementStateId") ?? movement.GetType().GetProperty("State");
                if (prop != null)
                {
                    var cur = prop.GetValue(movement);
                    if (cur != null && cur.ToString().ToLower().Contains("sprint"))
                        isSprint = true;
                    else if (cur != null && cur.ToString().ToLower().Contains("walk"))
                        isWalk = true;
                }
            }
        }
        catch
        {
            // ignore reflection errors; if we can't detect, bail out
        }

        AudioEventSO sfx = null;
        float chance = 0f;

        if (isSprint)
        {
            sfx = profile.sprintRattleSfx;
            chance = profile.sprintRattleChance;
        }
        else if (isWalk)
        {
            sfx = profile.walkRattleSfx;
            chance = profile.walkRattleChance;
        }

        if (sfx == null || chance <= 0f) return;

        if (Random.value > chance) return;

        if (!IsWithinDistance(profile.maxFootstepDistance))
            return;

        Transform emitter = GetAudioEmitter();
        if (!emitter) return;

        AudioEvents.PlayWorld3DAttached(sfx.eventId, emitter);
    }

    // ============================================================
    // Helpers
    // ============================================================

    Vector3 GetFootRayOrigin()
    {
        if (footRayOriginOverride != null)
            return footRayOriginOverride.position;

        if (profile != null)
            return transform.position + Vector3.up * profile.footRaycastHeight;

        return transform.position + Vector3.up * 1.0f;
    }

    Transform GetAudioEmitter()
    {
        if (audioEmitterOverride != null)
            return audioEmitterOverride;

        return transform;
    }

    bool IsWithinDistance(float maxDist)
    {
        if (maxDist <= 0f) return true;

        var lis = GetListener();
        if (!lis) return true;

        float sq = (transform.position - lis.position).sqrMagnitude;
        return sq <= maxDist * maxDist;
    }

    static Transform GetListener()
    {
        if (s_listener && s_listener.gameObject.activeInHierarchy)
            return s_listener;

        if (Time.time < s_nextListenerSearchTime)
            return s_listener;

        s_nextListenerSearchTime = Time.time + 0.5f;

        var cam = Camera.main;
        if (cam)
            s_listener = cam.transform;

        return s_listener;
    }

    bool IsAuthority
    {
        get
        {
#if FUSION_WEAVER
            if (GameSession.Mode == AppPlayMode.Single)
                return true;

            if (_no == null)
                _no = GetComponentInParent<NetworkObject>();

            if (_no == null || _no.Runner == null)
                return true;

            return _no.HasStateAuthority;
#else
            return true;
#endif
        }
    }

    bool CanPlayLocalSfx
    {
        get
        {
#if FUSION_WEAVER
            if (GameSession.Mode == AppPlayMode.Single) return true;
            if (_no == null) _no = GetComponentInParent<NetworkObject>();
            if (_no == null || _no.Runner == null) return true;
            // ✅ ai cũng được phát SFX cục bộ (local cosmetic)
            return true;
#else
        return true;
#endif
        }
    }

}
