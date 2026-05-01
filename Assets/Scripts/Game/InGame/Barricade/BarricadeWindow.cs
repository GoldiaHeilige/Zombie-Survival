using UnityEngine;
using System;
using UnityEngine.AI;
using System.Collections;
using TT;
using Unity.AI.Navigation;

public class BarricadeWindow : MonoBehaviour
{
    [System.Serializable]
    public class Slot
    {
        [Header("Setup")]
        public Transform anchor;          // chỗ ván nằm trên cửa (optional, để debug)
        public GameObject boardStatic;    // ván “thật” bám trên cửa
        public GameObject boardFX;        // prefab FX bay vào

        [Header("Runtime (debug)")]
        public bool isIntact;             // đang có ván hay trống
        public bool isRebuilding;         // FX đang build

        [Header("Hit / Break FX")]
        public Transform breakVfxPoint;   // chỗ spawn VFX vỡ ván (optional)
        public GameObject breakVfxPrefab; // prefab vfx vỡ ván
    }

    [Header("Slots")]
    [SerializeField] private Slot[] slots;

    [Header("Initial State")]
    [Tooltip("Số ván có sẵn lúc start (0 = trống; = slots.Length = full).")]
    [Range(0, 10)] public int initialIntactBoards = 0;

    [Header("Runtime")]
    [SerializeField] private bool _isWindowRebuilding;

    // ==== HP kiểu C cho zombie ====
    [Header("Zombie Damage (Hit Count)")]
    [Tooltip("Số lần zombie đánh để phá 1 ván.")]
    [SerializeField] private int hitsPerBoard = 3;

    [Header("Points")]
    [Tooltip("Điểm cộng cho player mỗi lần BẮT ĐẦU repair 1 thanh ván.")]
    [Min(0)] public int pointsPerRepair = 10;

    [Tooltip("Số hit đã tích vào ván intact trên cùng.")]
    [SerializeField] private int _currentBoardHits;

    [Header("Hit Reaction (Shake)")]
    [SerializeField] private Transform shakeTarget;     // cái transform cần rung, default = this.transform
    [SerializeField] private float hitShakeAmplitude = 0.04f;
    [SerializeField] private float hitShakeDuration = 0.12f;
    [SerializeField]
    private AnimationCurve hitShakeCurve =
        AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    private Vector3 _shakeBaseLocalPos;
    private Coroutine _shakeRoutine;


    /// <summary>Event cho trigger/UI biết state cửa đổi (build xong, ván vỡ...).</summary>
    public Action<BarricadeWindow> OnWindowStateChanged;

    public int SlotCount => slots != null ? slots.Length : 0;
    public bool IsWindowRebuilding => _isWindowRebuilding;

    // ─────────────────────────────
    //  Helpers trạng thái slot
    // ─────────────────────────────

    /// <summary>Cửa còn ván intact nào không?</summary>
    public bool HasIntactBoard()
    {
        if (slots == null) return false;
        for (int i = 0; i < slots.Length; i++)
            if (slots[i].isIntact)
                return true;
        return false;
    }

    /// <summary>Cửa còn slot trống (để build) không?</summary>
    public bool HasEmptySlot()
    {
        if (slots == null) return false;
        for (int i = 0; i < slots.Length; i++)
        {
            var s = slots[i];
            if (!s.isIntact && !s.isRebuilding)
                return true;
        }
        return false;
    }

    /// <summary>Index slot trống tiếp theo (từ 0 lên). Không có → -1.</summary>
    public int GetNextEmptySlotIndex()
    {
        if (slots == null) return -1;
        for (int i = 0; i < slots.Length; i++)
        {
            var s = slots[i];
            if (!s.isIntact && !s.isRebuilding)
                return i;
        }
        return -1;
    }

    /// <summary>Index ván intact trên cùng (từ trên xuống). Không có → -1.</summary>
    public int GetTopIntactBoardIndex()
    {
        if (slots == null) return -1;
        for (int i = slots.Length - 1; i >= 0; i--)
        {
            if (slots[i].isIntact)
                return i;
        }
        return -1;
    }

    // ─────────────────────────────
    //  Init
    // ─────────────────────────────

    private void Awake()
    {
        if (!shakeTarget)
            shakeTarget = transform;

        _shakeBaseLocalPos = shakeTarget.localPosition;

        InitBoards();
    }

    void InitBoards()
    {
        if (slots == null) return;

        for (int i = 0; i < slots.Length; i++)
        {
            var s = slots[i];
            bool intact = i < initialIntactBoards;

            s.isIntact = intact;
            s.isRebuilding = false;

            if (s.boardStatic) s.boardStatic.SetActive(intact);
            if (s.boardFX) s.boardFX.SetActive(false);
        }

        _isWindowRebuilding = false;
        _currentBoardHits = 0;

        NotifyStateChanged();
    }

    void NotifyStateChanged()
    {
        // ✅ Block vault nếu:
        // - còn ván intact (zombie phải đập)
        // - HOẶC đang rebuild (đang đóng ván, không cho nhảy xuyên)
        bool blockVault = CanTakeZombieHit() || HasRebuildingBoard() || _isWindowRebuilding;

        OnWindowStateChanged?.Invoke(this);
    }


    // ─────────────────────────────
    //  Build (player repair)
    // ─────────────────────────────

    /// <summary>Kiểm tra có thể start build ván mới không.</summary>
    public bool CanStartRebuild(out int slotIndex)
    {
        if (_isWindowRebuilding)
        {
            slotIndex = -1;
            return false;
        }

        slotIndex = GetNextEmptySlotIndex();
        return slotIndex >= 0;
    }

