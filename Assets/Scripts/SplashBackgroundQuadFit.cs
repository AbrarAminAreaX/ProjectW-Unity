using UnityEngine;

// Resizes a background quad each frame so its content covers the camera
// view at the correct aspect ratio. Replaces hand-tuned localScale that
// only works for one specific screen aspect.
//
// Cover: video fills the whole screen; sides or top/bottom may crop.
// Contain: whole video is visible; black bars may appear.
[DefaultExecutionOrder(-100)]
[ExecuteAlways]
public class SplashBackgroundQuadFit : MonoBehaviour
{
    public enum FitMode
    {
        // Match camera HEIGHT, width scales with source aspect.
        // Landscape source on portrait screen → width overflows + sides
        // crop. Portrait source on landscape screen → bars on sides.
        // Best when you care that the full vertical content is visible.
        FitHeight,
        // Match camera WIDTH, height scales with source aspect.
        FitWidth,
        // Match the SMALLER dimension so the whole frame is visible (with
        // bars if aspects differ).
        Contain,
        // Match the LARGER dimension so the screen is fully covered
        // (cropping the overflow on the other axis).
        Cover,
    }

    public Camera viewCamera;
    [Tooltip("Distance from the camera along its forward axis. Should match the quad's local Z when parented to the camera.")]
    public float distance = 10f;
    [Tooltip("Source aspect (width / height). For a 1920×1080 video this is 1.7777.")]
    public float sourceAspect = 16f / 9f;
    // Cover = "auto-fit to fill screen". Picks FitHeight for landscape
    // sources on portrait screens and FitWidth for portrait sources on
    // landscape screens. Aspect always preserved; whichever axis overflows
    // gets cropped naturally by the camera frustum.
    public FitMode mode = FitMode.Cover;

    void OnEnable() => Apply();
    void LateUpdate() => Apply();

    void Apply()
    {
        if (viewCamera == null) return;
        float screenAspect = viewCamera.aspect;
        if (screenAspect <= 0.0001f || sourceAspect <= 0.0001f) return;

        float fovRad = viewCamera.fieldOfView * Mathf.Deg2Rad;
        float camH = 2f * distance * Mathf.Tan(fovRad * 0.5f);
        float camW = camH * screenAspect;

        bool sourceWider = sourceAspect > screenAspect;
        float quadW, quadH;
        switch (mode)
        {
            case FitMode.FitHeight:
                quadH = camH;
                quadW = camH * sourceAspect;
                break;
            case FitMode.FitWidth:
                quadW = camW;
                quadH = camW / sourceAspect;
                break;
            case FitMode.Cover:
                if (sourceWider) { quadH = camH; quadW = camH * sourceAspect; }
                else { quadW = camW; quadH = camW / sourceAspect; }
                break;
            case FitMode.Contain:
            default:
                if (sourceWider) { quadW = camW; quadH = camW / sourceAspect; }
                else { quadH = camH; quadW = camH * sourceAspect; }
                break;
        }
        transform.localScale = new Vector3(quadW, quadH, 1f);
    }
}
