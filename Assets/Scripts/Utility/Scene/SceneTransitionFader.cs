using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TT.UI
{
    /// <summary>
    /// Fade đen toàn màn hình, dùng chung cho mọi chuyển scene.
    /// - DontDestroyOnLoad
    /// - FadeOut (0->1) rồi mới LoadScene
    /// - Scene mới: vẫn đen, sau đó FadeIn (1->0)
    /// </summary>
    public class SceneTransitionFader : MonoBehaviour
    {
        public static SceneTransitionFader Instance { get; private set; }

        [Header("Config")]
        [SerializeField] private float fadeOutDuration = 0.25f;
        [SerializeField] private float fadeInDuration = 0.25f;
        [SerializeField] private Ease fadeEase = Ease.OutQuad;

        private Canvas _canvas;
        private CanvasGroup _cg;
        private Image _img;

        private Tween _tween;
        private bool _isLoading;

        private void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            BuildUIIfNeeded();
            ForceBlack(true); // khởi tạo an toàn
            ForceBlack(false);
        }

        private void BuildUIIfNeeded()
        {
            _canvas = GetComponentInChildren<Canvas>(true);
            _cg = GetComponentInChildren<CanvasGroup>(true);
            _img = GetComponentInChildren<Image>(true);

            if (_canvas && _cg && _img) return;

            // Build runtime UI
            var root = new GameObject("FadeCanvas");
            root.transform.SetParent(transform, false);

            _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = short.MaxValue;

            root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            root.AddComponent<GraphicRaycaster>();

            _cg = root.AddComponent<CanvasGroup>();
            _cg.alpha = 0f;
            _cg.interactable = false;
            _cg.blocksRaycasts = false;

            var imgGo = new GameObject("Black");
            imgGo.transform.SetParent(root.transform, false);

            _img = imgGo.AddComponent<Image>();
            _img.color = Color.black;

            var rt = (RectTransform)imgGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static void LoadScene(string sceneName) => Instance.InternalLoad(sceneName);
        public static void LoadScene(int buildIndex) => Instance.InternalLoad(buildIndex);

        private void InternalLoad(string sceneName)
        {
            if (_isLoading) return;
            StartCoroutine(Co_Load(() => SceneManager.LoadScene(sceneName, LoadSceneMode.Single)));
        }

        private void InternalLoad(int buildIndex)
        {
            if (_isLoading) return;
            StartCoroutine(Co_Load(() => SceneManager.LoadScene(buildIndex, LoadSceneMode.Single)));
        }

        private IEnumerator Co_Load(System.Action doLoad)
        {
            _isLoading = true;

            // Fade Out -> black
            yield return FadeTo(1f, fadeOutDuration);

            doLoad?.Invoke();

            // đợi scene load xong 1 frame (để Awake/Start scene mới chạy trong nền màn đen)
            yield return null;

            // Fade In -> clear
            yield return FadeTo(0f, fadeInDuration);

            _isLoading = false;
        }

        private IEnumerator FadeTo(float a, float duration)
        {
            BuildUIIfNeeded();

            _tween?.Kill();
            _cg.blocksRaycasts = true;
            _cg.interactable = false;

            _tween = DOTween.To(() => _cg.alpha, x => _cg.alpha = x, a, duration)
                .SetEase(fadeEase)
                .SetUpdate(true); // unscaled

            while (_tween != null && _tween.IsActive() && _tween.IsPlaying())
                yield return null;

            // nếu alpha == 0 thì thả raycast
            bool isBlack = _cg.alpha >= 0.99f;
            _cg.blocksRaycasts = isBlack;
        }

        private void ForceBlack(bool black)
        {
            BuildUIIfNeeded();
            _tween?.Kill();
            _cg.alpha = black ? 1f : 0f;
            _cg.blocksRaycasts = black;
            _cg.interactable = false;
        }

        public void BeginNetworkFadeOut()
        {
            if (!gameObject.activeInHierarchy) return;
            BuildUIIfNeeded();

            // nếu đang tween thì kill rồi fade về đen
            StopAllCoroutines();
            StartCoroutine(FadeTo(1f, fadeOutDuration));
        }

        public void BeginNetworkFadeIn()
        {
            if (!gameObject.activeInHierarchy) return;
            BuildUIIfNeeded();

            StopAllCoroutines();
            StartCoroutine(FadeTo(0f, fadeInDuration));
        }

    }
}
