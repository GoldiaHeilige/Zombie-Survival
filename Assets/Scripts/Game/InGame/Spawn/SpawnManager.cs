using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

#if FUSION_WEAVER
using Fusion;
#endif

public class SpawnManager : MonoBehaviour
{
    [Header("Scene refs")]
    public List<SpawnPoint> spawnPoints = new List<SpawnPoint>();
    public SpawnRules rules;

    [Header("Spawn Collision Check")]
    [SerializeField] bool checkSpawnCollision = true;

    // bán kính “thân” zombie ~ radius của capsule / NavMeshAgent
    [SerializeField] float spawnCollisionRadius = 0.4f;

    // chiều cao capsule để check va chạm
    [SerializeField] float spawnCollisionHeight = 1.8f;

    // layer môi trường rắn (tường, props…), KHÔNG include player/zombie
    [SerializeField] LayerMask spawnCollisionMask = ~0;


    Transform _player;

    // All active players (for MP-safe spawn blocking)
    readonly List<Transform> _activePlayers = new List<Transform>();

    AIPortHub _hub;
    RoundDirector _roundDirector;
    [SerializeField] bool requirePorts = true;

    Camera _cam;
    readonly List<Vector3> _pickedPositions = new(); // toàn bộ điểm trong 1 burst (để separation)

    public bool HasPlayer() => _player != null;

    public static System.Action<Transform> OnLocalPlayerBound;

    public struct SpawnResult
    {
        public int spawnedCount;
        public int totalCost;
    }

    void Awake()
    {
        // --- NEW: chỉ Host (StateAuthority) mới để SpawnManager chạy ---
#if FUSION_WEAVER
        var runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
        if (runner != null && !(runner.IsServer || runner.IsSharedModeMasterClient))
        {
            // Client không spawn → tắt hẳn, và đừng yêu cầu ports
            requirePorts = false;
            enabled = false;
            return;
        }
#endif

        _hub = FindFirstObjectByType<AIPortHub>(FindObjectsInactive.Include);
        if (requirePorts && (_hub == null || _hub.Spawn == null || _hub.Random == null))
        {
            Debug.LogError("[SpawnManager] AIPortHub/ports missing.", this);
            enabled = false; return;
        }

        _roundDirector = RoundDirector.Instance != null
    ? RoundDirector.Instance
    : FindFirstObjectByType<RoundDirector>(FindObjectsInactive.Include);


        if (spawnPoints == null) spawnPoints = new List<SpawnPoint>();
        if (spawnPoints.Count == 0)
        {
            var found = FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            spawnPoints.AddRange(found);
            Debug.Log($"[SpawnManager] Auto-collected SpawnPoints: {spawnPoints.Count}", this);
        }

        // Track players for spawn-distance blocking (MP)
        PlayerRegistry.OnPlayerRegistered += OnPlayerRegistered;
        PlayerRegistry.OnPlayerUnregistered += OnPlayerUnregistered;

        // Pre-fill (in case players spawned before SpawnManager Awake)
        foreach (var p in PlayerRegistry.Players)
        {
            if (p != null) _activePlayers.Add(p.transform);
        }

    }

    public void BindLocalPlayer(Transform t)
    {
        if (t == null) return;

#if FUSION_WEAVER
        if (t.TryGetComponent<NetworkObject>(out var no))
        {
            // Chỉ chặn khi runner đang chạy và object KHÔNG có input authority.
            // Trường hợp single (không có runner) vẫn cho bind.
            if (no.Runner != null && !no.HasInputAuthority) return;
        }
#endif


        _player = t;
        LazyFindCamera();
        Debug.Log($"[SpawnManager] Bound local player: {_player.name}");

        OnLocalPlayerBound?.Invoke(_player);
    }

    public void UnbindPlayer(Transform t)
    {
        if (_player == t) _player = null;
    }


    void OnDestroy()
    {
        PlayerRegistry.OnPlayerRegistered -= OnPlayerRegistered;
        PlayerRegistry.OnPlayerUnregistered -= OnPlayerUnregistered;
    }

    void OnPlayerRegistered(PlayerRefs refs)
    {
        if (refs == null) return;
        var t = refs.transform;
        if (t == null) return;
        if (!_activePlayers.Contains(t)) _activePlayers.Add(t);
    }

