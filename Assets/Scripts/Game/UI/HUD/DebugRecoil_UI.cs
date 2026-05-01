using TMPro;
using UnityEngine;
using Game.Combat.Weapon.Recoil;

public class DebugRecoilUI : MonoBehaviour
{
    public WeaponController weapon;
    public TMP_Text text;

    void Update()
    {
        if (!weapon || !weapon.def || !text) return;
        var recoil = weapon.GetComponent<RecoilController>();
        if (!recoil) return;

        text.text = $"STATE: {weapon.GetState()}\n" +
                    $"ADS: {weapon.IsADS()}\n" +
                    $"RecoilProfile: {recoil.CurrentProfileName}\n" +
                    $"Vert: {recoil.AccumVertical:F2}\n" +
                    $"Horz: {recoil.AccumHorizontal:F2}\n" +
                    $"Recovering: {recoil.IsRecovering}";
    }
}
