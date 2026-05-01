// Assets/Scripts/AI/Zombie/Attack/ZombieMeleeExecutor.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(ZombieBlackboard))]
public class ZombieMeleeExecutor : MonoBehaviour
{
    [Header("Data")]
    public ZombieAttackSet attackSet;
    public Transform attackOrigin;
    public Transform selfRoot;

    [Header("Hit Filtering")]
    public LayerMask hitMask = ~0;
    public bool oneHitPerWindow = true;

    [Header("Animator (optional)")]
    public Animator animator;
    ZombieNetworkAnimator netAnim;

    [Header("Execution Mode")]
    public bool requireAttackState = true;         // ← chỉ hoạt động khi Brain ở Attack
    public ZombieBrain brain;

    [Header("Audio")]
    public ZombieAudioDriver audioDriver;

    [Header("Debug")]
    public bool drawGizmos = true;
    public Color gizmoColor = new Color(1, 0, 0, 0.5f);

    ZombieBlackboard bb;
    Dictionary<ZombieAttackProfile, float> nextReadyTimes = new Dictionary<ZombieAttackProfile, float>();
    float nextGlobalReadyTime = 0f;

    public bool IsBusy => isSwinging || Time.time < nextGlobalReadyTime;

    // runtime swing state
    bool isSwinging;
    HashSet<int> hitThisWindow = new HashSet<int>();

    void Awake()
    {
        bb = GetComponent<ZombieBlackboard>();
        if (attackOrigin == null) attackOrigin = transform;
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (brain == null) brain = GetComponent<ZombieBrain>();
        if (audioDriver == null) audioDriver = GetComponent<ZombieAudioDriver>();

        netAnim = GetComponent<ZombieNetworkAnimator>();

        // +++ NEW: nếu chưa set trong Inspector thì mặc định chính GameObject này là gốc của Zombie
        if (selfRoot == null) selfRoot = transform;

        //   Debug.Log($"[ZME v1.4] Awake on '{name}' | attackSet={(attackSet ? attackSet.profiles?.Length : 0)} | brain={(brain ? "yes" : "no")} | selfRoot={selfRoot.name}");
    }

    void Update()
    {
        // Đang có barricade chặn cửa?
        bool hasBarricade = brain != null && brain.HasBlockingBarricade;

        // Nếu không có player target VÀ cũng không có barricade → khỏi attack
        if (!bb.HasTarget() && !hasBarricade)
            return;

        if (attackSet == null || !attackSet.HasProfiles)
            return;

        // Nếu yêu cầu đúng state Attack mà Brain chưa ở Attack → thôi
        if (requireAttackState && (!brain || brain.current != ZombieBrain.State.Attack))
            return;

        if (isSwinging) return;
        if (Time.time < nextGlobalReadyTime) return;

        var prof = SelectValidProfile();
        if (prof == null) return;

        StartCoroutine(DoAttack(prof));
    }


    ZombieAttackProfile SelectValidProfile()
    {
        bool hasBarricade = brain != null && brain.HasBlockingBarricade;

        Vector3 focusPos;
        float dist;

        if (hasBarricade && brain.CurrentBarricadePoint != null)
        {
            // Khi phá cửa: đo khoảng cách tới điểm đứng đập cửa
            focusPos = brain.CurrentBarricadePoint.position;
            dist = Vector3.Distance(selfRoot.position, focusPos);
        }
        else
        {
            var t = bb.target;
            if (!t) return null;

            focusPos = t.position;
            dist = bb.distanceToTarget;
        }

        // Lọc profile hợp lệ theo range + cooldown + LOS + facing
        List<ZombieAttackProfile> candidates = new List<ZombieAttackProfile>();
        foreach (var p in attackSet.profiles)
        {
            if (p == null) continue;

            // cooldown theo chiêu
            if (nextReadyTimes.TryGetValue(p, out var readyAt) && Time.time < readyAt)
                continue;

            // range (dùng dist tới barricade hoặc player tuỳ case)
            if (dist < p.minRange || dist > p.maxRange)
                continue;

            // LOS + Facing:
            //  - Đánh player: giữ nguyên như cũ
            //  - Đánh barricade: bỏ LOS (vì có thể không nhìn thấy player), vẫn giữ facing
            if (!hasBarricade)
            {
                if (p.requireLOS && !bb.hasLOS) continue;
                if (p.facingAngle < 179.9f && !IsFacingTarget(focusPos, p.facingAngle)) continue;
            }
            else
            {
                if (p.facingAngle < 179.9f && !IsFacingTarget(focusPos, p.facingAngle)) continue;
            }

            candidates.Add(p);
        }

        if (candidates.Count == 0) return null;

        switch (attackSet.selectionMode)
        {
            case AttackSelectMode.FirstValid:
                return candidates[0];

            case AttackSelectMode.WeightedRandom:
                float total = 0f;
                foreach (var c in candidates) total += Mathf.Max(0.0001f, c.weight);
                float r = Random.value * total;
                foreach (var c in candidates)
                {
                    r -= Mathf.Max(0.0001f, c.weight);
                    if (r <= 0f) return c;
                }
                return candidates[candidates.Count - 1];

            case AttackSelectMode.HighestPriority:
            default:
                candidates.Sort((a, b) => b.priority.CompareTo(a.priority));
                return candidates[0];
        }
    }


