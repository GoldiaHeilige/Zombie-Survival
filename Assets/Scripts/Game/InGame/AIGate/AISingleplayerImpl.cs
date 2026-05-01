using System;
using UnityEngine;

/// SPIml cho hệ AI/Wave ở chế độ Singleplayer.
/// - Cung cấp các "dịch vụ" hạ tầng cho core thông qua các port mỏng:
///   ISpawnPort, IRandomPort, ITargetQuery, IDeathEvents, IGameStartPort
/// - Không tự tick AI/Director; chỉ là adapter gọi sang hệ SP hiện có.
/// - Đặt script này vào container:  AI_SP_Impl
/// - Core sẽ lấy reference tới các port này (DI/AutoBind) và gọi qua interface.
///
/// LƯU Ý: Tên class/hàm phía dưới match với code SP bạn đang có:
///   - Spawn/Despawn: ZombiePoolManager
///   - Targeting:     TargetService
///   - Start round:   RoundDirector.RequestManualStart()
///   - Random:        UnityEngine.Random
///
/// Nếu tên hàm khác chút trong dự án của bạn, bạn chỉ việc đổi 1–2 dòng ở đây.
public class AISingleplayerImpl :
    MonoBehaviour, ISpawnPort, IRandomPort, ITargetQuery, IDeathEvents, IGameStartPort
{
    [Header("References (auto-find nếu để trống)")]
    [SerializeField] private ZombiePoolManager poolManager;
    [SerializeField] private TargetService targetService;
    [SerializeField] private RoundDirector roundDirector;

    private AIPortHub _hub;
    private Coroutine _bootCo;

    void OnEnable()
    {
        if (_bootCo != null) StopCoroutine(_bootCo);
        _bootCo = StartCoroutine(BootAndRegister());
    }

    System.Collections.IEnumerator BootAndRegister()
    {
        // Đợi hub xuất hiện/active (tránh fail-fast khi scene load nặng)
        float timeout = 5f;
        float t = 0f;

        while (t < timeout)
        {
            if (_hub == null)
                _hub = FindFirstObjectByType<AIPortHub>(FindObjectsInactive.Include);

            if (_hub != null)
                break;

            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (_hub == null)
        {
            Debug.LogError("[SPIml] AIPortHub not found in scene. Add AIPortHub to AI_SystemRoot.", this);
            enabled = false;
            yield break;
        }

        // Register ports
        _hub.Spawn = this;
        _hub.Random = this;
        _hub.Target = this;
        _hub.GameStart = this;
        _hub.DeathEvents = this;

        _bootCo = null;
    }

    void OnDisable()
    {
        if (_bootCo != null) { StopCoroutine(_bootCo); _bootCo = null; }

        if (_hub != null)
        {
            if (ReferenceEquals(_hub.Spawn, this)) _hub.Spawn = null;
            if (ReferenceEquals(_hub.Random, this)) _hub.Random = null;
            if (ReferenceEquals(_hub.Target, this)) _hub.Target = null;
            if (ReferenceEquals(_hub.GameStart, this)) _hub.GameStart = null;
            if (ReferenceEquals(_hub.DeathEvents, this)) _hub.DeathEvents = null;
        }
    }


    // ===== ISpawnPort =====
    public GameObject Spawn(EnemyDefinition def, Vector3 position, Quaternion rotation)
    {
        // SP: gọi thẳng pool hiện có
        if (!poolManager) poolManager = FindFirstObjectByType<ZombiePoolManager>(FindObjectsInactive.Include);

        if (!poolManager)
        {
            Debug.LogError("[AISingleplayerImpl] ZombiePoolManager not found in scene.");
            return null;
        }
        if (!def)
        {
            Debug.LogError("[AISingleplayerImpl] EnemyDefinition is NULL.");
            return null;
        }

        return poolManager.Spawn(def.prefab.name, position, rotation);
    }

    public void Despawn(GameObject instance)
    {
        if (!instance) return;

        if (!poolManager) poolManager = FindFirstObjectByType<ZombiePoolManager>(FindObjectsInactive.Include);

        if (!poolManager)
        {
            Debug.LogError("[AISingleplayerImpl] ZombiePoolManager not found for Despawn.");
            return;
        }
        poolManager.Despawn(instance);
    }

    // ===== ISpawnPort =====
/*    public GameObject Spawn(EnemyDefinition def, Vector3 position, Quaternion rotation)
    {
        // SP: tạm thời spawn trực tiếp từ prefab, không dùng pool
        if (!def || !def.prefab)
        {
            Debug.LogError("[AISingleplayerImpl] EnemyDefinition/prefab missing.");
            return null;
        }

        var go = Instantiate(def.prefab, position, rotation);
        // SpawnManager đã tự gắn EnemySpawnHandle + DamageableDeathBridge rồi,
        // nên ở đây không làm gì thêm.
        return go;
    }

    public void Despawn(GameObject instance)
    {
        if (!instance) return;

        // SP: tạm thời dùng Destroy, sau này nếu quay lại pool thì chỉ cần đổi ở đây.
        Destroy(instance);
    }*/


    // ===== IRandomPort =====
    public int RangeInt(int minInclusive, int maxExclusive) => UnityEngine.Random.Range(minInclusive, maxExclusive);
    public float RangeFloat(float minInclusive, float maxInclusive) => UnityEngine.Random.Range(minInclusive, maxInclusive);
    public Vector2 InsideUnitCircle() => UnityEngine.Random.insideUnitCircle;
    public void SetSeed(int? seed)
    {
        if (seed.HasValue) UnityEngine.Random.InitState(seed.Value);
        // Nếu null → giữ RNG hiện tại (không reset)
    }

    // ===== ITargetQuery =====
    public ITargetable GetBestTarget(Vector3 fromPosition, bool requireAttackable = true)
    {
        if (!targetService) targetService = FindFirstObjectByType<TargetService>(FindObjectsInactive.Include);
        if (!targetService)
        {
            // Không có TargetService → trả null (AI call phải tự xử lý null)
            return null;
        }
        // Giả định TargetService đã có API tương tự:
        //   ITargetable GetBestTarget(Vector3 origin, bool requireAttackable)
        return targetService.GetBestTarget(fromPosition, requireAttackable);
    }

    // ===== IDeathEvents =====
    // Ở SP, Director của bạn đang hook chết qua DamageSystem/EnemyLifeToken rồi.
    // Nếu muốn route thêm, bạn có thể gọi RaiseEnemyDied(token) từ nơi phù hợp (tuỳ dự án).
    public event Action<EnemyLifeToken> OnEnemyDied;

    /// Gọi hàm này từ nơi bạn xử lý tử vong (nếu muốn chuyển kênh qua port).
    public void RaiseEnemyDied(EnemyLifeToken token)
    {
        try { OnEnemyDied?.Invoke(token); }
        catch (Exception e) { Debug.LogException(e); }
    }

    // ===== IGameStartPort =====
    public void RequestStart()
    {
        if (!roundDirector) roundDirector = FindFirstObjectByType<RoundDirector>(FindObjectsInactive.Include);
        if (!roundDirector)
        {
            Debug.LogWarning("[AISingleplayerImpl] RoundDirector not found → cannot RequestStart()");
            return;
        }
        // SP: gọi thẳng API start của Director
        // (đảm bảo tên hàm đúng với dự án của bạn)
        roundDirector.RequestManualStart();
    }

    // ===== Convenience / Diagnostics =====
    void Reset()
    {
        // Auto-wire trong Editor khi add component
        poolManager = FindFirstObjectByType<ZombiePoolManager>(FindObjectsInactive.Include);
        targetService = FindFirstObjectByType<TargetService>(FindObjectsInactive.Include);
        roundDirector = FindFirstObjectByType<RoundDirector>(FindObjectsInactive.Include);
    }

    void OnValidate()
    {
        // Nhắc nhở đặt đúng container
        if (enabled && gameObject.activeInHierarchy)
        {
            // Không bắt buộc, chỉ log nhẹ cho chắc
            if (GameSession.Mode != AppPlayMode.Single)
                Debug.LogWarning("[AISingleplayerImpl] GameSession.Mode != Single — SPIml chỉ dành cho Singleplayer container.");
        }
    }
}
