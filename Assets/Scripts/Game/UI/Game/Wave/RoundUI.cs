using UnityEngine;
using TMPro;
using DG.Tweening;

public class RoundUI : MonoBehaviour
{
    [Header("HUD Number (bottom-left)")]
    [SerializeField] private TMP_Text roundCornerText;          // số round ở góc
    [SerializeField] private CanvasGroup roundCornerGroup;      // CanvasGroup để ẩn/hiển

    [Header("Banner (center)")]
    [SerializeField] private TMP_Text roundBannerText;          // số round ở giữa, trượt đi
    [SerializeField] private TMP_Text roundWordText;            // chữ "ROUND"

    [Header("Colors")]
    [SerializeField] private Color hudBaseColor = Color.red;    // màu idle ở góc
    [SerializeField] private Color hudHighlightColor = Color.white;

    [Header("Timings (gợi ý chậm hơn)")]
    [SerializeField] private float colorToRedDuration = 0.9f;   // trắng -> đỏ (banner)
    [SerializeField] private float wordFadeDuration = 0.7f;     // fade chữ ROUND
    [SerializeField] private float moveToCornerDuration = 1.1f; // banner trượt về góc
    [SerializeField] private float endPulseDuration = 1.2f;     // đỏ -> trắng -> đỏ (HUD)
    [SerializeField] private float endPulseFadeDuration = 0.6f;

    Transform _boundPlayerRoot;
    RoundDirector _director;
    Vector2 _bannerStartAnchoredPos;

    int _currentRound = 0;

    Vector3 _bannerBaseScale;
    Vector3 _bannerStartWorldPos;

    Sequence _bannerSeq;
    Sequence _endPulseSeq;

    bool _initializedOnce = false;
    bool _hasPlayedIntro = false;          // NEW: chỉ intro 1 lần

#if FUSION_WEAVER
    RoundStateNet _netState;
#endif

    void Awake()
    {
        // Lấy CanvasGroup nếu bạn quên gán
        if (!roundCornerGroup && roundCornerText)
            roundCornerGroup = roundCornerText.GetComponent<CanvasGroup>();

        if (roundCornerText != null)
        {
            roundCornerText.color = hudBaseColor;
            roundCornerText.text = "0";

            // Lúc chưa start round nào -> ẩn góc
            if (roundCornerGroup != null)
                roundCornerGroup.alpha = 0f;
        }

        if (roundBannerText != null)
        {
            var bannerRT = roundBannerText.rectTransform;
            _bannerStartAnchoredPos = bannerRT.anchoredPosition;

            _bannerBaseScale = bannerRT.localScale;
            roundBannerText.gameObject.SetActive(false);
        }


        if (roundWordText != null)
        {
            roundWordText.gameObject.SetActive(false);
        }
    }

    void OnEnable()
    {
        TryHookDirector();
    }

    void OnDisable()
    {
#if FUSION_WEAVER
        if (_netState != null)
        {
            _netState.OnRoundChanged -= OnDirectorRoundChanged;
            _netState.OnRoundEnded -= OnDirectorRoundEnded;
            _netState = null;
        }
#endif

        if (_director != null)
        {
            _director.OnRoundChanged -= OnDirectorRoundChanged;
            _director.OnRoundEnded -= OnDirectorRoundEnded;
            _director = null;
        }

        _bannerSeq?.Kill();
        _endPulseSeq?.Kill();
    }


    public void Bind(Transform playerRoot)
    {
        _boundPlayerRoot = playerRoot;
        TryHookDirector();
    }

    void TryHookDirector()
    {
        // ===== Multiplayer: hook RoundStateNet =====
        if (GameSession.Mode != AppPlayMode.Single)
        {
#if FUSION_WEAVER
            if (_netState != null) return;

            _netState = RoundStateNet.Instance
                        ?? FindFirstObjectByType<RoundStateNet>(FindObjectsInactive.Include);

            if (_netState != null)
            {
                // Đăng ký sự kiện trước
                _netState.OnRoundChanged += OnDirectorRoundChanged;
                _netState.OnRoundEnded += OnDirectorRoundEnded;

                if (!_initializedOnce)
                {
                    // CHỈ truy cập RoundIndex nếu Object đã được Spawn (IsValid)
                    if (_netState.Object != null && _netState.Object.IsValid)
                    {
                        int r = _netState.RoundIndex;
                        if (r == 1 && !_hasPlayedIntro)
                        {
                            _currentRound = 1;
                            if (roundCornerText) roundCornerText.text = "1";
                            if (roundCornerGroup) roundCornerGroup.alpha = 0f;
                        }
                        else
                        {
                            SetRoundInstant(r);
                        }
                    }
                    // Nếu chưa Valid, đừng lo, sự kiện OnRoundChanged trong RoundStateNet.Spawned() 
                    // sẽ tự động gọi OnDirectorRoundChanged khi nó sẵn sàng.

                    _initializedOnce = true;
                }
            }
#endif
            return;
        }

        // ===== Singleplayer: hook RoundDirector như cũ =====
        if (_director != null) return;

        _director = RoundDirector.Instance
                    ?? FindFirstObjectByType<RoundDirector>(FindObjectsInactive.Include);

        if (_director != null)
        {
            _director.OnRoundChanged += OnDirectorRoundChanged;
            _director.OnRoundEnded -= OnDirectorRoundEnded;
            _director.OnRoundEnded += OnDirectorRoundEnded;

            if (!_initializedOnce)
            {
                SetRoundInstant(_director.roundIndex);
                _initializedOnce = true;
            }
        }
    }


