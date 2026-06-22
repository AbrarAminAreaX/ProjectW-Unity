using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

// Self-contained top-down minimap for Rob11Scene_ShipWorld (or any scene).
//
// Spawns an orthographic camera that looks straight down at a target (the
// guardian by default), renders it to a RenderTexture, and shows it in a screen
// corner as a (optionally circular) RawImage widget. A blip marks the player;
// the map can stay fixed-north or rotate with the player's heading.
//
// Mirrors the runtime-build, self-contained style of
// SpiritGuardianJoystickController / OpenWorldSetup — the only Editor step is
// adding this one component and (optionally) assigning the target.
//
// INTERIOR NOTE: a straight-down camera sees the ceiling of an enclosed space.
// For the ship interior, set 'renderLayers' to exclude the roof, or put
// simplified floor/marker geometry on a dedicated layer and render only that.
[DisallowMultipleComponent]
public class MiniMap : MonoBehaviour
{
    public enum Corner { TopRight, TopLeft, BottomRight, BottomLeft }

    [Header("Target")]
    [Tooltip("What the minimap centers on. Defaults to this GameObject's transform.")]
    public Transform target;

    [Header("Map camera")]
    [Tooltip("Height above the target the map camera sits at.")]
    public float cameraHeight = 30f;
    [Tooltip("Orthographic half-size = how much of the world is visible. Smaller = more zoomed in.")]
    public float orthographicSize = 12f;
    [Tooltip("Layers the minimap renders. Exclude the ceiling here for enclosed interiors.")]
    public LayerMask renderLayers = ~0;
    [Tooltip("Background fill behind the map (areas with no geometry).")]
    public Color backgroundColor = new Color(0.95f, 0.96f, 0.98f, 1f);

    [Header("Behaviour")]
    [Tooltip("Rotate the map with the target's heading (true) or keep it fixed-north (false).")]
    public bool rotateWithTarget = false;

    [Header("UI placement")]
    public Corner corner = Corner.TopRight;
    [Tooltip("Size of the minimap widget in pixels (reference 1080x1920 canvas).")]
    public float size = 320f;
    public Vector2 margin = new Vector2(40f, 40f);
    [Tooltip("Round the minimap into a circle.")]
    public bool circular = true;
    public Color borderColor = new Color(1f, 1f, 1f, 0.5f);
    public float borderThickness = 6f;

    [Header("Player blip")]
    public bool showBlip = true;
    public Color blipColor = new Color(0.3f, 0.85f, 1f, 1f);
    public float blipSize = 26f;

    [Header("RenderTexture")]
    [Tooltip("Resolution of the off-screen minimap render. 256-512 is plenty.")]
    public int textureResolution = 512;

    private Camera _mapCamera;
    private RenderTexture _rt;
    private RectTransform _blipRect;

    void Start()
    {
        if (target == null) target = transform;
        BuildCamera();
        BuildUI();
    }

    void LateUpdate()
    {
        if (target == null || _mapCamera == null) return;

        // Follow the target from directly above.
        Vector3 p = target.position;
        _mapCamera.transform.position = new Vector3(p.x, p.y + cameraHeight, p.z);

        if (rotateWithTarget)
        {
            // Map turns with the player; blip stays pointing up.
            _mapCamera.transform.rotation = Quaternion.Euler(90f, target.eulerAngles.y, 0f);
            if (_blipRect != null) _blipRect.localEulerAngles = Vector3.zero;
        }
        else
        {
            // Fixed-north map; the blip rotates to show heading.
            _mapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            if (_blipRect != null)
                _blipRect.localEulerAngles = new Vector3(0f, 0f, -target.eulerAngles.y);
        }
    }

    void OnDestroy()
    {
        if (_rt != null) { _rt.Release(); Destroy(_rt); }
    }

    // ----------------------------------------------------------------------
    //  Map camera + render texture
    // ----------------------------------------------------------------------

    private void BuildCamera()
    {
        var go = new GameObject("MiniMapCamera");
        _mapCamera = go.AddComponent<Camera>();
        _mapCamera.orthographic = true;
        _mapCamera.orthographicSize = orthographicSize;
        _mapCamera.cullingMask = renderLayers;
        _mapCamera.clearFlags = CameraClearFlags.SolidColor;
        _mapCamera.backgroundColor = backgroundColor;
        _mapCamera.nearClipPlane = 0.3f;
        _mapCamera.farClipPlane = cameraHeight + 100f;
        _mapCamera.allowMSAA = false;

        // Configure as a URP base camera without post-processing.
        var camData = _mapCamera.GetUniversalAdditionalCameraData();
        camData.renderType = CameraRenderType.Base;
        camData.renderPostProcessing = false;
        camData.renderShadows = false;

        _rt = new RenderTexture(textureResolution, textureResolution, 16)
        {
            name = "MiniMapRT"
        };
        _mapCamera.targetTexture = _rt;
    }