    IEnumerator DoAttack(ZombieAttackProfile p)
    {
        isSwinging = true;

        // Dừng chuyển động nếu chiêu không cho phép move
        bool prevIsKinematicMove = false;
        var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        bool hasNav = agent && agent.isOnNavMesh;
        if (hasNav)
        {
            prevIsKinematicMove = agent.isStopped;
            agent.isStopped = !p.allowMoveDuringAttack;
        }


        // Sync anim attack qua mạng (host phát, mọi client nhận)
        bool isMultiplayer = GameSession.Mode != AppPlayMode.Single;

        // LUÔN thử dùng netAnim trước (nó đã được sửa để hỗ trợ cả SP)
        if (netAnim != null)
        {
            netAnim.PlayAttack(p.animationIndex);
        }
        // Fallback: nếu không có netAnim hoặc netAnim không hoạt động
        else if (animator != null)
        {
            animator.SetInteger("AttackIndex", p.animationIndex);
            yield return null;
            animator.SetTrigger("DoAttack");
        }

        if (audioDriver != null)
            audioDriver.OnAttackWindup();

        //      Debug.Log("TryAttack: sending anim " + p.animationIndex);

        /*        Debug.Log("ATTACK TEST: AttackIndex set to " + p.animationIndex + "  | current animator value = "
                  + animator.GetInteger("AttackIndex"));*/


        // Hướng về target trong windup
        float windupEnd = Time.time + p.windupTime;
        while (Time.time < windupEnd)
        {
            if (bb.HasTarget())
                FaceTowards(bb.target.position);
            yield return null;
        }

        // Active window chính
        yield return StartCoroutine(RunActiveWindow(p.activeTime, p));

        // Active windows bổ sung (nếu khai báo)
        if (p.extraActiveWindows != null)
        {
            foreach (var w in p.extraActiveWindows)
            {
                // w.x: delay sau windup/đòn trước, w.y: duration window
                if (w.x > 0) yield return new WaitForSeconds(w.x);
                yield return StartCoroutine(RunActiveWindow(w.y, p));
            }
        }

        // Recovery
        if (p.recoveryTime > 0f)
            yield return new WaitForSeconds(p.recoveryTime);

        // Cooldowns
        nextReadyTimes[p] = Time.time + p.cooldown;
        if (attackSet.globalCooldown > 0f)
            nextGlobalReadyTime = Time.time + attackSet.globalCooldown;

        // Restore move
        if (hasNav)
            agent.isStopped = prevIsKinematicMove;

        isSwinging = false;
    }

    IEnumerator RunActiveWindow(float duration, ZombieAttackProfile p)
    {
        hitThisWindow.Clear();
        float end = Time.time + Mathf.Max(0f, duration);
        //   Debug.Log($"[ZombieMeleeExecutor] >>> Active window start: {p.displayName}, duration={duration:F2}s");

        while (Time.time < end)
        {
            DoHitCheckOnce(p);
            yield return null; // check mỗi frame để bắt chuyển động của target
        }
    }