    void OnPlayerUnregistered(PlayerRefs refs)
    {
        if (refs == null) return;
        _activePlayers.Remove(refs.transform);
    }

    float GetMinDistanceToAnyPlayer(Vector3 pos)
    {
        float min = float.PositiveInfinity;

        for (int i = _activePlayers.Count - 1; i >= 0; i--)
        {
            var t = _activePlayers[i];
            if (t == null) { _activePlayers.RemoveAt(i); continue; }
            float d = Vector3.Distance(t.position, pos);
            if (d < min) min = d;
        }

        if (float.IsPositiveInfinity(min))
        {
            if (_player != null) return Vector3.Distance(_player.position, pos);
            return 9999f;
        }

        return min;
    }


    void LazyFindPlayer()
    {
        if (_player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) _player = p.transform;
        }
    }

    private void LazyFindCamera()
    {
        if (_cam != null) return;
        _cam = Camera.main;
    }

    public SpawnResult SpawnBurst(WaveProfile wave, int alive, int spent, int roundIndex)
    {
        LazyFindPlayer();
        var res = new SpawnResult();

        if (_player == null || wave == null || wave.allowTypes == null || wave.allowTypes.Count == 0)
            return res;

        int room = Mathf.Max(0, wave.concurrencyCap - alive);
        if (room <= 0) return res;

        int toSpawn = Mathf.Min(wave.spawnBurst, room);

        // Xoá danh sách vị trí đã chọn (separation trong cùng burst)
        _pickedPositions.Clear();

        // ---- NEW: safety + ban list (per-burst) ----
        int safety = 512; // chặn mọi khả năng lặp vô hạn
        _bannedDefsThisBurst.Clear();
        int failedPickPointCount = 0;

        // Lặp cho đến khi đạt toSpawn cá thể hoặc hết budget/điểm
        while (res.spawnedCount < toSpawn && safety-- > 0)
        {
            int remainingBudget = wave.budgetPoints - (spent + res.totalCost);
            if (remainingBudget <= 0) break;

            // Lấy 1 EnemyDefinition hợp lệ theo weight + budget, bỏ qua các def vừa bị ban ở burst này
            var def = PickEnemyDefWeighted(wave.allowTypes, remainingBudget, roundIndex, _bannedDefsThisBurst);
            if (def == null) break; // không còn loại nào phù hợp budget

            // Quyết định kích thước nhóm theo groupSizeRange
            int minG = Mathf.Max(1, def.groupSizeRange.x);
            int maxG = Mathf.Max(minG, def.groupSizeRange.y);
            int wantGroup = _hub.Random.RangeInt(minG, maxG + 1);

            // Clamp bởi room còn lại trong burst
            int canByRoom = toSpawn - res.spawnedCount;
            wantGroup = Mathf.Min(wantGroup, canByRoom);

            // Clamp bởi budget còn lại
            int canByBudget = remainingBudget / Mathf.Max(1, def.cost);
            wantGroup = Mathf.Min(wantGroup, canByBudget);

            if (wantGroup <= 0) break;

            // Chọn 1 SpawnPoint làm "tâm" nhóm
            var point = PickSpawnPoint();
            if (point == null)
            {
                failedPickPointCount++;
                if (failedPickPointCount >= 3)
                {
                    Debug.LogWarning("[SpawnManager] No valid SpawnPoint for this burst after multiple tries. Abort burst early.");
                    break;
                }
                // thử lại vòng kế tiếp với def khác
                _bannedDefsThisBurst.Add(def);
                continue;
            }

            // Cố gắng tìm ra đến 'wantGroup' vị trí gần tâm (mỗi vị trí vẫn qua FOV/LOS/separation)
            _groupPositions.Clear();
            int tries = 0;
            const int maxTries = 32; // tránh vòng lặp vô hạn nếu map chật
            while (_groupPositions.Count < wantGroup && tries < maxTries)
            {
                tries++;
                if (TryFindPositionNear(point.transform.position, out var pos))
                {
                    // Kiểm tra separation với chính nội nhóm (ngoài _pickedPositions)
                    bool ok = true;
                    float minSep = rules ? rules.minSeparationBetweenSpawns : 0f;
                    if (minSep > 0f)
                    {
                        for (int i = 0; i < _groupPositions.Count; i++)
                        {
                            if (Vector3.SqrMagnitude(pos - _groupPositions[i]) < (minSep * minSep))
                            {
                                ok = false;
                                break;
                            }
                        }
                    }
                    if (ok) _groupPositions.Add(pos);
                }
            }

            if (_groupPositions.Count == 0)
            {
                // Không tìm được chỗ cho nhóm này → co rút còn 1 cá thể thử lại
                if (wantGroup > 1)
                {
                    // thử lại nhóm cỡ 1
                    int triesSolo = 0;
                    bool spawnedSolo = false;

                    while (triesSolo++ < 4)
                    {
                        if (TryFindPositionNear(point.transform.position, out var soloPos))
                        {
                            // Spawn 1 cá thể                        
                            var go1 = _hub.Spawn.Spawn(def, soloPos, Quaternion.identity);

                            var ticket1 = go1.GetComponent<EnemySpawnHandle>();
                            if (ticket1 == null) ticket1 = go1.AddComponent<EnemySpawnHandle>();
                            ticket1.MarkSpawned(def.cost);

                            if (go1.GetComponent<DamageableDeathBridge>() == null)
                                go1.AddComponent<DamageableDeathBridge>();

                            res.spawnedCount++;
                            res.totalCost += def.cost;
                            _pickedPositions.Add(soloPos);

                            spawnedSolo = true;
                            break;
                        }
                    }

                    if (!spawnedSolo)
                    {
                        // Ban def này trong burst hiện tại để tránh lặp vô hạn rồi thử def khác
                        _bannedDefsThisBurst.Add(def);
                        continue;
                    }
                }
                else
                {
                    // Nhóm đã là 1 mà vẫn không đặt được -> ban def này trong burst để tránh loop
                    _bannedDefsThisBurst.Add(def);
                    continue;
                }
            }
            else
            {
                // Nếu tìm được ít hơn wantGroup, chúng ta spawn bấy nhiêu (linh hoạt)
                int spawnThisGroup = _groupPositions.Count;

                // Instantiate từng cá thể trong nhóm
                for (int i = 0; i < spawnThisGroup; i++)
                {
                    var pos = _groupPositions[i];

                    var go = _hub.Spawn.Spawn(def, pos, Quaternion.identity);

                    var agent = go.GetComponent<UnityEngine.AI.NavMeshAgent>();
                    if (agent && !agent.isOnNavMesh)
                    {
                        if (NavMesh.SamplePosition(go.transform.position, out var hit, 2f, NavMesh.AllAreas))
                            go.transform.position = hit.position;
                    }

                    // Gắn "phiếu" & báo spawn (đếm alive/budget)
                    var ticket = go.GetComponent<EnemySpawnHandle>();
                    if (ticket == null) ticket = go.AddComponent<EnemySpawnHandle>();
                    ticket.MarkSpawned(def.cost);

                    // (Tuỳ) đảm bảo có Bridge nếu prefab thiếu
                    if (go.GetComponent<DamageableDeathBridge>() == null)
                        go.AddComponent<DamageableDeathBridge>();

                    res.spawnedCount++;
                    res.totalCost += def.cost;

                    _pickedPositions.Add(pos);

                    // Nếu đã đạt giới hạn của burst hoặc budget, dừng sớm
                    if (res.spawnedCount >= toSpawn) break;
                    remainingBudget = wave.budgetPoints - (spent + res.totalCost);
                    if (remainingBudget < def.cost) break;
                }
            }

            // Đánh cooldown cho SpawnPoint sau khi bơm nhóm (chỉ khi có point hợp lệ)
            point.PunchCooldown();

            // Nếu đã đầy burst → dừng
            if (res.spawnedCount >= toSpawn) break;
        }

        if (safety <= 0)
        {
            Debug.LogError("[SpawnManager] Safety break reached — possible bad spawn conditions (FOV/LOS/separation too strict?).");
        }

        if (res.spawnedCount > 0)
        {
            Debug.Log($"[SpawnManager] Burst OK: +{res.spawnedCount} (local cost {res.totalCost}) | " +
                      $"spentSnap={spent} -> will be {spent + res.totalCost}/{wave.budgetPoints}");
        }
        else
        {
            Debug.Log($"[SpawnManager] Burst skipped (no room/point/budget).");
        }

        return res;
    }

    SpawnPoint PickSpawnPoint()
    {
        LazyFindPlayer();
        if (_player == null)
        {
            if (rules != null && rules.verboseLogs) Debug.LogWarning("[SpawnManager] PickSpawnPoint: _player null.");
            return null;
        }
        if (spawnPoints == null || spawnPoints.Count == 0)
        {
            Debug.LogWarning("[SpawnManager] No SpawnPoints assigned.");
            return null;
        }

        _candidates.Clear();
        _scores.Clear();

        foreach (var sp in spawnPoints)
        {
            if (sp == null || sp.IsOnCooldown) continue;

            var gate = sp.GetComponent<SpawnPointZoneGate>();
            if (gate != null && !gate.IsZoneOpenForSpawn)
                continue;

            float d = GetMinDistanceToAnyPlayer(sp.transform.position);
            float minDist = Mathf.Max(sp.minPlayerDistance, rules ? rules.minPlayerDistance : 0f);
            float maxDist = rules ? rules.maxPlayerDistance : 9999f;
            if (d < minDist || d > maxDist) continue;

            // chấm điểm: Ở gần minDist hơn thì điểm cao hơn
            float baseW = Mathf.Max(0.0001f, sp.weight);

            // t sẽ = 0 khi d = minDist, = 1 khi d = maxDist
            float t = Mathf.InverseLerp(minDist, maxDist, d);
            // distScore = 1 khi d gần minDist (gần player), = 0 khi d gần maxDist (xa player)
            float distScore = 1f - t;

            float safeScore = 1f;

            if (rules != null && (rules.checkFOV || rules.checkLOS))
            {
                bool fovOk = IsOutsidePlayerFOV(sp.transform.position);
                bool losOk = IsLOSBlockedForPlayer(sp.transform.position);
                if (rules.checkFOV) safeScore *= fovOk ? 1.2f : 0.5f;
                if (rules.checkLOS) safeScore *= losOk ? 1.2f : 0.5f;
            }

            float score = baseW * (0.5f + 0.5f * distScore) * safeScore;
            _candidates.Add(sp);
            _scores.Add(score);
        }

        if (_candidates.Count == 0)
        {
            if (rules != null && rules.verboseLogs)
                Debug.LogWarning("[SpawnManager] No valid SpawnPoint after visibility/distance checks.");
            return null;
        }

        float total = 0f;
        for (int i = 0; i < _scores.Count; i++) total += Mathf.Max(0.0001f, _scores[i]);
        float r = _hub.Random.RangeFloat(0f, 1f) * total, a = 0f;
        for (int i = 0; i < _candidates.Count; i++)
        {
            a += Mathf.Max(0.0001f, _scores[i]);
            if (r <= a) return _candidates[i];
        }
        return _candidates[0];
    }

    readonly List<float> _scores = new List<float>();
    readonly List<SpawnPoint> _candidates = new List<SpawnPoint>();
    readonly List<Vector3> _groupPositions = new List<Vector3>(8);
    readonly HashSet<EnemyDefinition> _bannedDefsThisBurst = new HashSet<EnemyDefinition>();

    bool TryFindPositionNear(Vector3 center, out Vector3 pos)
    {
        float sampleR = rules ? rules.sampleRadius : 1.2f;
        int tries = rules ? rules.maxSampleTries : 6;

        for (int i = 0; i < tries; i++)
        {
            Vector2 v = _hub.Random.InsideUnitCircle();
            Vector3 sph = new Vector3(v.x, 0f, v.y);
            Vector3 rnd = center + sph * sampleR * (1f + i * 0.35f);
            rnd.y = center.y;

            if (NavMesh.SamplePosition(rnd, out var hit, sampleR, NavMesh.AllAreas))
            {
                var p = hit.position;

                // FOV / LOS
                if (!IsOutsidePlayerFOV(p)) continue;
                if (!IsLOSBlockedForPlayer(p)) continue;

                // Separation trong cùng burst (toàn bộ các vị trí đã pick trước đó trong burst)
                if (!RespectBurstSeparation(p)) continue;

                if (!IsSpawnPositionFree(p))
                {
                    // vị trí này va chạm môi trường -> thử chỗ khác
                    continue;
                }

                pos = p;
                return true;
            }
        }

        pos = center;
        return false;
    }

    bool IsSpawnPositionFree(Vector3 pos)
    {
        if (!checkSpawnCollision)
            return true;

        float r = Mathf.Max(0.01f, spawnCollisionRadius);
        float bodyHeight = Mathf.Max(spawnCollisionHeight, r * 2f);

        // Nhấc capsule lên một chút để KHÔNG chạm sàn
        const float bottomOffset = 0.5f; // 20cm, tuỳ sửa thêm trong inspector nếu muốn
        Vector3 center = pos + Vector3.up * (bottomOffset + bodyHeight * 0.5f);

        Vector3 up = Vector3.up * (bodyHeight * 0.5f - r);
        Vector3 p1 = center + up;
        Vector3 p2 = center - up;

        bool blocked = Physics.CheckCapsule(
            p1, p2, r,
            spawnCollisionMask,
            QueryTriggerInteraction.Ignore);

        return !blocked;
    }

    bool IsOutsidePlayerFOV(Vector3 worldPos)
    {
        if (rules == null || !rules.checkFOV) return true;

        LazyFindCamera();
        if (_cam == null) return true; // không có camera thì bỏ qua

        Vector3 eye = _cam.transform.position;
        Vector3 dir = (worldPos - eye);
        float dot = Vector3.Dot(_cam.transform.forward, dir.normalized);
        float angleDeg = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
        if (angleDeg > rules.cameraConeAngle * 0.5f) return true; // nằm ngoài nón -> OK

        Vector3 vp = _cam.WorldToViewportPoint(worldPos);
        if (vp.z < 0f) return true; // phía sau camera
        float pad = Mathf.Clamp01(rules.viewportEdgePadding);
        bool inside = (vp.x > pad && vp.x < 1f - pad && vp.y > pad && vp.y < 1f - pad);
        return !inside; // nếu đang trong khung nhìn thì KHÔNG OK
    }

    bool IsLOSBlockedForPlayer(Vector3 worldPos)
    {
        if (rules == null || !rules.checkLOS) return true;

        LazyFindCamera();
        if (_cam == null) return true;

        Vector3 eye = _cam.transform.position;
        Vector3 to = worldPos - eye;
        float dist = to.magnitude;
        if (dist <= 0.1f) return false;

        bool hit = Physics.Raycast(eye, to.normalized, out var rh, dist, rules.losObstructionMask, QueryTriggerInteraction.Ignore);
        return hit; // true = bị che chắn -> an toàn
    }

    bool RespectBurstSeparation(Vector3 candidate)
    {
        if (rules == null) return true;
        float minSep = rules.minSeparationBetweenSpawns;
        if (minSep <= 0f) return true;

        foreach (var picked in _pickedPositions)
            if (Vector3.SqrMagnitude(candidate - picked) < (minSep * minSep))
                return false;

        return true;
    }

    EnemyDefinition PickEnemyDefWeighted(IList<EnemyDefinition> allowed, int remainingBudget, int roundIndex, HashSet<EnemyDefinition> banList = null)
    {
        _defCandidates.Clear();
        float totalW = 0f;
        for (int i = 0; i < allowed.Count; i++)
        {
            var def = allowed[i];
            if (def == null || def.prefab == null) continue;
            if (def.cost > remainingBudget) continue;
            if (banList != null && banList.Contains(def)) continue;

            float rawW = (_roundDirector != null)
                ? _roundDirector.GetEffectiveEnemyWeight(def, roundIndex)
                : def.weight;

            float w = Mathf.Max(0.0001f, rawW);

            _defCandidates.Add((def, w));
            totalW += w;
        }

        if (_defCandidates.Count == 0) return null;

        float roll = _hub.Random.RangeFloat(0f, 1f) * totalW, acc = 0f;
        foreach (var (def, w) in _defCandidates)
        {
            acc += w;
            if (roll <= acc) return def;
        }
        return _defCandidates[0].def;
    }

    readonly System.Collections.Generic.List<(EnemyDefinition def, float w)> _defCandidates =
        new System.Collections.Generic.List<(EnemyDefinition, float)>();
}
