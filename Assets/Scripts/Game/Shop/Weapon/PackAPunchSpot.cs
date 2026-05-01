using UnityEngine;
using System.Collections;
using DG.Tweening;
#if FUSION_WEAVER
using Fusion;
#endif

/// <summary>
/// Pack-a-Punch machine.
/// Không equip trực tiếp, chỉ spawn WorldWeapon bản nâng cấp.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class PackAPunchSpot : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Giá nâng cấp vũ khí.")]
    public int upgradeCost = 5000;

    [Tooltip("Điểm A: vị trí súng xuất hiện bên ngoài (ra/vào máy).")]
    public Transform entryPoint;

    [Tooltip("Điểm B: vị trí trong máy (bên trong model).")]
    public Transform insidePoint;

    [Tooltip("Thời gian súng di chuyển A→B hoặc B→A.")]
    public float moveTime = 0.75f;

    [Tooltip("Thời gian 'chế biến' bên trong máy trước khi nhả bản nâng cấp.")]
    public float processingTime = 2.0f;

    [Tooltip("Khoảng cách tối đa để được tương tác.")]
    public float interactRange = 2.0f;

    [Header("Runtime")]
    [SerializeField] private bool _isBusy;
    private WorldWeapon _currentVisualWeapon;

    public bool IsBusy => _isBusy;

    void Reset()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;

        if (!entryPoint) entryPoint = transform;
        if (!insidePoint) insidePoint = transform;

        moveTime = 0.75f;
        processingTime = 2.0f;
        interactRange = 2.0f;
    }

    /// <summary>
    /// Có thể dùng PaP với weapon này không?
    /// </summary>
    public bool CanUseFor(WeaponDef baseDef)
    {
        if (_isBusy) return false;
        if (!baseDef) return false;
        if (!baseDef.upgradedVersion) return false;
        return true;
    }

    /// <summary>
    /// Gọi trên HOST/SP để bắt đầu upgrade (trừ point + chạy DOTween).
    /// </summary>
    public bool TryStartUpgrade(WeaponDef baseDef, PlayerPoints points, GameObject owner)
    {
        if (!CanUseFor(baseDef))
        {
            Debug.Log("[PaP] Cannot use for this weapon or machine busy.");
            return false;
        }

        if (!points)
        {
            Debug.LogWarning("[PaP] No PlayerPoints.");
            return false;
        }

        if (!points.TrySpend(upgradeCost, PointReason.Purchase, gameObject))
        {
            Debug.Log("[PaP] Not enough points to upgrade.");
            return false;
        }

        var upgradedDef = baseDef.upgradedVersion;
        if (!upgradedDef)
        {
            Debug.LogWarning("[PaP] upgradedVersion is null at runtime.");
            return false;
        }

        Debug.Log($"[PaP] Start upgrade {baseDef.weaponName} -> {upgradedDef.weaponName}");

        _isBusy = true;
        StartCoroutine(UpgradeRoutine(baseDef, upgradedDef));

        return true;
    }


    IEnumerator UpgradeRoutine(WeaponDef baseDef, WeaponDef upgradedDef)
    {
        // 1) Spawn visual súng cũ tại entryPoint, move A -> B, rồi destroy
        _currentVisualWeapon = SpawnWorldWeapon(baseDef, 0, 0, entryPoint, moveTime + 5f);

        if (_currentVisualWeapon)
        {
            yield return MoveWeapon(_currentVisualWeapon.transform,
                                    entryPoint.position,
                                    insidePoint.position,
                                    moveTime);

            // vào trong máy → phá visual cũ
            SafeDespawnWorldWeapon(_currentVisualWeapon);
            _currentVisualWeapon = null;
        }

        // 2) Thời gian "chế biến" bên trong
        if (processingTime > 0f)
            yield return new WaitForSeconds(processingTime);

        // 3) Spawn bản nâng cấp ở trong máy, move B -> A, cho nhặt
        int mag = upgradedDef.magSize;
        int reserve = upgradedDef.startReserve;

        _currentVisualWeapon = SpawnWorldWeapon(upgradedDef, mag, reserve, insidePoint, moveTime + 0.2f);
        if (_currentVisualWeapon)
        {
            yield return MoveWeapon(_currentVisualWeapon.transform,
                                    insidePoint.position,
                                    entryPoint.position,
                                    moveTime);

        //    Debug.Log($"[PaP] Upgraded weapon ready at {entryPoint.position}");
            // Tới đây, khẩu PaP nằm ở ngoài, player có thể nhặt.
        }

        _isBusy = false;
    }

    IEnumerator MoveWeapon(Transform t, Vector3 from, Vector3 to, float duration)
    {
        if (!t) yield break;
        if (duration <= 0f)
        {
            t.position = to;
            yield break;
        }

        float startTime = Time.time;
        float endTime = startTime + duration;

        // đảm bảo bắt đầu đúng vị trí
        t.position = from;

        while (Time.time < endTime && t)
        {
            float t01 = Mathf.InverseLerp(startTime, endTime, Time.time);
            t.position = Vector3.Lerp(from, to, t01);
        //    Debug.Log($"[PaP][Move] t01={t01:F2} pos={t.position}");
            yield return null;
        }

        if (t)
            t.position = to;
    }


    WorldWeapon SpawnWorldWeapon(WeaponDef def, int mag, int reserve, Transform anchor, float blockPickupSeconds)
    {
        if (!def || !def.worldPrefab || !anchor)
        {
            Debug.LogWarning("[PaP] SpawnWorldWeapon failed: missing def/worldPrefab/anchor");
            return null;
        }

        Vector3 pos = anchor.position;
        Quaternion rot = anchor.rotation;

        WorldWeapon ww = null;

#if FUSION_WEAVER
        var no = GetComponent<NetworkObject>();
        var runner = no ? no.Runner : null;

        if (runner != null)
        {
            var prefabNO = def.worldPrefab.GetComponent<NetworkObject>();
            if (!prefabNO)
            {
                Debug.LogError("[PaP] worldPrefab missing NetworkObject.");
                return null;
            }

            runner.Spawn(
                prefabNO,
                pos,
                rot,
                inputAuthority: null,
                onBeforeSpawned: (r, obj) =>
                {
                    ww = obj.GetComponent<WorldWeapon>();
                    if (ww != null)
                    {
                        ww.InitFromDrop(System.Guid.NewGuid().ToString(), def, mag, reserve);
                        ww.BlockPickupFor(blockPickupSeconds);
                    }
                });

            return ww;
        }
#endif

        // Singleplayer / no runner
        var go = Instantiate(def.worldPrefab, pos, rot);
        ww = go.GetComponent<WorldWeapon>();
        if (ww != null)
        {
            ww.InitFromDrop(System.Guid.NewGuid().ToString(), def, mag, reserve);
            ww.BlockPickupFor(blockPickupSeconds);
        }
        return ww;
    }

    void SafeDespawnWorldWeapon(WorldWeapon ww)
    {
        if (!ww) return;

#if FUSION_WEAVER
        var no = ww.GetComponent<NetworkObject>();
        if (no && no.Runner != null)
        {
            if (no.HasStateAuthority)
            {
                no.Runner.Despawn(no);
            }
            return;
        }
#endif
        Destroy(ww.gameObject);
    }
}
