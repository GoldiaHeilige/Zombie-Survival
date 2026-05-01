using UnityEngine;
using Game.Combat.Weapon.Recoil; // namespace của RecoilController

public class HitscanShot : IShot
{
    public void Fire(ref WeaponContext ctx)
    {
        // 1) Lấy origin/forward như cũ
        var camT = ctx.aimCam ? ctx.aimCam.transform : null;
        Vector3 origin = camT ? camT.position : (ctx.muzzle ? ctx.muzzle.position : Vector3.zero);
        Vector3 baseDir = camT ? camT.forward : (ctx.muzzle ? ctx.muzzle.forward : Vector3.forward);

        // 2) Lấy RecoilController nếu có (đặt trên WeaponHolder/FPS weapon)
        RecoilController rc = null;
        if (ctx.wc)
            rc = ctx.wc.GetComponentInChildren<RecoilController>();

        // 3) Xác định ADS (đổi theo flag của bạn nếu tên khác)
        bool isADS = ctx.isADS;

        // 4) Tính hướng bắn có/không cone
        Vector3 shotDir = baseDir;
        if (rc != null && camT != null)
        {
            // Ở đây mình truyền trạng thái di chuyển = false để đảm bảo compile ngay.
            // Sau bạn thay bằng cờ thực tế (IsMoving / IsAirborne / IsCrouching) nếu muốn.
            // 🔧 PATCH — truyền movement flags từ WeaponContext
            shotDir = rc.ComputeShotDirection(
                camT,
                isADS,
                ctx.isMoving,
                ctx.isAirborne,
                ctx.isCrouching
            );


            // Debug ray: hipfire = vàng, ADS = xanh
            // Vẽ debug chính XÁC với movement & ADS
            rc.DebugDrawShotRay(
                camT,
                isADS,
                ctx.isMoving,
                ctx.isAirborne,
                ctx.isCrouching,
                35f,
                0.25f
            );

        }

        // 5) Vẽ thêm 1 tia trắng để so sánh (center-line)
        Debug.DrawRay(origin, baseDir * 20f, Color.white, 0.25f);

        // 6) Thực thi bắn với hướng đã áp cone
        FireHitscan.Fire(ctx.wc, ctx.def, origin, shotDir);
        ctx.OnShotFX?.Invoke();

        // 7) Debug hit point (raycast TÁCH BIỆT chỉ để vẽ dấu, không gây sát thương)
        if (Physics.Raycast(origin, shotDir, out var hit, 250f, ~0, QueryTriggerInteraction.Ignore))
        {
            // vẽ một "dấu X" nhỏ tại điểm chạm
            float m = 0.12f;
            Debug.DrawRay(hit.point - hit.normal * m, hit.normal * (m * 2f), Color.magenta, 0.3f);
            Vector3 t1 = Vector3.Cross(hit.normal, Vector3.up); if (t1.sqrMagnitude < 1e-4f) t1 = Vector3.right;
            t1.Normalize();
            Vector3 t2 = Vector3.Cross(hit.normal, t1).normalized;
            Debug.DrawRay(hit.point - t1 * m, t1 * (m * 2f), Color.magenta, 0.3f);
            Debug.DrawRay(hit.point - t2 * m, t2 * (m * 2f), Color.magenta, 0.3f);
        }
    }
}