    void DoHitCheckOnce(ZombieAttackProfile p)
    {
        bool hasBarricade = brain != null && brain.HasBlockingBarricade;

        // Không có player target VÀ cũng không có barricade → khỏi check
        if (!bb.HasTarget() && !hasBarricade)
            return;

        // --- Tính hình học vùng chém/cắn ---
        Vector3 origin = attackOrigin ? attackOrigin.position : transform.position;
        Quaternion rot = attackOrigin ? attackOrigin.rotation : transform.rotation;
        Vector3 center = origin + rot * p.shapeLocalOffset;

        Collider[] hits = null;
        switch (p.shape)
        {
            case ZombieHitShape.Sphere:
                hits = Physics.OverlapSphere(center, p.radius, hitMask, QueryTriggerInteraction.Collide);
                break;
            case ZombieHitShape.Capsule:
                {
                    Vector3 half = rot * Vector3.forward * Mathf.Max(0f, (p.height * 0.5f - p.radius));
                    Vector3 a = center - half, b = center + half;
                    hits = Physics.OverlapCapsule(a, b, p.radius, hitMask, QueryTriggerInteraction.Collide);
                    break;
                }
            case ZombieHitShape.Box:
                hits = Physics.OverlapBox(center, p.boxHalfExtents, rot, hitMask, QueryTriggerInteraction.Collide);
                break;
        }
        if (hits == null || hits.Length == 0) return;

        foreach (var col in hits)
        {
            if (!col) continue;

            // Bỏ collider thuộc chính zombie
            if (col.transform.IsChildOf(selfRoot))
                continue;

            // ─────────────────────────────
            //  1) BarricadeHitReceiver (cửa)
            // ─────────────────────────────
            var barricade = col.GetComponentInParent<BarricadeHitReceiver>(true);
            if (barricade != null && barricade.Window != null)
            {
                int victimKey = barricade.Window.GetInstanceID();
                if (oneHitPerWindow && hitThisWindow.Contains(victimKey))
                    continue;

                hitThisWindow.Add(victimKey);
                barricade.OnHitByZombie();
                continue;   // không xử lý damage player / IDamageable cho collider này
            }

            // ─────────────────────────────
            //  2) Phần còn lại: player / IDamageable như cũ
            // ─────────────────────────────

            // Tính hướng/điểm va chạm để log & knockback
            Vector3 dir = (col.transform.position - center);
            if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;
            Vector3 hitPoint = col.ClosestPoint(center);
            Vector3 hitNormal = -dir.normalized;

            if (p.knockback > 0f && col.attachedRigidbody != null)
                col.attachedRigidbody.AddForce(dir.normalized * p.knockback, ForceMode.VelocityChange);

            // Tìm IDamageable
            IDamageable victimIdmg = null;
            GameObject victimRoot = null;

            // 1) Ưu tiên PlayerLifeController
            var playerLife = col.GetComponentInParent<PlayerLifeController>(true);
            bool isPlayerTarget = playerLife != null;
            if (playerLife != null)
            {
                victimIdmg = playerLife;
                victimRoot = playerLife.gameObject;
            }
            else
            {
                // 2) Fallback IDamageable chung
                victimIdmg = col.GetComponentInParent<IDamageable>(true);
                if (victimIdmg == null)
                    victimIdmg = col.GetComponent<IDamageable>();

                if (victimIdmg != null)
                {
                    if (victimIdmg is Component comp)
                        victimRoot = comp.gameObject;
                    else
                        victimRoot = col.gameObject;
                }
            }

            if (victimIdmg == null)
                continue;

            int victimKey2 = victimRoot.GetInstanceID();
            if (oneHitPerWindow && hitThisWindow.Contains(victimKey2))
                continue;
            hitThisWindow.Add(victimKey2);

            // (Tuỳ chọn) Chặn bắn đồng đội sớm
            // --- Bỏ qua đồng đội sớm (double-guard): không gọi DamageSystem, không log ---
            var myIdmg = GetComponentInParent<IDamageable>();
            if (myIdmg != null && victimIdmg != null && victimIdmg.GetTeam() == myIdmg.GetTeam())
            {
                continue;
            }


            // Log trước khi Apply
            //  Debug.Log($"[ZME] READY to apply: targetRoot='{victimRoot.name}', team={victimIdmg.GetTeam()}, collider='{col.name}'");

            // Lập DamageEvent và Apply
            var e = new DamageEvent
            {
                attacker = gameObject,
                victimGO = victimRoot,
                victim = victimIdmg,
                weaponId = "zombie_bite",
                baseDamage = p.damage,
                damageType = DamageType.Melee,
                source = DamageSource.AI,
                distance = bb.distanceToTarget,
                hitPoint = hitPoint,
                hitNormal = hitNormal,
                shotDirection = -hitNormal,
                hitCollider = col,
                impactDirection = dir.normalized,
                impactForce = p.knockback,
                time = Time.time
            };

            try
            {
                var result = DamageRouter.Apply(e);

                if (result.isApplied && isPlayerTarget)
                {
                    // SP: phát trực tiếp (vì không có net)
                    if (GameSession.Mode == AppPlayMode.Single)
                    {
                        if (audioDriver != null) audioDriver.OnAttackHitPlayer();
                    }
                    else
                    {
#if FUSION_WEAVER
                        // MP: Melee chỉ chạy ở host -> pulse để clients cũng nghe
                        var zs = GetComponent<ZombieStateNet>();
                        zs?.PulseHitPlayer();
#endif
                    }
                }
                //    Debug.Log($"[ZME] AFTER Apply() -> isApplied={result.isApplied}, final={result.finalDamage}, remain={result.remainingHealth}");

            }
            catch (System.Exception ex)
            {
                Debug.LogError("[ZME] EXCEPTION when calling DamageSystem.Apply: " + ex);
            }
            // ======= END DAMAGE BLOCK =======
        }
    }