    // ----------------------------------------------------------------------
    //  UI widget (runtime build)
    // ----------------------------------------------------------------------

    private void BuildUI()
    {
        var canvasGo = new GameObject("MiniMapCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;

        // Border / frame behind the map.
        var frameGo = new GameObject("MiniMapFrame", typeof(Image));
        frameGo.transform.SetParent(canvasGo.transform, false);
        var frameRect = frameGo.GetComponent<RectTransform>();
        AnchorToCorner(frameRect);
        frameRect.sizeDelta = new Vector2(size + borderThickness * 2f, size + borderThickness * 2f);
        var frameImg = frameGo.GetComponent<Image>();
        frameImg.color = borderColor;
        frameImg.sprite = circular ? CircleSprite() : null;
        frameImg.raycastTarget = false;

        // Mask defines the visible shape (circle or square) and clips the map.
        var maskGo = new GameObject("MiniMapMask", typeof(Image), typeof(Mask));
        maskGo.transform.SetParent(frameGo.transform, false);
        var maskRect = maskGo.GetComponent<RectTransform>();
        CenterFill(maskRect, size);
        var maskImg = maskGo.GetComponent<Image>();
        maskImg.sprite = circular ? CircleSprite() : null;
        maskImg.color = Color.white;
        maskImg.raycastTarget = false;
        maskGo.GetComponent<Mask>().showMaskGraphic = false;

        // The live map render.
        var mapGo = new GameObject("MiniMapImage", typeof(RawImage));
        mapGo.transform.SetParent(maskGo.transform, false);
        CenterFill(mapGo.GetComponent<RectTransform>(), size);
        var mapImage = mapGo.GetComponent<RawImage>();
        mapImage.texture = _rt;
        mapImage.raycastTarget = false;

        // Player blip on top (sibling of the mask so it isn't clipped).
        if (showBlip)
        {
            var blipGo = new GameObject("MiniMapBlip", typeof(Image));
            blipGo.transform.SetParent(frameGo.transform, false);
            _blipRect = blipGo.GetComponent<RectTransform>();
            _blipRect.anchorMin = _blipRect.anchorMax = new Vector2(0.5f, 0.5f);
            _blipRect.pivot = new Vector2(0.5f, 0.5f);
            _blipRect.anchoredPosition = Vector2.zero;
            _blipRect.sizeDelta = new Vector2(blipSize, blipSize);
            var blipImg = blipGo.GetComponent<Image>();
            blipImg.sprite = ArrowSprite();
            blipImg.color = blipColor;
            blipImg.raycastTarget = false;
        }
    }

    private void AnchorToCorner(RectTransform rect)
    {
        Vector2 a;
        Vector2 m;
        switch (corner)
        {
            case Corner.TopLeft:     a = new Vector2(0f, 1f); m = new Vector2( margin.x, -margin.y); break;
            case Corner.BottomRight: a = new Vector2(1f, 0f); m = new Vector2(-margin.x,  margin.y); break;
            case Corner.BottomLeft:  a = new Vector2(0f, 0f); m = new Vector2( margin.x,  margin.y); break;
            default:                 a = new Vector2(1f, 1f); m = new Vector2(-margin.x, -margin.y); break; // TopRight
        }
        rect.anchorMin = rect.anchorMax = a;
        rect.pivot = a;
        rect.anchoredPosition = m;
    }

    private static void CenterFill(RectTransform rect, float side)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(side, side);
    }

    // ----------------------------------------------------------------------
    //  Procedural sprites (no art assets needed)
    // ----------------------------------------------------------------------

    private static Sprite _cachedCircle;
    private static Sprite CircleSprite()
    {
        if (_cachedCircle != null) return _cachedCircle;
        const int s = 128;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Vector2 c = new Vector2(s / 2f, s / 2f);
        float r = s / 2f - 1f;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = Mathf.Clamp01(1f - (d - (r - 1.5f)) / 1.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        _cachedCircle = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
        return _cachedCircle;
    }

    private static Sprite _cachedArrow;
    private static Sprite ArrowSprite()
    {
        if (_cachedArrow != null) return _cachedArrow;
        const int s = 64;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                // Triangle pointing up: apex at top-center, base at bottom.
                float nx = (x / (float)(s - 1)) * 2f - 1f;     // -1..1
                float ny = y / (float)(s - 1);                  // 0 (bottom)..1 (top)
                bool inside = Mathf.Abs(nx) <= (1f - ny) && ny >= 0.1f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, inside ? 1f : 0f));
            }
        tex.Apply();
        _cachedArrow = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
        return _cachedArrow;
    }
}
