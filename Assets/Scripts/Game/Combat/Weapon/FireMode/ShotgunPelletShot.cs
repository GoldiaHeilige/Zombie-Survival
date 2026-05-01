using UnityEngine;
using Game.Combat.Weapon.Recoil;
using System.Collections.Generic;

public class ShotgunPelletShot : IShot
{
    public void Fire(ref WeaponContext ctx)
    {
        var camT = ctx.aimCam ? ctx.aimCam.transform : null;
        Vector3 origin = camT ? camT.position : (ctx.muzzle ? ctx.muzzle.position : Vector3.zero);
        Vector3 baseDir = camT ? camT.forward : (ctx.muzzle ? ctx.muzzle.forward : Vector3.forward);


        // Recoil base direction (nếu có)
        RecoilController rc = null;
        if (ctx.wc)
            rc = ctx.wc.GetComponentInChildren<RecoilController>();

        bool isADS = ctx.isADS;

        Vector3 centerDir = baseDir;
        if (rc != null && camT != null)
        {
            centerDir = rc.ComputeShotDirection(
                camT,
                isADS,
                ctx.isMoving,
                ctx.isAirborne,
                ctx.isCrouching
            );
        }

        int pellets = Mathf.Max(1, ctx.def != null ? ctx.def.pelletCount : 1);
        float spreadDeg = 0f;
        if (ctx.def != null)
            spreadDeg = isADS ? ctx.def.pelletSpreadDegADS : ctx.def.pelletSpreadDegHip;

        int impactCount = Random.Range(3, 7); // 3–6 impact
        impactCount = Mathf.Min(impactCount, pellets);

        HashSet<int> impactPelletIndices = new HashSet<int>();
        while (impactPelletIndices.Count < impactCount)
        {
            impactPelletIndices.Add(Random.Range(0, pellets));
        }


        for (int i = 0; i < pellets; i++)
        {
            Vector3 pelletDir = ApplyCone(centerDir, spreadDeg);

            // Impact chỉ spawn 1 lần (pellet đầu)
            bool spawnImpact = impactPelletIndices.Contains(i);
            FireHitscan.Fire(ctx.wc, ctx.def, origin, pelletDir, spawnImpact);

            // (tuỳ bạn) nếu muốn per-pellet callback hit thì phải làm sâu hơn,
            // hiện tại ctx.OnHit đang được xử lý bên trong FireHitscan/Damage pipeline.
        }

        ctx.OnShotFX?.Invoke();
    }

    static Vector3 ApplyCone(Vector3 dir, float spreadDeg)
    {
        if (spreadDeg <= 0.0001f) return dir.normalized;

        dir = dir.normalized;

        // tạo basis quanh dir
        Vector3 right = Vector3.Cross(Vector3.up, dir);
        if (right.sqrMagnitude < 1e-6f)
            right = Vector3.Cross(Vector3.forward, dir);
        right.Normalize();
        Vector3 up = Vector3.Cross(dir, right).normalized;

        Vector2 off = Random.insideUnitCircle * spreadDeg;
        Quaternion q = Quaternion.AngleAxis(off.x, up) * Quaternion.AngleAxis(off.y, right);
        return (q * dir).normalized;
    }
}