    public bool StartRebuildAtIndex(int index)
    {
        if (slots == null) return false;
        if (index < 0 || index >= slots.Length) return false;

        var s = slots[index];
        if (s.isIntact || s.isRebuilding)
            return false;

        if (s.boardFX == null)
        {
            Debug.LogWarning($"[BarricadeWindow] Slot {index} chưa gán boardFX.", this);
            return false;
        }

        s.isRebuilding = true;
        _isWindowRebuilding = true;

        s.boardFX.SetActive(true);
        var anim = s.boardFX.GetComponent<Animator>();
        if (anim != null)
        {
            anim.Play("Board_FlyIn", 0, 0f);
        }

        return true;
    }

    public bool TryStartRebuildNext()
    {
        if (!CanStartRebuild(out int index)) return false;
        return StartRebuildAtIndex(index);
    }

    /// <summary>Gọi từ BarricadeBoardFX.Anim_OnBuildFinished().</summary>
    public void OnBoardBuildFinished(int slotIndex)
    {
        if (slots == null) return;
        if (slotIndex < 0 || slotIndex >= slots.Length) return;

        var s = slots[slotIndex];

        s.isRebuilding = false;
        s.isIntact = true;

        if (s.boardFX) s.boardFX.SetActive(false);
        if (s.boardStatic) s.boardStatic.SetActive(true);

        _isWindowRebuilding = false;
        _currentBoardHits = 0; // reset stack hit khi build lại

    //    Debug.Log($"[BarricadeWindow] Build xong slot {slotIndex}", this);
        NotifyStateChanged();
    }

    // ─────────────────────────────
    //  Zombie damage kiểu C
    // ─────────────────────────────

    /// <summary>Cửa còn ván intact để nhận hit không?</summary>
    public bool CanTakeZombieHit()
    {
        return HasIntactBoard();
    }

    /// <summary>
    /// Zombie đánh cửa. Trả về true nếu cú đánh này làm vỡ ván,
    /// đồng thời trả ra slotIndex ván bị vỡ (>= 0 nếu có).
    /// </summary>
    public bool ApplyZombieHit(out int brokenBoardIndex)
    {
        brokenBoardIndex = -1;

        if (!CanTakeZombieHit()) return false;
        if (hitsPerBoard <= 0) hitsPerBoard = 1;

        int boardIndex = GetTopIntactBoardIndex();
        if (boardIndex < 0) return false;

        _currentBoardHits++;
        if (_currentBoardHits >= hitsPerBoard)
        {
            _currentBoardHits = 0;
            brokenBoardIndex = boardIndex;
            BreakBoard(boardIndex);
            return true;
        }

        return false;
    }

    /// <summary>Overload cho SP / debug, không quan tâm index.</summary>
    public void ApplyZombieHit()
    {
        ApplyZombieHit(out _);
    }

    /// <summary>Phá ván tại slotIndex.</summary>
    public void BreakBoard(int slotIndex)
    {
        if (slots == null) return;
        if (slotIndex < 0 || slotIndex >= slots.Length) return;

        var s = slots[slotIndex];

        // đang build thì thôi, tránh conflict
        if (s.isRebuilding) return;
        if (!s.isIntact) return;

        s.isIntact = false;
        if (s.boardStatic) s.boardStatic.SetActive(false);

        // === VFX vỡ ván ===
        if (s.breakVfxPrefab != null)
        {
            Vector3 pos;
            Vector3 normal;

            if (s.breakVfxPoint != null)
            {
                pos = s.breakVfxPoint.position;
                normal = -s.breakVfxPoint.forward;
            }
            else if (s.boardStatic != null)
            {
                pos = s.boardStatic.transform.position;
                normal = -s.boardStatic.transform.forward;
            }
            else if (s.anchor != null)
            {
                pos = s.anchor.position;
                normal = -s.anchor.forward;
            }
            else
            {
                pos = transform.position;
                normal = -transform.forward;
            }

            // Nếu bạn đã có ImpactPool (giống lúc build ván) thì ưu tiên dùng
            if (ImpactPool.Instance != null)
            {
                ImpactPool.Instance.Spawn(s.breakVfxPrefab, pos, normal);
            }
            else
            {
                // Fallback: Instantiate bình thường
                Instantiate(s.breakVfxPrefab, pos, Quaternion.LookRotation(normal));
            }
        }

   //     Debug.Log($"[BarricadeWindow] Break board slot {slotIndex}", this);
        NotifyStateChanged();
    }

    public void PlayHitShake()
    {
        if (!shakeTarget) return;

        if (_shakeRoutine != null)
        {
            StopCoroutine(_shakeRoutine);
            _shakeRoutine = null;
            shakeTarget.localPosition = _shakeBaseLocalPos;
        }

        _shakeRoutine = StartCoroutine(HitShakeRoutine());
    }

    private IEnumerator HitShakeRoutine()
    {
        float t = 0f;

        while (t < hitShakeDuration)
        {
            float normalized = hitShakeDuration > 0f ? t / hitShakeDuration : 1f;
            float strength = hitShakeCurve != null ? hitShakeCurve.Evaluate(normalized) : 1f;

            // Rung nhẹ quanh vị trí gốc
            Vector3 offset = UnityEngine.Random.insideUnitSphere * (hitShakeAmplitude * strength);
            // Nếu không muốn rung theo trục Z thì:
            offset.z = 0f;

            shakeTarget.localPosition = _shakeBaseLocalPos + offset;

            t += Time.deltaTime;
            yield return null;
        }

        shakeTarget.localPosition = _shakeBaseLocalPos;
        _shakeRoutine = null;
    }

    public bool HasRebuildingBoard()
    {
        if (slots == null) return false;
        for (int i = 0; i < slots.Length; i++)
            if (slots[i].isRebuilding)
                return true;
        return false;
    }
}
