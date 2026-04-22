using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Runtime setup for the OpenWorld scene.
///
/// On Start it:
///   1. Resolves the player (existing GameObject by name, an explicit override,
///      or a spawned placeholder capsule).
///   2. Wires <see cref="ThirdPersonCamera"/> on the main camera to follow it.
///   3. Builds an on-screen virtual joystick UI at runtime.
///   4. Attaches <see cref="PlayerController"/> to the player and points it at
///      the joystick + camera.
///
/// Doing the joystick UI in code means you don't have to hand-build a Canvas
/// in the Editor — useful when iterating from outside Unity.
/// </summary>
public class OpenWorldSetup : MonoBehaviour
{
    [Header("Player")]
    [Tooltip("Name of an existing player GameObject in the scene. If found, it is used as the follow target.")]
    public string existingPlayerName = "Player";

    [Tooltip("Direct reference to the player. Wins over existingPlayerName if set.")]
    public Transform playerOverride;

    public Vector3 playerStartPosition = new Vector3(0f, 1f, 0f);
    public Color playerColor = new Color(0.3f, 0.6f, 1f);

    [Header("Movement")]
    public float moveSpeed = 3f;

    [Header("Joystick UI")]
    [Tooltip("Bottom-left offset from the screen edge in pixels.")]
    public Vector2 joystickMargin = new Vector2(160f, 160f);
    public float joystickRadius = 120f;
    public Color joystickBackgroundColor = new Color(1f, 1f, 1f, 0.25f);
    public Color joystickHandleColor = new Color(1f, 1f, 1f, 0.6f);

    [Header("Ground (placeholder, off by default)")]
    public bool spawnGround = false;
    public float groundSize = 30f;

    void Start()
    {
        if (spawnGround) CreateGround();

        Transform playerTransform = ResolvePlayer(out bool spawnedPlaceholder);
        WireCamera(playerTransform);

        var joystick = BuildJoystickUI();
        WirePlayerController(playerTransform, joystick, spawnedPlaceholder);
    }

    // -------------------- player --------------------

    private Transform ResolvePlayer(out bool spawnedPlaceholder)
    {
        spawnedPlaceholder = false;

        if (playerOverride != null) return playerOverride;

        if (!string.IsNullOrEmpty(existingPlayerName))
        {
            var existing = GameObject.Find(existingPlayerName);
            if (existing != null) return existing.transform;
        }

        spawnedPlaceholder = true;
        return CreatePlayer().transform;
    }

    private GameObject CreatePlayer()
    {
        var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = "Player";
        player.transform.position = playerStartPosition;
        ApplyUrpMaterial(player, playerColor);
        return player;
    }

    private void WirePlayerController(Transform playerTransform, OnScreenJoystick joystick, bool spawnedPlaceholder)
    {
        var go = playerTransform.gameObject;
        var controller = go.GetComponent<PlayerController>();
        if (controller == null) controller = go.AddComponent<PlayerController>();
        controller.joystick = joystick;
        controller.moveSpeed = moveSpeed;
        if (Camera.main != null) controller.cameraReference = Camera.main.transform;
    }

    private void CreateGround()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(groundSize / 10f, 1f, groundSize / 10f);
        ApplyUrpMaterial(ground, new Color(0.25f, 0.3f, 0.25f));
    }

    private static void ApplyUrpMaterial(GameObject go, Color color)
    {
        var renderer = go.GetComponent<Renderer>();
        if (renderer == null) return;

        var shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");
        if (shader == null) return;

        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        renderer.material = mat;
    }

    private void WireCamera(Transform target)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        var follow = cam.GetComponent<ThirdPersonCamera>();
        if (follow == null) follow = cam.gameObject.AddComponent<ThirdPersonCamera>();
        follow.target = target;
    }

    // -------------------- joystick UI --------------------

    private OnScreenJoystick BuildJoystickUI()
    {
        // Canvas
        var canvasGo = new GameObject("JoystickCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;

        // Background image
        var bgGo = new GameObject("JoystickBackground", typeof(Image));
        bgGo.transform.SetParent(canvasGo.transform, false);
        var bgRect = bgGo.GetComponent<RectTransform>();
        bgRect.anchorMin = bgRect.anchorMax = new Vector2(0f, 0f); // bottom-left
        bgRect.pivot = new Vector2(0.5f, 0.5f);
        bgRect.anchoredPosition = joystickMargin;
        bgRect.sizeDelta = new Vector2(joystickRadius * 2f, joystickRadius * 2f);
        var bgImage = bgGo.GetComponent<Image>();
        bgImage.sprite = CreateCircleSprite();
        bgImage.color = joystickBackgroundColor;
        bgImage.raycastTarget = false;

        // Handle image
        var handleGo = new GameObject("JoystickHandle", typeof(Image));
        handleGo.transform.SetParent(bgGo.transform, false);
        var handleRect = handleGo.GetComponent<RectTransform>();
        handleRect.anchorMin = handleRect.anchorMax = new Vector2(0.5f, 0.5f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.anchoredPosition = Vector2.zero;
        handleRect.sizeDelta = new Vector2(joystickRadius, joystickRadius);
        var handleImage = handleGo.GetComponent<Image>();
        handleImage.sprite = CreateCircleSprite();
        handleImage.color = joystickHandleColor;
        handleImage.raycastTarget = false;

        // Joystick component (lives on the canvas root for convenience)
        var joystick = canvasGo.AddComponent<OnScreenJoystick>();
        joystick.background = bgRect;
        joystick.handle = handleRect;
        joystick.radius = joystickRadius;

        return joystick;
    }

    /// <summary>
    /// Generates a soft white circle sprite at runtime so we don't need to
    /// ship a texture asset for the joystick visuals.
    /// </summary>
    private static Sprite _cachedCircle;
    private static Sprite CreateCircleSprite()
    {
        if (_cachedCircle != null) return _cachedCircle;

        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f - 1f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                // Soft anti-aliased edge.
                float alpha = Mathf.Clamp01(1f - (dist - (radius - 1.5f)) / 1.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        _cachedCircle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return _cachedCircle;
    }
}
