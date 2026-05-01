using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using NIX.Core.DesignPatterns;

namespace TT
{
    [DisallowMultipleComponent]
    public class AudioSceneDirector : SingletonBehaviour<AudioSceneDirector>, IAutoDontDestroy, NIX.Core.DesignPatterns.INoAutoCreate
    {
        [Header("Scene Names")]
        [SerializeField] private string[] menuScenes = { "MainMenu", "LobbyScene", "ServerBrowserScene" };
        [SerializeField] private string[] gameScenes = { "M_PoliceStation" }; // hoặc để trống rồi check theo pattern

        [Header("Event Names (AudioEventSO.eventName)")]
        [Header("Event Ids (AudioEventSO.eventId)")]
        [SerializeField] private int menuMusicEventId = 1001;
        [SerializeField] private int gameAmbientLoopEventId = 2001;
        [SerializeField] private int[] gameAmbientRandomEventIds = 
        { 
            2101, 2102, 2103 
        };


        [Header("Fades")]
        [SerializeField] private float fadeOutSeconds = 0.75f;

        [Header("Random Ambient Timing")]
        [SerializeField] private Vector2 randomDelayRange = new Vector2(8f, 18f);

        private AudioHandle _menuMusicHandle;
        private AudioHandle _ambientLoopHandle;
        private Coroutine _randomRoutine;
        private bool _subscribed;

        protected override void Awake()
        {
            base.Awake();

            // Nếu là bản duplicate vừa spawn trong MainMenu scene -> base.Awake() đã Destroy(gameObject)
            // nhưng code phía dưới vẫn có thể chạy trong cùng frame, nên chặn.
            if (Instance != this) return;

            SceneManager.activeSceneChanged += OnSceneChanged;
            _subscribed = true;
        }

        private void Start()
        {
            if (Instance != this) return;

            // apply cho scene hiện tại lúc vào play
            OnSceneChanged(default, SceneManager.GetActiveScene());
        }

        private void OnDestroy()
        {
            if (_subscribed)
                SceneManager.activeSceneChanged -= OnSceneChanged;
        }

        private void OnSceneChanged(Scene oldScene, Scene newScene)
        {
            string s = newScene.name;

            if (IsMenuScene(s))
                EnterMenu();
            else if (IsGameScene(s))
                EnterGame();
            else
                EnterMenu(); // fallback: coi như menu nếu mày chưa add scene vào list
        }

        private bool IsMenuScene(string sceneName)
        {
            for (int i = 0; i < menuScenes.Length; i++)
                if (menuScenes[i] == sceneName) return true;
            return false;
        }

        private bool IsGameScene(string sceneName)
        {
            // ưu tiên danh sách explicit
            for (int i = 0; i < gameScenes.Length; i++)
                if (gameScenes[i] == sceneName) return true;

            // hoặc fallback theo GameSession.SelectedMap nếu mày muốn:
            // return sceneName == GameSession.SelectedMap;

            return false;
        }

        private void EnterMenu()
        {
            var mgr = AudioManager.Instance;
            if (!mgr) return;

            StopRandomRoutine();

            // Stop game ambience
            mgr.FadeOutAndStop(_ambientLoopHandle, fadeOutSeconds);
            _ambientLoopHandle = default; // ✅ reset ngay để lần sau vào game/menu không bị “kẹt”

            // Start menu music nếu chưa đang play thật sự
            if (!mgr.IsHandlePlaying(_menuMusicHandle))
                _menuMusicHandle = mgr.Play2DHandle(menuMusicEventId);
        }

        private void EnterGame()
        {
            var mgr = AudioManager.Instance;
            if (!mgr) return;

            // Stop menu music
            mgr.FadeOutAndStop(_menuMusicHandle, fadeOutSeconds);
            _menuMusicHandle = default; // ✅ reset ngay

            // Start ambient loop
            if (!mgr.IsHandlePlaying(_ambientLoopHandle))
                _ambientLoopHandle = mgr.Play2DHandle(gameAmbientLoopEventId);

            StartRandomRoutine();
        }

        private void StartRandomRoutine()
        {
            if (_randomRoutine != null) return;
            if (gameAmbientRandomEventIds == null || gameAmbientRandomEventIds.Length == 0) return;

            _randomRoutine = StartCoroutine(RandomAmbientLoop());
        }

        private void StopRandomRoutine()
        {
            if (_randomRoutine == null) return;
            StopCoroutine(_randomRoutine);
            _randomRoutine = null;
        }

        private IEnumerator RandomAmbientLoop()
        {
            var mgr = AudioManager.Instance;

            while (true)
            {
                float wait = Random.Range(randomDelayRange.x, randomDelayRange.y);
                yield return new WaitForSecondsRealtime(wait);

                if (!mgr) mgr = AudioManager.Instance;
                if (!mgr) continue;

                mgr.Play2D(gameAmbientRandomEventIds[Random.Range(0, gameAmbientRandomEventIds.Length)]);
            }
        }
    }
}
