// Shooting/Shots/ProjectileShot.cs
using UnityEngine;

public class ProjectileShot : IShot
{
    public void Fire(ref WeaponContext ctx)
    {
        if (ctx.def.projectilePrefab == null || ctx.muzzle == null)
        {
            ctx.OnShotFX?.Invoke();
            return;
        }

        var go = Object.Instantiate(ctx.def.projectilePrefab, ctx.muzzle.position, ctx.muzzle.rotation);

        var proj = go.GetComponent<BulletProjectile>();
        if (proj != null)
        {
            var attackerGO = ctx.wc && ctx.wc.owner ? ctx.wc.owner.gameObject : (ctx.wc ? ctx.wc.gameObject : null);
            proj.Init(attackerGO, ctx.def);
        }

        ctx.OnShotFX?.Invoke();
    }
}