    bool IsFacingTarget(Vector3 targetPos, float maxAngle)
    {
        Vector3 to = targetPos - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return true;
        float ang = Vector3.Angle(transform.forward, to);
        return ang <= maxAngle * 0.5f;
    }

    void FaceTowards(Vector3 worldPos)
    {
        Vector3 dir = worldPos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
        {
            var targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 12f * Time.deltaTime);
        }
    }

    public void TryAttackOnce()
    {
        bool hasBarricade = brain != null && brain.HasBlockingBarricade;

        if (!bb.HasTarget() && !hasBarricade)
            return;
        if (attackSet == null || !attackSet.HasProfiles)
            return;
        if (IsBusy) return;

        var prof = SelectValidProfile();
        if (prof == null) return;

        StartCoroutine(DoAttack(prof));
        // Debug.Log($"[ZME] TryAttackOnce -> {prof.displayName}");
    }


    void OnDrawGizmosSelected()
    {
        if (!drawGizmos || attackSet == null || attackOrigin == null) return;
        // vẽ theo profile đầu tiên (minh hoạ)
        var p = (attackSet.profiles != null && attackSet.profiles.Length > 0) ? attackSet.profiles[0] : null;
        if (p == null) return;

        Gizmos.color = gizmoColor;
        var rot = attackOrigin.rotation;
        var center = attackOrigin.position + rot * p.shapeLocalOffset;

        switch (p.shape)
        {
            case ZombieHitShape.Sphere:
                Gizmos.DrawWireSphere(center, p.radius);
                break;
            case ZombieHitShape.Capsule:
                {
                    Vector3 half = rot * Vector3.forward * Mathf.Max(0f, (p.height * 0.5f - p.radius));
                    Vector3 a = center - half;
                    Vector3 b = center + half;
                    DrawWireCapsule(a, b, p.radius);
                    break;
                }
            case ZombieHitShape.Box:
                Gizmos.matrix = Matrix4x4.TRS(center, rot, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, p.boxHalfExtents * 2f);
                Gizmos.matrix = Matrix4x4.identity;
                break;
        }
    }

    // Vẽ capsule minh hoạ (đơn giản)
    void DrawWireCapsule(Vector3 a, Vector3 b, float r)
    {
        // thân
        Gizmos.DrawLine(a + Vector3.up * r, b + Vector3.up * r);
        Gizmos.DrawLine(a - Vector3.up * r, b - Vector3.up * r);
        Gizmos.DrawLine(a + Vector3.right * r, b + Vector3.right * r);
        Gizmos.DrawLine(a - Vector3.right * r, b - Vector3.right * r);
        // hai đầu cầu (chỉ là minh hoạ đơn giản)
        Gizmos.DrawWireSphere(a, r);
        Gizmos.DrawWireSphere(b, r);
    }

    string FullPath(Transform t)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder(t.name);
        while (t.parent != null) { t = t.parent; sb.Insert(0, t.name + "/"); }
        return sb.ToString();
    }

    public void ResetExecutor()
    {
        StopAllCoroutines();
        isSwinging = false;

        var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent && agent.enabled && agent.isOnNavMesh)
            agent.isStopped = false;
    }
}
