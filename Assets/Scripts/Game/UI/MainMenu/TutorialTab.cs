using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace TT.UI
{
    public class TutorialTabCycler : MonoBehaviour
    {
        [Header("Tabs")]
        [SerializeField] private List<GameObject> tabs = new();
        [SerializeField] private int startIndex = 0;
        [SerializeField] private bool loop = true;

        [Header("UI")]
        [SerializeField] private Button btnPrev;
        [SerializeField] private Button btnNext;
        [SerializeField] private TMP_Text labelIndex; // optional: "1/5"

        private int _index;

        private void Awake()
        {
            if (btnPrev) btnPrev.onClick.AddListener(Prev);
            if (btnNext) btnNext.onClick.AddListener(Next);

            _index = Mathf.Clamp(startIndex, 0, Mathf.Max(0, tabs.Count - 1));
            Apply();
        }

        private void OnDestroy()
        {
            if (btnPrev) btnPrev.onClick.RemoveListener(Prev);
            if (btnNext) btnNext.onClick.RemoveListener(Next);
        }

        public void Prev()
        {
            if (tabs.Count == 0) return;

            _index--;
            if (_index < 0) _index = loop ? tabs.Count - 1 : 0;

            Apply();
        }

        public void Next()
        {
            if (tabs.Count == 0) return;

            _index++;
            if (_index >= tabs.Count) _index = loop ? 0 : tabs.Count - 1;

            Apply();
        }

        private void Apply()
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                if (tabs[i]) tabs[i].SetActive(i == _index);
            }

            if (labelIndex)
                labelIndex.text = tabs.Count <= 0 ? "0/0" : $"{_index + 1}/{tabs.Count}";

            if (!loop)
            {
                if (btnPrev) btnPrev.interactable = _index > 0;
                if (btnNext) btnNext.interactable = _index < tabs.Count - 1;
            }
        }

        // Optional: nếu bạn muốn MenuController reset tab mỗi lần mở tutorial
        public void ResetToStart()
        {
            _index = Mathf.Clamp(startIndex, 0, Mathf.Max(0, tabs.Count - 1));
            Apply();
        }
    }
}
