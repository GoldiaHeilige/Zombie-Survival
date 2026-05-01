public static class ShotFactory
{
    public static IShot Create(WeaponDef def)
    {
        switch (def.fireKind)
        {
            case WeaponDef.FireKind.Projectile: return new ProjectileShot();
            case WeaponDef.FireKind.Hitscan:
            default: return new HitscanShot();
        }
    }
}
