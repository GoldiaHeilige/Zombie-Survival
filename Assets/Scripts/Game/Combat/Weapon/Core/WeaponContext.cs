using System;
using UnityEngine;

public struct WeaponContext
{
    // Ai đang bắn (để truyền qua damage / projectile)
    public WeaponController wc;

    // Cấu hình súng & tham số raycast
    public WeaponDef def;
    public Camera aimCam;
    public Transform muzzle;
    public LayerMask hitMask;

    // Trạng thái & hook mới (phục vụ recoil/spread)
    public bool isADS;
    public bool isMoving;
    public bool isAirborne;
    public bool isCrouching;

    /// <summary>
    /// Yêu cầu một vectơ lệch (yaw,pitch) theo độ cho HIPFIRE (random trong vòng tròn).
    /// Nếu null, coi như không spread.
    /// </summary>
    public Func<Vector2> RequestHipfireSpreadDeg;

    // Hook tuỳ chọn
    public Action<RaycastHit> OnHit; // nếu muốn xử lý trúng đạn
    public Action OnShotFX;          // muzzle/anim/sfx sau khi bắn
}
