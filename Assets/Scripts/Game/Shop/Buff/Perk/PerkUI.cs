using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace TT
{
    public class PerkUI : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("RectTransform ở giữa màn hình để spawn toast (có thể là 1 empty in Canvas)")]
        public RectTransform toastStart;

        [Tooltip("HorizontalLayoutGroup (góc dưới) chứa các perk icons đã sở hữu")]
        public HorizontalLayoutGroup ownedBar;

        [Tooltip("Prefab icon (UI Image) dùng cho cả toast và owned bar")]
        public PerkIconView iconPrefab;

        [Header("Timing")]
        public float toastStaySeconds = 1.25f;
        public float moveDuration = 0.45f;

        [Header("Animation")]
        public Ease moveEase = Ease.OutCubic;
        public float toastScalePunch = 0.12f;
        public float punchDuration = 0.18f;

        // perkId -> owned icon instance
        private readonly Dictionary<string, PerkIconView> _owned = new();

        // queue để nếu mua liên tục thì move từng cái 1 (tránh tween chồng)
        private readonly Queue<PerkChangedEventData> _queue = new();
        private bool _processing;
        private Transform _localRoot;

        public void Bind(Transform playerRoot)
        {
            _localRoot = playerRoot;
        }

        private void OnEnable()
        {
            Observer.Instance?.AddObserver(PerkTopics.Acquired, OnPerkAcquired);
        }

        private void OnDisable()
        {
            Observer.Instance?.RemoveObserver(PerkTopics.Acquired, OnPerkAcquired);
        }

        private void OnPerkAcquired(object data)
        {
            if (data is not TT.PerkChangedEventData e) return;

            // ===== Local-only filter (prefer Bind) =====
            if (_localRoot == null)
            {
                // fallback nếu quên Bind (optional)
                var local = PlayerRegistry.GetLocalPlayer();
                if (local != null) _localRoot = local.transform;
            }
            if (_localRoot == null) return;

            if (e.owner == null) return;

            // e.owner thường là GO có PerkManager (có thể root hoặc child)
            var ownerT = e.owner.transform;
            bool isLocal =
                ownerT == _localRoot ||
                ownerT.IsChildOf(_localRoot) ||
                _localRoot.IsChildOf(ownerT);

            if (!isLocal) return;
            // ==========================================

            if (e.def == null || string.IsNullOrWhiteSpace(e.perkId)) return;
            if (e.icon == null) return;

            _queue.Enqueue(e);
            if (!_processing)
                StartCoroutine(ProcessQueue());
        }



        private IEnumerator ProcessQueue()
        {
            _processing = true;

            while (_queue.Count > 0)
            {
                var e = _queue.Dequeue();
                yield return PlayAcquireToast(e);
            }

            _processing = false;
        }

        private IEnumerator PlayAcquireToast(PerkChangedEventData e)
        {
            // Nếu perk đã có icon (owned), chỉ update stacks và vẫn chơi toast (tuỳ bạn).
            // COD thường perk không stack, nhưng bạn có maxStacks nên mình update.
            if (_owned.TryGetValue(e.perkId, out var existingOwned) && existingOwned != null)
            {
                existingOwned.SetStacks(e.stacks);
            }

            // 1) Spawn toast icon ở giữa
            var toast = Instantiate(iconPrefab, toastStart);
            toast.name = $"PerkToast_{e.perkId}";
            toast.SetIcon(e.icon);
            toast.SetStacks(e.stacks);

            var toastRt = (RectTransform)toast.transform;
            toastRt.anchoredPosition = Vector2.zero;
            toastRt.localScale = Vector3.one;

            // pop nhẹ
            toastRt.DOKill();
            toastRt.DOPunchScale(Vector3.one * toastScalePunch, punchDuration, vibrato: 8, elasticity: 0.9f);

            // 2) chờ 1–2s
            yield return new WaitForSeconds(toastStaySeconds);

            // 3) đảm bảo có owned icon target (NHƯNG ẨN)
            var target = GetOrCreateOwnedIcon(e);
            var targetCg = GetOrAddCanvasGroup(target);
            targetCg.alpha = 0f;              // ẩn trong lúc toast bay
            targetCg.interactable = false;
            targetCg.blocksRaycasts = false;

            // 4) tween toast về đúng vị trí target
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)ownedBar.transform);

            var targetRt = (RectTransform)target.transform;
            Vector3 targetWorld = targetRt.position;

            yield return toastRt.DOMove(targetWorld, moveDuration)
                .SetEase(moveEase)
                .WaitForCompletion();

            // 5) khi toast tới nơi: hiện owned icon rồi mới destroy toast
            targetCg.DOKill();
            targetCg.DOFade(1f, 0.12f);

            Destroy(toast.gameObject);

        }

        private PerkIconView GetOrCreateOwnedIcon(PerkChangedEventData e)
        {
            if (_owned.TryGetValue(e.perkId, out var icon) && icon != null)
            {
                icon.SetIcon(e.icon);
                icon.SetStacks(e.stacks);
                return icon;
            }

            var created = Instantiate(iconPrefab, ownedBar.transform);
            created.name = $"PerkOwned_{e.perkId}";
            created.SetIcon(e.icon);
            created.SetStacks(e.stacks);

            // đảm bảo có CanvasGroup (để PlayAcquireToast điều khiển alpha)
            GetOrAddCanvasGroup(created).alpha = 1f;

            _owned[e.perkId] = created;
            return created;
        }
    

         private static CanvasGroup GetOrAddCanvasGroup(Component c)
        {
            var cg = c.GetComponent<CanvasGroup>();
            if (cg == null) cg = c.gameObject.AddComponent<CanvasGroup>();
            return cg;
        }

    }
}