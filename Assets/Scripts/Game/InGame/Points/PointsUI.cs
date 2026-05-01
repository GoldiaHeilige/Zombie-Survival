using UnityEngine;
using TMPro;
using TT;
using DG.Tweening;

public class PointsUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text currentPointsText;
    [SerializeField] private TMP_Text gainText;

    [Header("Counting Settings")]
    [SerializeField] private float countDuration = 0.3f;

    [Header("Main Number Tween")]
    [SerializeField] private float punchScale = 0.25f;
    [SerializeField] private float punchDuration = 0.15f;

    [Header("Gain Anim Settings")]
    [SerializeField] private float gainAnimDuration = 0.5f;

    [Header("Gain Layout")]
    [SerializeField] private Vector2 gainRelativeOffset = new Vector2(-40f, 0f);
    [SerializeField] private float gainRandomHorizontal = 25f;
    [SerializeField] private float gainRandomVertical = 10f;

    [Header("Spend Anim Settings")]
    [SerializeField] private Color spendColor = Color.red;

    [Header("Pool Settings")]
    [SerializeField] private int maxTextInstances = 5;

    [SerializeField] private CanvasGroup _parentCanvasGroup;

    [Header("Row Style (Local vs Remote)")]
    [SerializeField] private float localMainScaleMultiplier = 1.25f;
    [SerializeField] private float remoteMainScaleMultiplier = 1.0f;

    // BG blood chỉ mờ ở remote (gán CanvasGroup của BG blood trong prefab)
    [SerializeField] private CanvasGroup bloodBgCanvasGroup;
    [SerializeField, Range(0f, 1f)] private float localBgAlpha = 1f;
    [SerializeField, Range(0f, 1f)] private float remoteBgAlpha = 0.65f;

    Vector3 _defaultMainScale;
    bool _isLocalRow;



    Transform _boundPlayerRoot;

    int _displayedPoints;

    Vector3 _baseScale;
    Vector2 _gainBasePos;
    Color _gainBaseColor;

    Tween _countTween;
    Color _pointsBaseColor;

    // Pools cho gain và spend
    TMP_Text[] _gainPool;
    TMP_Text[] _spendPool;
    Sequence[] _gainSeqPool;
    Sequence[] _spendSeqPool;
    int _gainCursor;
    int _spendCursor;

    void Awake()
    {
        if (currentPointsText != null)
        {
            currentPointsText.text = "0";
            _defaultMainScale = currentPointsText.rectTransform.localScale;
            _baseScale = _defaultMainScale; // _baseScale sẽ là "style scale" (local/remote)


            _pointsBaseColor = currentPointsText.color;
        }

        if (gainText != null)
        {
            _gainBasePos = gainText.rectTransform.anchoredPosition;
            _gainBaseColor = gainText.color;

            // Tạo pool cho gain (màu vàng)
            _gainPool = new TMP_Text[maxTextInstances];
            _gainSeqPool = new Sequence[maxTextInstances];

            // Tạo pool cho spend (màu đỏ)
            _spendPool = new TMP_Text[maxTextInstances];
            _spendSeqPool = new Sequence[maxTextInstances];

            for (int i = 0; i < maxTextInstances; i++)
            {
                // Tạo gain text
                TMP_Text gainT;
                if (i == 0)
                {
                    gainT = gainText; // cái có sẵn trong scene
                }
                else
                {
                    gainT = Instantiate(gainText, gainText.transform.parent);
                }

                gainT.gameObject.SetActive(false);
                gainT.rectTransform.anchoredPosition = _gainBasePos;
                gainT.color = _gainBaseColor;
                _gainPool[i] = gainT;

                // Tạo spend text
                TMP_Text spendT = Instantiate(gainText, gainText.transform.parent);
                spendT.gameObject.SetActive(false);
                spendT.rectTransform.anchoredPosition = _gainBasePos;
                spendT.color = spendColor; // Màu đỏ
                _spendPool[i] = spendT;
            }
        }
    }

    void OnEnable()
    {
        // ✅ Auto bật CanvasGroup cha (nếu bị tắt do đổi scene)
        if (_parentCanvasGroup == null)
            _parentCanvasGroup = GetComponentInParent<CanvasGroup>(true);

        if (_parentCanvasGroup != null)
        {
            _parentCanvasGroup.alpha = 1f;
            _parentCanvasGroup.interactable = true;
            _parentCanvasGroup.blocksRaycasts = true;
        }

        Observer.Instance?.AddObserver(PointsTopics.Changed, OnPointsChanged);
        Observer.Instance?.AddObserver(PointsTopics.Gained, OnPointsGained);
        Observer.Instance?.AddObserver(PointsTopics.Spent, OnPointsSpent);

        // bonus: đảm bảo text chính bật (phòng trường hợp bị tắt)
        if (currentPointsText) currentPointsText.gameObject.SetActive(true);
    }


    void OnDisable()
    {
        Observer.Instance?.RemoveObserver(PointsTopics.Changed, OnPointsChanged);
        Observer.Instance?.RemoveObserver(PointsTopics.Gained, OnPointsGained);
        Observer.Instance?.RemoveObserver(PointsTopics.Spent, OnPointsSpent);

        _countTween?.Kill();

        // Clean up cả gain và spend pools
        CleanupPool(_gainSeqPool);
        CleanupPool(_spendSeqPool);
    }

    void CleanupPool(Sequence[] pool)
    {
        if (pool != null)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                pool[i]?.Kill();
                pool[i] = null;
            }
        }
    }

    public void Bind(Transform playerRoot)
    {
        _boundPlayerRoot = playerRoot;

        RefreshFromWallet();
        StartCoroutine(RefreshNextFrame());
    }

    void RefreshFromWallet()
    {
        if (_boundPlayerRoot == null) return;

        var wallet = _boundPlayerRoot.GetComponentInChildren<PlayerPoints>();
        if (wallet == null) return;

        _displayedPoints = wallet.Current;
        if (currentPointsText != null)
            currentPointsText.text = _displayedPoints.ToString();
    }

    System.Collections.IEnumerator RefreshNextFrame()
    {
        yield return null; // 1 frame
        RefreshFromWallet();
    }


    void OnPointsChanged(object payloadObj)
    {
        if (!(payloadObj is PointsChangedEventData data))
            return;

        if (_boundPlayerRoot != null && data.owner != null)
        {
            if (data.owner.transform.root != _boundPlayerRoot.root)
                return;
        }

        StartCountingTo(data.newValue);
    }

    void OnPointsGained(object payloadObj)
    {
        if (!(payloadObj is PointsChangedEventData data))
            return;

        if (_boundPlayerRoot != null && data.owner != null)
        {
            if (data.owner.transform.root != _boundPlayerRoot.root)
                return;
        }

        // Hiển thị gain (+) với số dương
        ShowPointChange(data.delta, true);
    }

    void OnPointsSpent(object payloadObj)
    {
        if (!(payloadObj is PointsChangedEventData data))
            return;

        if (_boundPlayerRoot != null && data.owner != null)
        {
            if (data.owner.transform.root != _boundPlayerRoot.root)
                return;
        }

        // Hiển thị spend (-) với số âm
        ShowPointChange(-Mathf.Abs(data.delta), false); // Đảm bảo là số âm
    }

    void StartCountingTo(int newValue)
    {
        if (currentPointsText == null) return;

        _countTween?.Kill();

        float start = _displayedPoints;
        float end = newValue;

        _countTween = DOTween
            .To(() => start, v =>
            {
                start = v;
                int iv = Mathf.RoundToInt(v);
                if (iv != _displayedPoints)
                {
                    _displayedPoints = iv;
                    currentPointsText.text = iv.ToString();
                }
            }, end, countDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true);

        // Xử lý punch scale không bị stack
        currentPointsText.rectTransform.DOKill(true);
        currentPointsText.rectTransform.localScale = _baseScale;

        currentPointsText.rectTransform
            .DOPunchScale(Vector3.one * punchScale, punchDuration, 0, 0)
            .SetUpdate(true);
    }

    void ShowPointChange(int amount, bool isGain)
    {
        if (isGain)
        {
            if (_gainPool == null || _gainPool.Length == 0) return;
            int index = _gainCursor++ % _gainPool.Length;
            AnimatePointChange(_gainPool[index], _gainSeqPool, index, amount,
                              gainRelativeOffset, gainRandomHorizontal, gainRandomVertical,
                              _gainBaseColor, isGain);
        }
        else
        {
            if (_spendPool == null || _spendPool.Length == 0) return;
            int index = _spendCursor++ % _spendPool.Length;
            AnimatePointChange(_spendPool[index], _spendSeqPool, index, amount,
                              gainRelativeOffset, gainRandomHorizontal, gainRandomVertical,
                              spendColor, isGain);
        }
    }

    void AnimatePointChange(TMP_Text text, Sequence[] seqPool, int index, int amount,
                           Vector2 relativeOffset, float randomHorizontal, float randomVertical,
                           Color baseColor, bool isGain)
    {
        var rt = text.rectTransform;

        // Kill anim cũ nếu slot đang dùng
        if (seqPool[index] != null)
        {
            seqPool[index].Kill();
            seqPool[index] = null;
        }

        // Setup text + vị trí start
        text.gameObject.SetActive(true);

        // SỬA Ở ĐÂY: Luôn hiển thị dấu +/- trước số
        text.text = amount > 0 ? $"+{amount}" : $"{amount}";

        rt.anchoredPosition = _gainBasePos;

        var c = baseColor;
        c.a = 1f;
        text.color = c;

        // DÙNG CHUNG RANDOM VỚI GAIN (nếu bạn muốn)
        // Hoặc dùng biến riêng nếu đã set trong Inspector
        float randX = Random.Range(-randomHorizontal, randomHorizontal);
        float randY = Random.Range(-randomVertical, randomVertical);

        Vector2 endPos = _gainBasePos + relativeOffset + new Vector2(randX, randY);

        // Tạo tween
        Sequence seq = DOTween.Sequence()
            .Append(
                rt.DOAnchorPos(endPos, gainAnimDuration)
                  .SetEase(Ease.OutQuad)
            )
            .Join(
                text.DOFade(0f, gainAnimDuration)
                 .SetEase(Ease.OutQuad)
            )
            .OnComplete(() =>
            {
                text.gameObject.SetActive(false);
                text.color = baseColor;
            })
            .SetUpdate(true);

        seqPool[index] = seq;
    }

    public void SetMainPointsColor(Color c)
    {
        if (currentPointsText == null) return;
        currentPointsText.color = c;
        _pointsBaseColor = c;
    }

    public void ApplyRowStyle(bool isLocal)
    {
        _isLocalRow = isLocal;

        float mult = isLocal ? localMainScaleMultiplier : remoteMainScaleMultiplier;
        _baseScale = _defaultMainScale * mult;

        // set scale “đứng yên” của main points
        if (currentPointsText != null)
            currentPointsText.rectTransform.localScale = _baseScale;

        // set alpha BG blood
        if (bloodBgCanvasGroup != null)
            bloodBgCanvasGroup.alpha = isLocal ? localBgAlpha : remoteBgAlpha;
    }

}