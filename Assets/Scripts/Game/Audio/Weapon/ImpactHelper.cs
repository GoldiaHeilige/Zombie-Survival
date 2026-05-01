using TT;
using UnityEngine;

public static class ImpactHelper
{
    public static SurfaceType DetectSurface(RaycastHit hit, bool isFlesh)
    {
        if (isFlesh)
            return SurfaceType.Flesh;

        if (hit.collider != null)
        {
            var surf = hit.collider.GetComponent<SurfaceMaterial>();
            if (surf != null)
                return surf.type;
        }

        return SurfaceType.Default;
    }

    public static GameObject GetVFX(WeaponDef def, SurfaceType surface)
    {
        if (def == null || def.impactVFX == null)
            return null;

        return surface switch
        {
            SurfaceType.Flesh => def.impactVFX.Flesh ?? def.impactVFX.Default,
            SurfaceType.Metal => def.impactVFX.Metal ?? def.impactVFX.Default,
            SurfaceType.Wood => def.impactVFX.Wood ?? def.impactVFX.Default,
            SurfaceType.Concrete => def.impactVFX.Concrete ?? def.impactVFX.Default,
            SurfaceType.Glass => def.impactVFX.Glass ?? def.impactVFX.Default,
            SurfaceType.Dirt => def.impactVFX.Dirt ?? def.impactVFX.Default,
            SurfaceType.Water => def.impactVFX.Water ?? def.impactVFX.Default,
            _ => def.impactVFX.Default,
        };
    }

    public static AudioEventSO GetSFX(WeaponDef def, SurfaceType surface)
    {
        if (def == null || def.impactSFX == null)
            return null;

        return surface switch
        {
            SurfaceType.Flesh => def.impactSFX.Flesh ?? def.impactSFX.Default,
            SurfaceType.Metal => def.impactSFX.Metal ?? def.impactSFX.Default,
            SurfaceType.Wood => def.impactSFX.Wood ?? def.impactSFX.Default,
            SurfaceType.Concrete => def.impactSFX.Concrete ?? def.impactSFX.Default,
            SurfaceType.Glass => def.impactSFX.Glass ?? def.impactSFX.Default,
            SurfaceType.Dirt => def.impactSFX.Dirt ?? def.impactSFX.Default,
            SurfaceType.Water => def.impactSFX.Water ?? def.impactSFX.Default,
            _ => def.impactSFX.Default,
        };
    }
}
