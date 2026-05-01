using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_RENDER_PIPELINE_UNIVERSAL
using UnityEngine.Rendering.Universal;
#endif

public static class GraphicsSettingsApplier
{
    public static void Apply(GraphicsSettingsData g)
    {
        if (g == null) return;

        // 1) Run in background
        Application.runInBackground = g.runInBackground;

        // 2) VSync + FPS cap
        if (g.vsync)
        {
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
        }
        else
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Mathf.Clamp(g.fpsCap, 1, 1000);
        }

        // 3) Render Scale
        float rs = Mathf.Clamp(g.renderScale, 0.1f, 2f);

        // ✅ CÁI NÀY MỚI LÀ “ĂN NGAY” (ép render buffer scale)
        // Nếu mày muốn thấy rõ hiệu ứng: set 0.5 -> sẽ mờ/nhòe + FPS tăng.
        ScalableBufferManager.ResizeBuffers(rs, rs);
        Debug.Log($"[Graphics] ScalableBufferManager.ResizeBuffers({rs}, {rs}) | currentRP={(GraphicsSettings.currentRenderPipeline ? GraphicsSettings.currentRenderPipeline.GetType().Name : "null")}");

        // (optional) vẫn set URP asset renderScale để đồng bộ với UI/asset
        TrySetURPRenderScale(rs);
    }

    private static void TrySetURPRenderScale(float rs)
    {
#if UNITY_RENDER_PIPELINE_UNIVERSAL
    // Lấy Asset đang chạy thực tế
    var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

    if (urpAsset != null)
    {
        // 1. Gán giá trị
        urpAsset.renderScale = rs;

        // 2. KHÓA CHÍNH: Trong Unity 6, bạn phải báo cho hệ thống là Asset đã thay đổi
        // để nó vẽ lại Render Target với kích thước mới
        EditorGraphicsSettings.SetDirty(); // Nếu chạy trong Editor
        
        // Với bản Build, cách tốt nhất để ép URP cập nhật RenderScale là:
        float temp = urpAsset.renderScale;
        urpAsset.renderScale = temp; 
        
        // Đổi Quality Level tạm thời rồi quay lại để Re-initialize Pipeline
        int currentQuality = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(currentQuality == 0 ? 1 : 0, false);
        QualitySettings.SetQualityLevel(currentQuality, false);

        Debug.Log($"[Graphics] Unity 6 Force Update RenderScale: {rs}");
    }
#endif
    }
}
