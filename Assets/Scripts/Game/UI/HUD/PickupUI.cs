using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;

public class PickupUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI pickupText;
    [SerializeField] private PlayerPickup playerPickup;

    [Header("Input Actions")]
    [SerializeField] private InputActionReference pickupAction;   // “Pickup” (F)
    [SerializeField] private InputActionReference replaceAction;  // “ReplaceWeapon” (V)

    // ====== NEW: Cho phép bind từ ngoài (UILocalBinder) ======
    public void SetSource(PlayerPickup newPickup)
    {
        playerPickup = newPickup;
        if (pickupText != null) pickupText.gameObject.SetActive(false);
    }
    // =========================================================

    void Awake()
    {
        if (pickupText == null)
            pickupText = GetComponentInChildren<TextMeshProUGUI>(true);

        // Fallback cho SP
        if (playerPickup == null)
            playerPickup = Object.FindFirstObjectByType<PlayerPickup>();
    }

    void Start()
    {
        if (pickupText != null) pickupText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (pickupText == null || playerPickup == null) return;

        string pickupKeyName = "F";
        string replaceKeyName = "V";

        if (pickupAction != null && pickupAction.action != null && pickupAction.action.bindings.Count > 0)
        {
            pickupKeyName = pickupAction.action.GetBindingDisplayString(
                0,
                out _,
                out _,
                InputBinding.DisplayStringOptions.DontIncludeInteractions
            );
        }

        if (replaceAction != null && replaceAction.action != null && replaceAction.action.bindings.Count > 0)
        {
            replaceKeyName = replaceAction.action.GetBindingDisplayString(
                0,
                out _,
                out _,
                InputBinding.DisplayStringOptions.DontIncludeInteractions
            );
        }

        // ==== 1) ƯU TIÊN TRẠNG THÁI REPLACE ====
        if (playerPickup.HasPendingReplace)
        {
            pickupText.gameObject.SetActive(true);

            var oldName = playerPickup.PendingReplaceOldName;
            var newName = playerPickup.PendingReplaceNewName;

            if (string.IsNullOrEmpty(oldName)) oldName = "current weapon";
            if (string.IsNullOrEmpty(newName)) newName = "new weapon";

            pickupText.text = $"Press [{Bold(replaceKeyName)}] to replace \"{Bold(oldName)}\" with \"{Bold(newName)}\"";
            return;
        }

        // ==== 2) Không pending replace → fallback về logic cũ ====
        var ww = playerPickup.GetLookedWeapon();
        if (ww != null && ww.weaponDef != null)
        {
            var def = ww.weaponDef;

            // Nếu đã có cùng loại súng → chỉ hiện add ammo
            int sameSlot = playerPickup.FindSameWeaponSlot(def);
            if (sameSlot >= 0)
            {
                pickupText.gameObject.SetActive(true);
                pickupText.text = pickupText.text = $"Press [{Bold(pickupKeyName)}] to add {ww.reserveOnGround} ammo to \"{Bold(def.weaponName)}\"";
                return;
            }

            // Slot còn trống hoặc chưa full → pickup bình thường
            pickupText.gameObject.SetActive(true);
            pickupText.text = $"Press [{Bold(pickupKeyName)}] to pickup {Bold(def.weaponName)}";
            return;
        }

        // ==== 3) Không nhìn vào gì cả → ẩn text ====
        if (pickupText.gameObject.activeSelf)
            pickupText.gameObject.SetActive(false);
    }

    private static string Bold(string s) => $"<b>{s}</b>";
}
