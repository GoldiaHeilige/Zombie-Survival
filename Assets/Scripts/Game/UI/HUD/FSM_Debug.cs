/*using UnityEngine;
using TMPro;

public class StateDebugHUD : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private KeyCode toggleKey = KeyCode.F1;

    [Header("Targets")]
    [SerializeField] private PlayerMovementController movement;
    [SerializeField] private WeaponController weapon;

    [Header("Options")]
    [SerializeField] private float refreshInterval = 0.1f;

    float _t;

    void Reset()
    {
        // Ưu tiên tìm trong chính GameObject/cây con
        if (label == null) label = GetComponentInChildren<TextMeshProUGUI>(true);

        // Fallback: Unity 6 API (không deprecated)
        if (movement == null)
            movement = Object.FindFirstObjectByType<PlayerMovementController>(FindObjectsInactive.Exclude);
        if (weapon == null)
            weapon = Object.FindFirstObjectByType<WeaponController>(FindObjectsInactive.Exclude);
    }

    void Awake()
    {
        if (label == null) label = GetComponentInChildren<TextMeshProUGUI>(true);

        if (movement == null)
            movement = Object.FindFirstObjectByType<PlayerMovementController>(FindObjectsInactive.Exclude);

        if (weapon == null)
            weapon = Object.FindFirstObjectByType<WeaponController>(FindObjectsInactive.Exclude);
    }

    public void SetWeapon(WeaponController wc) => weapon = wc;
    public void SetMovement(PlayerMovementController mv) => movement = mv;

    void Update()
    {
        if (label == null) return;

        if (Input.GetKeyDown(toggleKey))
            label.gameObject.SetActive(!label.gameObject.activeSelf);
        if (!label.gameObject.activeSelf) return;

        _t += Time.deltaTime;
        if (_t < refreshInterval) return;
        _t = 0f;

        // Movement
        string mvState = movement ? movement.state.ToString() : "N/A";
        bool grounded = movement && movement.TryGetComponent<CharacterController>(out var cc) && cc.isGrounded;
        float stamina = movement ? movement.stamina : 0f;

        // Weapon
        string wpState = (weapon && weapon.fsm != null) ? weapon.GetState().ToString() : "N/A";
        bool ads = weapon && weapon.IsADS();
        string wpnId = weapon && weapon.def != null ? weapon.def.weaponId : "(none)";

        label.text =
$@"<mspace=0.6em>
<b><size=115%>PLAYER</size></b>
State: <color=#7EC8E3>{mvState}</color>    <color=#{(grounded ? "8BE37E" : "E39E7E")}>{(grounded ? "Grounded" : "Air")}</color>
Stamina: <b>{stamina:0}</b>

<b><size=115%>WEAPON</size></b>
ID: <b>{wpnId}</b>
State: <color=#E39E7E>{wpState}</color>    ADS: <color=#{(ads ? "8BE37E" : "BBBBBB")}>{(ads ? "ON" : "OFF")}</color>
</mspace>";
    }
}
*/