    // =====================================================
    // Event handlers
    // =====================================================

    void OnDirectorRoundChanged(int newRound)
    {
        if (newRound <= 0)
        {
            SetRoundInstant(0);
            return;
        }

        // Chỉ intro 1 lần (thường là round 1)
        if (!_hasPlayedIntro)
        {
            _hasPlayedIntro = true;
            PlayNewRoundBanner(newRound);
        }
        else
        {
            // Các round sau: chỉ update số ở góc, không banner
            SetRoundInstant(newRound);
        }
    }

    void OnDirectorRoundEnded(int endedRound)
    {
        if (endedRound == _currentRound && endedRound > 0)
        {
            PlayEndRoundPulse();
        }
    }

    // =====================================================
    // Logic
    // =====================================================

    void SetRoundInstant(int value)
    {
        _currentRound = Mathf.Max(0, value);

        _endPulseSeq?.Kill();
        _endPulseSeq = null;

        if (!roundCornerText) return;

        roundCornerText.text = _currentRound.ToString();
        roundCornerText.color = hudBaseColor;

        if (roundCornerGroup != null)
            roundCornerGroup.alpha = (_currentRound > 0) ? 1f : 0f;

        if (roundBannerText != null)
            roundBannerText.gameObject.SetActive(false);

        if (roundWordText != null)
            roundWordText.gameObject.SetActive(false);
    }

    void PlayNewRoundBanner(int value)
    {
        _currentRound = value;

        if (!roundBannerText || !roundCornerText)
        {
            // Fallback: nếu thiếu setup thì set instant
            SetRoundInstant(value);
            return;
        }

        var bannerRT = roundBannerText.rectTransform;

        // Hủy tween cũ
        _bannerSeq?.Kill();
        _endPulseSeq?.Kill();

        // Chuẩn bị banner: ở giữa (world pos cũ), to hơn, màu trắng
        bannerRT.anchoredPosition = _bannerStartAnchoredPos;
        bannerRT.localScale = _bannerBaseScale * 1.4f;

        roundBannerText.text = _currentRound.ToString();
        roundBannerText.color = hudHighlightColor;
        roundBannerText.gameObject.SetActive(true);

        // Chuẩn bị chữ ROUND
        if (roundWordText != null)
        {
            roundWordText.gameObject.SetActive(true);
            roundWordText.text = "ROUND";
            var c = hudHighlightColor;
            c.a = 1f;
            roundWordText.color = c;
        }

        // Ẩn HUD góc trong lúc banner chơi
        if (roundCornerGroup != null)
            roundCornerGroup.alpha = 0f;

        _bannerSeq = DOTween.Sequence();

        // 1) delay nhẹ
        _bannerSeq.AppendInterval(0.2f);

        // 2) Trắng -> đỏ cho cả số & chữ ROUND
        _bannerSeq.Append(roundBannerText.DOColor(hudBaseColor, colorToRedDuration));
        if (roundWordText != null)
        {
            _bannerSeq.Join(roundWordText.DOColor(hudBaseColor, colorToRedDuration));
        }

        // 3) Đợi thêm chút rồi fade chữ ROUND
        _bannerSeq.AppendInterval(0.2f);

        if (roundWordText != null)
        {
            _bannerSeq.Append(roundWordText.DOFade(0f, wordFadeDuration));
        }

        // 4) Đồng thời kéo banner về vị trí của HUD góc + scale nhỏ lại (WORLD POS)
        var cornerRT = roundCornerText.rectTransform;

        // Chuyển world-pos của corner về local của parent banner
        Vector3 cornerWorld = cornerRT.position;
        Vector2 targetLocalInBannerParent =
            bannerRT.parent.InverseTransformPoint(cornerWorld);

        // Dùng anchoredPos (cùng parent → không lệch theo GameView)
        _ = _bannerSeq.Join(
            bannerRT.DOAnchorPos(targetLocalInBannerParent, moveToCornerDuration)
                     .SetEase(Ease.InOutCubic)
        );
        _bannerSeq.Join(bannerRT.DOScale(_bannerBaseScale, moveToCornerDuration));

        // 5) Kết thúc: tắt banner, bật HUD góc
        _bannerSeq.OnComplete(() =>
        {
            roundBannerText.gameObject.SetActive(false);

            if (roundWordText != null)
                roundWordText.gameObject.SetActive(false);

            roundCornerText.text = _currentRound.ToString();
            roundCornerText.color = hudBaseColor;

            if (roundCornerGroup != null)
                roundCornerGroup.alpha = 1f;
        });
    }

    void PlayEndRoundPulse()
    {
        if (!roundCornerText) return;

        _endPulseSeq?.Kill();

        // Loop: đỏ <-> trắng cho tới khi round mới start (SetRoundInstant sẽ Kill())
        _endPulseSeq = DOTween.Sequence();
        _endPulseSeq.Append(
            roundCornerText.DOColor(hudHighlightColor, endPulseFadeDuration)
        );
        _endPulseSeq.Append(
            roundCornerText.DOColor(hudBaseColor, endPulseFadeDuration)
        );
        _endPulseSeq.SetLoops(-1, LoopType.Restart);
    }
}
