using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace TT
{
    public class PerkIconView : MonoBehaviour
    {
        public Image iconImage;
        public TMP_Text stacksText; // optional

        public void SetIcon(Sprite s)
        {
            if (iconImage) iconImage.sprite = s;
        }

        public void SetStacks(int stacks)
        {
            if (!stacksText) return;

            // COD perk thường không stack => ẩn khi 1
            if (stacks <= 1) stacksText.gameObject.SetActive(false);
            else
            {
                stacksText.gameObject.SetActive(true);
                stacksText.text = stacks.ToString();
            }
        }
    }
}
