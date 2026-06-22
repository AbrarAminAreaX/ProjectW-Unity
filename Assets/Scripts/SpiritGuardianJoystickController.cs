using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Joystick-driven free-fly controller for the spirit guardian in
/// Rob11Scene_ShipWorld.
///
/// Behaviour:
///   • An on-screen virtual joystick (see <see cref="OnScreenJoystick"/>) moves
///     the guardian around a horizontal plane, relative to the camera
///     (stick-up = away from camera, stick-right = camera-right). A gentle
///     procedural bob is layered on top so it always feels like it's floating.
///   • While the stick is held, the guardian banks to face its movement
///     direction. The moment the stick is released it smoothly turns back to
///     LOOK AT the camera (the user), like an attentive companion.
///   • A particle "thruster flare" fires downward from beneath the guardian to
///     sell the flying feel. It idles softly and flares up while moving.
///   • A <see cref="GuardianFollowCamera"/> is attached to the camera so it
///     trails the guardian.
///   • The URP Depth of Field focal length is driven from movement: it sits at
///     a blurred idle value (49 by default) when stopped and drops toward 1
///     while flying so the world reads sharp in motion.
///
/// Self-contained: if no joystick / thruster is assigned in the inspector it
/// builds them at runtime (overlay canvas + particle system), so the only
/// Editor step is adding this one component to the Rob11 GameObject. Reuses the
/// runtime-UI approach from <see cref="OpenWorldSetup"/>.
///
/// On Start it disables conflicting transform writers (<see cref="Rob11Ctrl"/>,
/// the old <see cref="SpiritGuardianFlyController"/>, and Animator root motion)
/// so nothing fights this controller for the transform.
/// </summary>
[DisallowMultipleComponent]
public class SpiritGuardianJoystickController : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Virtual joystick. Left empty = one is built at runtime.")]
    public OnScreenJoystick joystick;

    [Tooltip("Camera the movement is relative to / the guardian looks at on idle. Defaults to Camera.main.")]
    public Transform cameraTransform;

    [Header("Movement")]
    [Tooltip("Fly speed in units/second at full stick deflection.")]
    public float moveSpeed = 2.5f;

    [Tooltip("How quickly the guardian accelerates / decelerates toward the target velocity.")]
    public float acceleration = 6f;

    [Tooltip("How quickly the raw stick reading is smoothed before it drives movement. Higher = snappier, lower = floatier. Tames finger jitter and abrupt direction flicks.")]
    public float inputSmoothing = 14f;

    [Tooltip("Response curve on stick deflection. 1 = linear (twitchy). >1 gives fine control near center and ramps up toward the edge — 1.6 feels good.")]
    [Range(1f, 3f)]
    public float inputResponseExponent = 1.6f;

    [Header("Rotation")]
    [Tooltip("Turn speed (deg/sec) when banking toward the movement direction.")]
    public float turnSpeed = 360f;

    [Tooltip("Turn speed (deg/sec) when realigning to face the camera after release.")]
    public float faceCameraSpeed = 220f;

    [Tooltip("Yaw offset (degrees) added to the facing. Set to 180 if the model ends up facing away from the camera.")]
    public float modelForwardYawOffset = 0f;

    [Tooltip("Keep the guardian upright (only yaw) instead of tilting toward the camera/movement.")]
    public bool keepUpright = true;

    [Header("Procedural hover")]
    public float bobAmplitude = 0.08f;
    public float bobFrequency = 1.0f;

    [Header("Thruster flare")]
    [Tooltip("Local offset from the guardian pivot where the thruster sits (negative Y = below).")]
    public Vector3 thrusterOffset = new Vector3(0f, -0.6f, 0f);

    [Tooltip("Overall scale of the thruster particles. Bump this up/down to match the model size.")]
    public float thrusterScale = 1f;

    public Color thrusterColor = new Color(1f, 0.55f, 0.15f, 1f);

    [Tooltip("Particles/sec when idle vs. flying at full deflection.")]
    public float thrusterIdleRate = 18f;
    public float thrusterMoveRate = 90f;

    [Header("Follow camera")]
    [Tooltip("Attach a GuardianFollowCamera to the camera and point it at the guardian at runtime.")]
    public bool autoSetupFollowCamera = true;

    [Header("Depth of Field")]
    [Tooltip("Drive the URP Depth of Field focal length from movement: blurry when idle, sharp when flying. " +
             "Disabled for now — the background stays non-blurred. Tick to re-enable the movement-driven blur.")]
    public bool controlDepthOfField = false;

    [Tooltip("Volume holding the Depth of Field override. Left empty = first one found in the scene.")]
    public Volume dofVolume;

    [Tooltip("Focal length when the guardian is stopped (more background blur).")]
    public float focalLengthIdle = 49f;

    [Tooltip("Focal length when the guardian is moving (minimized to remove blur).")]
    public float focalLengthMoving = 1f;

    [Tooltip("How quickly the focal length reacts to start/stop. Higher = snappier.")]
    public float dofResponse = 6f;

    [Header("Collision (walls)")]
    [Tooltip("Route movement through a CharacterController so the guardian collides with and slides along walls instead of passing through them. A capsule is added at runtime if none exists.")]
    public bool useCharacterController = true;
    public float colliderRadius = 0.3f;
    public float colliderHeight = 1.2f;
    [Tooltip("Capsule center relative to the guardian pivot. Raise Y if the pivot is at the feet.")]
    public Vector3 colliderCenter = new Vector3(0f, 0.6f, 0f);
    [Tooltip("Max height of a step the capsule can climb directly.")]
    public float stepOffset = 0.4f;
    public float slopeLimit = 60f;

    [Header("Auto floor follow (stairs / floors)")]
    [Tooltip("Keep the guardian hovering a fixed height above whatever floor is below it, so it automatically rises/descends over stairs and ramps. No up/down input needed.")]
    public bool followFloor = true;

    [Tooltip("How high above the floor the guardian hovers.")]
    public float hoverHeight = 1.0f;

    [Tooltip("Layers treated as floor for the downward probe. Default = everything.")]
    public LayerMask groundMask = ~0;

    [Tooltip("How far ahead (along movement) the floor is sampled, so the guardian lifts BEFORE reaching a step instead of bumping into the riser.")]
    public float floorLookAhead = 0.6f;

    [Tooltip("How high above the guardian the downward floor ray starts.")]
    public float floorRayUp = 1.5f;

    [Tooltip("How far down the floor ray probes for a surface.")]
    public float floorRayDown = 8f;

    [Tooltip("How quickly the hover height adapts to floor-height changes (higher = snappier stair following).")]
    public float floorFollowResponse = 8f;

    [Header("Conflict suppression")]
    public bool disableRob11Ctrl = true;
    public bool disableOldFlyController = true;
    public bool disableRootMotion = true;

    // Runtime
    private Vector3 _velocity;         // smoothed world velocity (horizontal)
    private Vector2 _smoothedInput;    // shaped + smoothed stick reading
    private float _phase;
    private CharacterController _controller;
    private float _baseY;              // smoothed floor-follow height (before bob)
    private bool _baseYInit;
    private static readonly RaycastHit[] _floorHits = new RaycastHit[8];
    private Animator _animator;
    private ParticleSystem _thruster;
    private ParticleSystem.EmissionModule _thrusterEmission;
    private ParticleSystem.MainModule _thrusterMain;
    private DepthOfField _dof;
    private float _dofBlend;           // 0 = idle (blur), 1 = moving (sharp)

    // External joystick input forwarded from Flutter (the embedded Unity view
    // doesn't reliably deliver drag touches to the on-screen joystick, so
    // Flutter drives it instead). Once it arrives we hide the runtime joystick.
    public static SpiritGuardianJoystickController Instance { get; private set; }
    private bool _useExternalInput;
    private Vector2 _externalInput;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    /// Called (via Rob11SceneManager.SetJoystick) with normalized stick values
    /// in [-1..1]. Flutter sends "0,0" on release.
    public void SetExternalInput(float x, float y)
    {
        _externalInput = new Vector2(x, y);
        if (!_useExternalInput)
        {
            _useExternalInput = true;
            // Hide the runtime-built on-screen joystick — Flutter provides one.
            if (joystick != null) joystick.gameObject.SetActive(false);
        }
    }

    void Start()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        _animator = GetComponentInChildren<Animator>();

        if (disableRootMotion && _animator != null)
            _animator.applyRootMotion = false;

        if (disableRob11Ctrl)
        {
            var ctrl = GetComponent<Rob11Ctrl>();
            if (ctrl != null) ctrl.enabled = false;
        }

        if (disableOldFlyController)
        {
            var old = GetComponent<SpiritGuardianFlyController>();
            if (old != null) old.enabled = false;
        }

        if (joystick == null) joystick = BuildJoystickUI();
        BuildThruster();

        if (autoSetupFollowCamera && cameraTransform != null)
            SetupFollowCamera();

        if (controlDepthOfField)
            ResolveDepthOfField();
        else
            DisableDepthOfField(); // keep the logic, but never blur the background for now

        if (useCharacterController)
            SetupController();

        _baseY = transform.position.y; // corrected on the first floor sample
        _phase = Random.value * Mathf.PI * 2f;
    }

    private void SetupController()
    {
        _controller = GetComponent<CharacterController>();
        if (_controller == null)
            _controller = gameObject.AddComponent<CharacterController>();

        _controller.radius = colliderRadius;
        _controller.height = colliderHeight;
        _controller.center = colliderCenter;
        _controller.stepOffset = Mathf.Min(stepOffset, colliderHeight * 0.5f);
        _controller.slopeLimit = slopeLimit;
        _controller.minMoveDistance = 0f; // apply even tiny moves (smooth following)
    }

    // Finds the floor height beneath 'probe' via a downward ray, skipping the
    // guardian's own colliders. Returns false if nothing was hit.
    private bool SampleFloor(Vector3 probe, out float floorY)
    {
        floorY = 0f;
        Vector3 origin = new Vector3(probe.x, transform.position.y + floorRayUp, probe.z);
        float dist = floorRayUp + floorRayDown;

        int n = Physics.RaycastNonAlloc(
            origin, Vector3.down, _floorHits, dist, groundMask, QueryTriggerInteraction.Ignore);

        bool found = false;
        float bestY = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            var h = _floorHits[i];
            if (h.collider == null) continue;
            if (h.collider.transform.IsChildOf(transform)) continue; // skip self/children
            if (h.point.y > bestY) { bestY = h.point.y; found = true; }
        }

        if (found) floorY = bestY;
        return found;
    }

    private void SetupFollowCamera()
    {
        // Don't let the grounded third-person rig fight the flying follow rig.
        var tpc = cameraTransform.GetComponent<ThirdPersonCamera>();
        if (tpc != null) tpc.enabled = false;

        var follow = cameraTransform.GetComponent<GuardianFollowCamera>();
        if (follow == null) follow = cameraTransform.gameObject.AddComponent<GuardianFollowCamera>();
        follow.target = transform;
        follow.enabled = true;
    }

    private void ResolveDepthOfField()
    {
        if (dofVolume == null)
        {
            var volumes = FindObjectsByType<Volume>(FindObjectsSortMode.None);
            foreach (var v in volumes)
            {
                if (v.profile != null && v.profile.TryGet(out DepthOfField found))
                {
                    dofVolume = v;
                    _dof = found;
                    break;
                }
            }
        }
        else if (dofVolume.profile != null)
        {
            dofVolume.profile.TryGet(out _dof);
        }

        if (_dof != null)
        {
            // focalLength only drives blur in Bokeh mode.
            _dof.active = true;
            _dof.mode.overrideState = true;
            _dof.mode.value = DepthOfFieldMode.Bokeh;
            _dof.focalLength.overrideState = true;
            _dof.focalLength.value = focalLengthIdle;
        }
        else
        {
            Debug.LogWarning("[SpiritGuardianJoystickController] No DepthOfField override found; DoF control disabled.");
        }
    }

    // DoF blur is turned off for now. Find the Depth of Field override and
    // deactivate it so the background always renders sharp, regardless of what
    // the scene's Volume profile has baked in. _dof is intentionally left null
    // so UpdateDepthOfField() stays a no-op. Re-enabling is just flipping
    // controlDepthOfField back on (ResolveDepthOfField then takes over).
    private void DisableDepthOfField()
    {
        DepthOfField dof = null;
        if (dofVolume != null && dofVolume.profile != null)
        {
            dofVolume.profile.TryGet(out dof);
        }
        else
        {
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (v.profile != null && v.profile.TryGet(out dof))
                    break;
            }
        }

        if (dof != null)
        {
            dof.active = false;                      // no DoF contribution → sharp background
            dof.focalLength.value = focalLengthMoving;
        }
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;

        // ---- Input shaping + smoothing ----
        Vector2 rawInput = _useExternalInput
            ? _externalInput
            : (joystick != null ? joystick.Value : Vector2.zero);

        // Ease the deflection so small tilts give fine control and it ramps up
        // toward the edge (also softens the dead-zone edge into a gentle onset).
        float mag = rawInput.magnitude;
        if (mag > 0.0001f)
            rawInput = (rawInput / mag) * Mathf.Pow(Mathf.Clamp01(mag), inputResponseExponent);

        // Smooth the reading itself (frame-rate independent) so jitter and
        // abrupt flicks don't translate straight into motion.
        _smoothedInput = Vector2.Lerp(_smoothedInput, rawInput, 1f - Mathf.Exp(-inputSmoothing * dt));

        Vector2 input = _smoothedInput;
        bool moving = input.sqrMagnitude > 0.0001f;

        // ---- Movement: camera-relative on a flat horizontal plane ----
        Vector3 desiredVel = Vector3.zero;
        if (moving && cameraTransform != null)
        {
            Vector3 camF = cameraTransform.forward; camF.y = 0f; camF.Normalize();
            Vector3 camR = cameraTransform.right;   camR.y = 0f; camR.Normalize();
            Vector3 dir = camR * input.x + camF * input.y;
            desiredVel = Vector3.ClampMagnitude(dir, 1f) * moveSpeed;
        }
        // Frame-rate-independent smoothing toward the target velocity (the old
        // acceleration*dt lerp could overshoot at low frame rates).
        _velocity = Vector3.Lerp(_velocity, desiredVel, 1f - Mathf.Exp(-acceleration * dt));
        _velocity.y = 0f; // horizontal only; vertical is driven by floor-follow

        // ---- Procedural hover bob ----
        float bob = Mathf.Sin(Time.time * bobFrequency + _phase) * bobAmplitude;

        // ---- Auto floor-follow: keep hoverHeight above the floor below ----
        if (followFloor)
        {
            // Sample slightly ahead so we rise before reaching a step's riser.
            Vector3 probe = transform.position;
            if (_velocity.sqrMagnitude > 0.0001f)
                probe += _velocity.normalized * floorLookAhead;

            if (SampleFloor(probe, out float floorY))
            {
                float targetY = floorY + hoverHeight;
                _baseY = _baseYInit
                    ? Mathf.Lerp(_baseY, targetY, floorFollowResponse * dt)
                    : targetY;
                _baseYInit = true;
            }
            // No floor found -> hold current height (don't drop into the void).
        }
        else
        {
            _baseY = transform.position.y - bob;
        }

        // ---- Apply movement ----
        float desiredWorldY = _baseY + bob;
        Vector3 motion = _velocity * dt;
        motion.y = desiredWorldY - transform.position.y;

        if (_controller != null && _controller.enabled)
            _controller.Move(motion);          // collide & slide against walls
        else
            transform.position += motion;       // fallback if no controller

        // ---- Rotation ----
        UpdateRotation(moving, dt);

        // ---- Thruster intensity ----
        UpdateThruster(input.magnitude);

        // ---- Depth of Field: blurry when idle, sharp when moving ----
        if (controlDepthOfField)
            UpdateDepthOfField(dt);
    }

    private void UpdateDepthOfField(float dt)
    {
        if (_dof == null) return;

        // Drive off actual speed so it ramps smoothly with acceleration.
        float moveFactor = Mathf.Clamp01(_velocity.magnitude / Mathf.Max(0.01f, moveSpeed));
        _dofBlend = Mathf.Lerp(_dofBlend, moveFactor, dofResponse * dt);
        _dof.focalLength.value = Mathf.Lerp(focalLengthIdle, focalLengthMoving, _dofBlend);
    }

    private void UpdateRotation(bool moving, float dt)
    {
        Vector3 faceDir;
        float speed;

        if (moving && _velocity.sqrMagnitude > 0.0001f)
        {
            // Bank toward where we're flying.
            faceDir = _velocity;
            speed = turnSpeed;
        }
        else if (cameraTransform != null)
        {
            // Idle: look back at the camera / user.
            faceDir = cameraTransform.position - transform.position;
            speed = faceCameraSpeed;
        }
        else
        {
            return;
        }

        if (keepUpright) faceDir.y = 0f;
        if (faceDir.sqrMagnitude < 0.0001f) return;

        Quaternion target = Quaternion.LookRotation(faceDir.normalized, Vector3.up)
                            * Quaternion.Euler(0f, modelForwardYawOffset, 0f);

        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, speed * dt);
    }

    private void UpdateThruster(float inputMagnitude)
    {
        if (_thruster == null) return;

        float t = Mathf.Clamp01(inputMagnitude);
        _thrusterEmission.rateOverTime = Mathf.Lerp(thrusterIdleRate, thrusterMoveRate, t);
        _thrusterMain.startSpeedMultiplier = Mathf.Lerp(1.5f, 3.5f, t) * thrusterScale;
    }

    // ----------------------------------------------------------------------
    //  Thruster flare (runtime particle system)
    // ----------------------------------------------------------------------

    private void BuildThruster()
    {
        var go = new GameObject("ThrusterFlare");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = thrusterOffset;
        // Cone emits along local +Z; rotate so +Z points straight down.
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        _thruster = go.AddComponent<ParticleSystem>();
        _thruster.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        _thrusterMain = _thruster.main;
        _thrusterMain.simulationSpace = ParticleSystemSimulationSpace.World; // trails when flying
        _thrusterMain.startLifetime = 0.45f;
        _thrusterMain.startSpeed = 2f;
        _thrusterMain.startSize = 0.35f * thrusterScale;
        _thrusterMain.startColor = thrusterColor;
        _thrusterMain.maxParticles = 300;
        _thrusterMain.gravityModifier = 0f;

        _thrusterEmission = _thruster.emission;
        _thrusterEmission.rateOverTime = thrusterIdleRate;

        var shape = _thruster.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 14f;
        shape.radius = 0.08f * thrusterScale;

        // Fade + shrink over life for a soft flame plume.
        var col = _thruster.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(1f, 0.85f, 0.6f), 0.5f),
                new GradientColorKey(new Color(1f, 0.4f, 0.1f), 1f)
            },
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = grad;

        var sol = _thruster.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(
            1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));

        // Renderer + soft additive-ish material.
        var psr = go.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.material = BuildParticleMaterial();

        _thruster.Play();
    }

    private static Material _cachedParticleMat;
    private static Material BuildParticleMaterial()
    {
        if (_cachedParticleMat != null) return _cachedParticleMat;

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Particles/Standard Unlit")
                     ?? Shader.Find("Mobile/Particles/Additive")
                     ?? Shader.Find("Sprites/Default");

        var mat = new Material(shader);
        mat.mainTexture = SoftDotTexture();
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);

        // Push toward additive transparent if the shader exposes blend modes.
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // transparent
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 1f);     // additive
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        _cachedParticleMat = mat;
        return mat;
    }

    private static Texture2D _cachedDot;
    private static Texture2D SoftDotTexture()
    {
        if (_cachedDot != null) return _cachedDot;

        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Vector2 c = new Vector2(size / 2f, size / 2f);
        float r = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c) / r;
                float a = Mathf.Clamp01(1f - d);
                a = a * a; // softer falloff
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        _cachedDot = tex;
        return tex;
    }

    // ----------------------------------------------------------------------
    //  Joystick UI (runtime) — mirrors OpenWorldSetup so no Canvas is needed
    // ----------------------------------------------------------------------

    [Header("Joystick UI (runtime build)")]
    public Vector2 joystickMargin = new Vector2(180f, 220f);
    public float joystickRadius = 120f;
    public Color joystickBackgroundColor = new Color(1f, 1f, 1f, 0.22f);
    public Color joystickHandleColor = new Color(1f, 1f, 1f, 0.55f);

    private OnScreenJoystick BuildJoystickUI()
    {
        var canvasGo = new GameObject("GuardianJoystickCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;

        var bgGo = new GameObject("JoystickBackground", typeof(Image));
        bgGo.transform.SetParent(canvasGo.transform, false);
        var bgRect = bgGo.GetComponent<RectTransform>();
        bgRect.anchorMin = bgRect.anchorMax = new Vector2(0f, 0f);
        bgRect.pivot = new Vector2(0.5f, 0.5f);
        bgRect.anchoredPosition = joystickMargin;
        bgRect.sizeDelta = new Vector2(joystickRadius * 2f, joystickRadius * 2f);
        var bgImage = bgGo.GetComponent<Image>();
        bgImage.sprite = CircleSprite();
        bgImage.color = joystickBackgroundColor;
        bgImage.raycastTarget = false;

        var handleGo = new GameObject("JoystickHandle", typeof(Image));
        handleGo.transform.SetParent(bgGo.transform, false);
        var handleRect = handleGo.GetComponent<RectTransform>();
        handleRect.anchorMin = handleRect.anchorMax = new Vector2(0.5f, 0.5f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.anchoredPosition = Vector2.zero;
        handleRect.sizeDelta = new Vector2(joystickRadius, joystickRadius);
        var handleImage = handleGo.GetComponent<Image>();
        handleImage.sprite = CircleSprite();
        handleImage.color = joystickHandleColor;
        handleImage.raycastTarget = false;

        var js = canvasGo.AddComponent<OnScreenJoystick>();
        js.background = bgRect;
        js.handle = handleRect;
        js.radius = joystickRadius;
        return js;
    }

    private static Sprite _cachedCircle;
    private static Sprite CircleSprite()
    {
        if (_cachedCircle != null) return _cachedCircle;
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Vector2 c = new Vector2(size / 2f, size / 2f);
        float r = size / 2f - 1f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = Mathf.Clamp01(1f - (d - (r - 1.5f)) / 1.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        _cachedCircle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return _cachedCircle;
    }
}
