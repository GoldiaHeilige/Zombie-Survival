using UnityEngine;

[DisallowMultipleComponent]
public class PlayerFPArmView : MonoBehaviour
{
    [Header("FP Arms Renderer")]
    [Tooltip("SkinnedMeshRenderer của tay FPS. Nếu để trống, script sẽ tự tìm trong children.")]
    [SerializeField] private SkinnedMeshRenderer armsRenderer;

    /// <summary>
    /// Gán mesh + material tay FPS. Gọi hàm này SAU khi view FP (tay + súng) đã spawn.
    /// </summary>
    public void Apply(Mesh mesh, Material material)
    {
        if (mesh == null)
        {
            Debug.LogWarning("[FPArmView] Mesh tay null, bỏ qua.", this);
            return;
        }

        // Nếu renderer cũ không còn hợp lệ thì tìm lại
        if (armsRenderer == null)
        {
            // NEW: chỉ lấy ACTIVE để tránh renderer của prefab cũ (đã bị SetActive(false))
            var renderers = GetComponentsInChildren<SkinnedMeshRenderer>(false);

            if (renderers == null || renderers.Length == 0)
            {
                // fallback: nếu prefab tay đang inactive vì lý do nào đó
                renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            }

            if (renderers == null || renderers.Length == 0)
            {
                Debug.LogWarning("[FPArmView] Không tìm thấy SkinnedMeshRenderer trong children của " + name, this);
                return;
            }

            // NEW: chọn đúng renderer tay (đừng lấy bừa [0])
            SkinnedMeshRenderer pick = null;
            foreach (var r in renderers)
            {
                var n = r.name.ToLowerInvariant();
                if (n.Contains("HandFPS"))
                {
                    pick = r;
                    break;
                }
            }
            armsRenderer = pick != null ? pick : renderers[0];
        }

        armsRenderer.sharedMesh = mesh;

        if (material != null)
            armsRenderer.sharedMaterial = material;
    }

}
