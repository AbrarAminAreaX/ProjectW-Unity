using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Onboarding_Animation cinematic for ProjectW — "Cosmic Genesis" splash.
// Rig-free: the Spirit Guardian (Tripo FBX) is 20 separate meshes with no
// bones/Animator, so every beat is procedural. Coroutine-driven to match the
// SplashSceneController convention; all timings are inspector-tweakable.
//
//   Phase 1 (0.0–1.5) Cosmic Genesis    — 20 parts fly in from a scattered
//                                          shell along arcs + spin and lock.
//   Phase 2 (1.5–3.0) Guardian's Gift   — robot floats forward; heart blooms
//                                          + pulses at the chest; smile glows on.
//   Phase 3 (3.0–4.5) Logo Transform    — camera pushes in, robot fades back,
//                                          heart hands off to MorphCanvas, spins
//                                          and crossfade-morphs into the "W".
//   @4.5  haptic_double + W locks.
//   Phase 4 (4.5–6.0) Interface Reveal  — staggered headline → subhead →
//                                          button (slide+glow) → footer.
//   Hold — wait for "Get Started" tap → SendToFlutter.Send("onboarding_get_started").
public class OnboardingAnimationController : MonoBehaviour
{
    [Header("Core refs")]
    public Transform robotRoot;
    public Camera targetCamera;
    [Tooltip("Background fitters whose viewCamera should be bound to targetCamera at runtime (nebula quad, etc.).")]
    public List<SplashBackgroundQuadFit> backgroundFitters = new List<SplashBackgroundQuadFit>();
    public bool autoPlayOnStart = true;

    // ── Phase 1 — Cosmic Genesis ────────────────────────────────────────
    // Parts don't fly in from far away anymore. They start scattered only a
    // SHORT distance from their final spot (already roughly robot-shaped),
    // pop in by scaling 0→full at random times, then slide the small gap home.
    [Header("Phase 1 — Assembly (Cosmic Genesis)")]
    public float phase1Duration = 1.5f;
    [Tooltip("Exploded spread at the start: each part begins at this multiple of its distance from the robot's center (at scale 0), then scales up while closing in. 1 = no spread.")]
    public float expandFactor = 3f;
    [Tooltip("How long each part takes to scale up + close in.")]
    public float partFlight = 0.7f;
    [Tooltip("Sideways arc bulge of each part's path as it flies in (world units). 0 = straight lines.")]
    public float arcAmount = 0.35f;
    [Tooltip("Degrees each part spins while it scales up + closes in. 0 = no spin (parts keep their orientation).")]
    public float spinDegrees = 0f;
    [Tooltip("Damped overshoot when a part snaps in. 0 = none.")]
    public float partOvershoot = 0.12f;
    public float partSettle = 0.12f;
    [Tooltip("Bilateral assembly: parts whose final position is LEFT of the robot's center fly in from the left and RIGHT parts from the right, then meet to form the robot — instead of exploding radially out from the center.")]
    public bool splitFromSides = true;
    [Tooltip("How far out to each side (world units) the two halves start, added on top of the robot's own half-width, before they slide in.")]
    public float sideSpread = 2.5f;
    [Tooltip("Random depth/height scatter on the side-start positions so each wall of parts isn't perfectly flat. 0 = flat walls.")]
    public float sideJitter = 0.3f;
    [Tooltip("How spread out the parts' start times are. 0 = every part launches together (smoothest, least 'sequenced'); 1 = fully staggered across phase 1. Lower = smoother / less timed-looking.")]
    [Range(0f, 1f)] public float assemblyStagger = 0.4f;
    public AnimationCurve assemblyEase = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 1.2f),
        new Keyframe(1f, 1f, 0.2f, 0f));

    // ── Phase 2 — Guardian's Gift ───────────────────────────────────────
    [Header("Phase 2 — Guardian's Gift")]
    public float phase2Duration = 1.5f;
    [Tooltip("World-space offset the robot floats by during phase 2 (toward camera + up a touch).")]
    public Vector3 floatOffset = new Vector3(0f, 0.15f, -0.6f);
    [Tooltip("Glowing heart object at the chest (quad/sprite renderer). Blooms + pulses. Optional.")]
    public Transform heart;
    public Renderer heartRenderer;
    public Color heartColor = new Color(1f, 0.25f, 0.35f);
    public float heartBloomScale = 1f;
    public float heartEmission = 3f;
    [Tooltip("Optional left/right chest plate transforms. They rotate their local Y to 0 (swing open) to reveal the heart — they are NOT hidden.")]
    public Transform chestLeft;
    public Transform chestRight;
    [Tooltip("Fraction of phase 2 spent opening the chest before the heart begins to appear (0..1).")]
    [Range(0.1f, 0.9f)] public float chestOpenPortion = 0.5f;
    [Tooltip("Smile glow object on the face (drawn on during phase 2). Optional.")]
    public Transform smileGlow;
    [Tooltip("Uniform scale the smile grows to at full bloom (x=y=z). It animates 0 → this.")]
    public float smileMaxScale = 0.2f;

    // ── Phase 3 — Logo Transformation ───────────────────────────────────
    [Header("Phase 3 — Logo Transformation")]
    public float phase3Duration = 1.5f;
    [Tooltip("Camera dolly toward the heart (world units along forward).")]
    public float cameraPush = 4.5f;
    public float cameraStartFov = 45f;
    public float cameraPushFov = 30f;
    [Tooltip("MorphCanvas group containing the heart image (screen space). Fades in as the camera reaches the heart.")]
    public CanvasGroup morphGroup;
    public Image morphHeartImage;
    [Tooltip("Legacy screen-space W image. Now kept hidden — the 3D 'W_Logo' below is revealed instead.")]
    public Image morphWImage;
    [Tooltip("In-scene 3D 'W_Logo' object. The heart crossfades into this (scales 0 → its authored scale).")]
    public Transform wLogo;
    [Tooltip("Total spin of the heart as it morphs, in degrees.")]
    public float morphSpin = 540f;
    public float morphHeartStartScale = 0.6f;
    public float morphWEndScale = 1f;
    [Tooltip("The W_Logo flies in along Z: it starts at this local Z (e.g. -10, far back) and slides forward to its authored resting Z. No rotation, no scaling.")]
    public float wLogoStartZ = -10f;

    // ── Revamped flow (current) ─────────────────────────────────────────
    // After assembly: robot moves toward camera; body parts fade out (alpha)
    // leaving the full-scale heart; heart spins a few turns, becomes the W; the
    // W moves back to its resting center.
    [Header("Approach + Body Fade + Heart→W")]
    [Tooltip("Seconds for the chest to open (revealing the heart) before the robot moves.")]
    public float chestOpenDuration = 0.7f;
    [Tooltip("World Z the robot moves to as it approaches the camera (camera sits at z = -10).")]
    public float approachTargetZ = -9f;
    [Tooltip("Seconds for the robot to move to the camera.")]
    public float approachDuration = 1.3f;
    [Tooltip("Seconds for the body to fade out (alpha), leaving only the heart.")]
    public float bodyFadeDuration = 0.9f;
    [Tooltip("How many full turns the heart spins before it becomes the W. Use whole numbers so it eases to a stop facing exactly where it started (no half-turn snap).")]
    public float heartSpins = 2f;
    [Tooltip("WORLD axis the heart spins around. Y (0,1,0) = vertical turntable, Z (0,0,1) = roll. Avoid X.")]
    public Vector3 heartSpinAxis = new Vector3(0f, 1f, 0f);
    [Tooltip("Seconds for the heart spin.")]
    public float heartSpinDuration = 1.1f;
    [Tooltip("Seconds at the TAIL of the spin where the heart crossfades into the W: the heart shrinks to nothing while the W grows in at the same spot, so it looks like the heart turns into the W. Overlaps the end of the spin, so keep it <= heartSpinDuration.")]
    public float heartMorphDuration = 0.45f;
    [Tooltip("Seconds for the W to move from the heart's spot back to its resting center.")]
    public float wLogoMoveDuration = 0.9f;
    [Tooltip("W scale at the swap (as a fraction of its resting scale), so it doesn't fill the screen up close. It grows to full as it recedes.")]
    public float wLogoSwapStartScale = 0.15f;

    [Header("Haptic")]
    [Tooltip("Message sent to Flutter at the 4.5s lock for the double-pulse haptic. Flutter host must handle it.")]
    public string hapticMessage = "haptic_double";

    // ── Phase 4 — Interface Reveal ──────────────────────────────────────
    [Header("Phase 4 — Interface Reveal")]
    public float phase4Duration = 1.5f;
    public CanvasGroup headlineGroup;
    public CanvasGroup subheadGroup;
    public CanvasGroup buttonGroup;
    public CanvasGroup footerGroup;
    [Tooltip("Headline/subhead TMP text. When typewriter is on these reveal character-by-character.")]
    public TMP_Text headlineText;
    public TMP_Text subheadText;
    [Tooltip("Reveal the headline + subhead with a typewriter effect (text appears as it's written). Off = plain fade.")]
    public bool typewriter = true;
    [Tooltip("Typewriter speed, characters per second.")]
    public float typeCharsPerSecond = 26f;
    [Tooltip("Seconds between each element starting to reveal.")]
    public float revealStagger = 0.28f;
    public float elementFade = 0.45f;
    [Tooltip("How far the button slides up into place (anchored units).")]
    public float buttonSlideUp = 60f;
    public RectTransform buttonRect;

    [Header("Completion")]
    public Button getStartedButton;
    [Tooltip("Message sent to Flutter to load the main scene (on tap, or automatically when the sequence ends).")]
    public string getStartedMessage = "onboarding_get_started";
    [Tooltip("When the sequence ends, automatically tell Flutter to load the main scene — no tap needed.")]
    public bool autoAdvanceOnEnd = true;
    [Tooltip("Seconds to hold the final screen after the reveal before auto-advancing.")]
    public float autoAdvanceDelay = 1.5f;

    // ── Internal ────────────────────────────────────────────────────────
    private Transform[] _parts;
    private Vector3[] _finalPos;
    private Quaternion[] _finalRot;
    private Vector3[] _finalScale;
    private Vector3[] _scatterPos;
    private Quaternion[] _scatterRot;
    // Per-part start delay. Parts are ordered top→bottom and heavily overlapped
    // so the robot materializes as one smooth, continuous build.
    private float[] _startDelay;
    private float _assembleTotal;
    private MaterialPropertyBlock _mpb;
    private Vector3 _robotStartPos;
    // Authored home poses, restored in OnDisable so play-mode motion never
    // leaks into the saved scene.
    private Vector3 _homePos; private Quaternion _homeRot; private Vector3 _homeScale;
    private Vector3 _camHomePos; private Quaternion _camHomeRot; private float _camHomeFov;
    private Vector3 _heartBaseScale;
    private Vector3 _smileHomeScale; private bool _hasSmileHome;
    private Vector3 _wLogoBaseScale; private Quaternion _wLogoBaseRot; private Vector3 _wLogoBasePos; private bool _hasWLogo;
    // Body-fade: ALL robot child renderers except the heart (so the Cube and any
    // extra children fade too) + their runtime material instances switched to
    // transparent so the body can alpha-fade away, leaving only the heart.
    private Renderer[] _bodyRenderers;
    private List<Material> _fadeMats;
    private bool _bodyFadePrepared;
    private int _headlineMaxVis = -1, _subheadMaxVis = -1;   // authored TMP maxVisibleCharacters
    // Closed (authored) chest-plate rotations + their "open" targets (local Y = 0).
    private Quaternion _chestLeftClosed, _chestRightClosed;
    private Quaternion _chestLeftOpen, _chestRightOpen;
    private bool _haptated;
    private bool _completed;   // guards the Flutter "load main scene" message (fires once)

    // Trailing index of a "tripo_part_<N>" name (e.g. "tripo_part_11" → 11).
    // Returns -1 for names without a plain integer suffix (the Cube, "6.001", …).
    static int PartIndex(string name)
    {
        int us = name.LastIndexOf('_');
        if (us < 0 || us + 1 >= name.Length) return -1;
        return int.TryParse(name.Substring(us + 1), out int v) ? v : -1;
    }

    static float Rand(int i, int salt)
    {
        float v = Mathf.Sin(i * 12.9898f + salt * 78.233f) * 43758.5453f;
        return v - Mathf.Floor(v);
    }

    // Smootherstep (Ken Perlin): zero 1st AND 2nd derivative at both ends, so
    // motion eases in and out with no snap — the basis of the smooth assembly.
    static float SmootherStep(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * x * (x * (x * 6f - 15f) + 10f);
    }

    void Awake()
    {
        // Keep the cinematic advancing even when the editor/app loses focus.
        Application.runInBackground = true;
        if (robotRoot == null) robotRoot = transform;
        if (targetCamera == null) targetCamera = Camera.main;
        _mpb = new MaterialPropertyBlock();

        // Bind any background fitters to the camera (the MCP can't assign the
        // Camera reference, so it's wired here at runtime). Auto-discover them
        // if the list wasn't populated in the inspector.
        if (backgroundFitters == null || backgroundFitters.Count == 0)
            backgroundFitters = new List<SplashBackgroundQuadFit>(FindObjectsOfType<SplashBackgroundQuadFit>(true));
        foreach (var f in backgroundFitters)
            if (f != null && f.viewCamera == null) f.viewCamera = targetCamera;

        ResolveRefs();
        CacheParts();

        // Cache chest plates' closed rotation now (Awake = still assembled,
        // before Phase-1 scatter) and the open target with local Y zeroed.
        if (chestLeft != null)
        {
            _chestLeftClosed = chestLeft.localRotation;
            Vector3 e = chestLeft.localEulerAngles;
            _chestLeftOpen = Quaternion.Euler(e.x, 0f, e.z);
        }
        if (chestRight != null)
        {
            _chestRightClosed = chestRight.localRotation;
            Vector3 e = chestRight.localEulerAngles;
            _chestRightOpen = Quaternion.Euler(e.x, 0f, e.z);
        }

        if (smileGlow != null) { _smileHomeScale = smileGlow.localScale; _hasSmileHome = true; }
        if (wLogo != null) { _wLogoBaseScale = wLogo.localScale; _wLogoBaseRot = wLogo.localRotation; _wLogoBasePos = wLogo.localPosition; _hasWLogo = true; }
        if (headlineText != null) _headlineMaxVis = headlineText.maxVisibleCharacters;
        if (subheadText != null) _subheadMaxVis = subheadText.maxVisibleCharacters;

        _homePos = robotRoot.position; _homeRot = robotRoot.rotation; _homeScale = robotRoot.localScale;
        if (targetCamera != null)
        {
            _camHomePos = targetCamera.transform.position;
            _camHomeRot = targetCamera.transform.rotation;
            _camHomeFov = targetCamera.fieldOfView;
        }
    }

    // Runs when play mode stops. Some editor/MCP play sessions fail to revert
    // transforms the controller animated; restore them so the scene stays clean.
    void OnDisable()
    {
        if (_parts != null)
        {
            for (int i = 0; i < _parts.Length; i++)
            {
                if (_parts[i] == null) continue;
                _parts[i].localPosition = _finalPos[i];
                _parts[i].localRotation = _finalRot[i];
                _parts[i].localScale = _finalScale[i];
            }
        }
        if (robotRoot != null)
        {
            robotRoot.position = _homePos;
            robotRoot.rotation = _homeRot;
            robotRoot.localScale = _homeScale;
        }
        if (smileGlow != null && _hasSmileHome) smileGlow.localScale = _smileHomeScale;
        if (wLogo != null && _hasWLogo) { wLogo.localPosition = _wLogoBasePos; wLogo.localScale = _wLogoBaseScale; wLogo.localRotation = _wLogoBaseRot; }
        // Re-show body parts and the heart that the fade/swap hid.
        if (_bodyRenderers != null) foreach (var r in _bodyRenderers) if (r != null) r.enabled = true;
        if (heart != null) { heart.gameObject.SetActive(true); if (_heartBaseScale != Vector3.zero) heart.localScale = _heartBaseScale; }
        if (headlineText != null && _headlineMaxVis >= 0) headlineText.maxVisibleCharacters = _headlineMaxVis;
        if (subheadText != null && _subheadMaxVis >= 0) subheadText.maxVisibleCharacters = _subheadMaxVis;
        if (targetCamera != null)
        {
            targetCamera.transform.position = _camHomePos;
            targetCamera.transform.rotation = _camHomeRot;
            targetCamera.fieldOfView = _camHomeFov;
        }
    }

    // The MCP can't assign object references in the inspector, so the scene is
    // authored with conventional names and everything is wired here at runtime.
    // Serialized fields still win if set manually.
    void ResolveRefs()
    {
        if (heart == null) heart = robotRoot.Find("ChestHeart");
        if (heart != null && heartRenderer == null) heartRenderer = heart.GetComponent<Renderer>();
        if (smileGlow == null) smileGlow = robotRoot.Find("FaceSmile");

        if (morphGroup == null)      morphGroup      = FindComp<CanvasGroup>("UICanvas/MorphRoot");
        if (morphHeartImage == null) morphHeartImage = FindComp<Image>("UICanvas/MorphRoot/MorphHeart");
        if (morphWImage == null)     morphWImage     = FindComp<Image>("UICanvas/MorphRoot/MorphW");
        if (wLogo == null)           { var w = GameObject.Find("W_Logo"); if (w != null) wLogo = w.transform; }
        if (headlineGroup == null)   headlineGroup   = FindComp<CanvasGroup>("UICanvas/Headline");
        if (subheadGroup == null)    subheadGroup    = FindComp<CanvasGroup>("UICanvas/Subhead");
        if (headlineText == null)    headlineText    = FindComp<TMP_Text>("UICanvas/Headline");
        if (subheadText == null)     subheadText     = FindComp<TMP_Text>("UICanvas/Subhead");
        if (buttonGroup == null)     buttonGroup     = FindComp<CanvasGroup>("UICanvas/GetStarted");
        if (buttonRect == null)      buttonRect      = FindComp<RectTransform>("UICanvas/GetStarted");
        if (getStartedButton == null) getStartedButton = FindComp<Button>("UICanvas/GetStarted");
        if (footerGroup == null)     footerGroup     = FindComp<CanvasGroup>("UICanvas/Footer");
    }

    static T FindComp<T>(string path) where T : Component
    {
        var go = GameObject.Find(path);
        return go != null ? go.GetComponent<T>() : null;
    }

    void CacheParts()
    {
        // Only the mesh fragments assemble. Any extra children we parent to the
        // robot (chest heart, face smile) are skipped by the name filter. The
        // manually-placed "Cube" is treated as a body fragment too, so it flies
        // in with the rest of the parts instead of just popping in at the end.
        var list = new List<Transform>();
        for (int i = 0; i < robotRoot.childCount; i++)
        {
            var c = robotRoot.GetChild(i);
            if (c.name.StartsWith("tripo_part") || c.name == "Cube") list.Add(c);
        }
        _parts = list.ToArray();

        // Body renderers to fade = every renderer under the robot EXCEPT the
        // heart's, so the Cube and any extra children fade with the body too.
        var skip = new HashSet<Renderer>();
        if (heart != null) foreach (var hr in heart.GetComponentsInChildren<Renderer>(true)) skip.Add(hr);
        var rends = new List<Renderer>();
        foreach (var r in robotRoot.GetComponentsInChildren<Renderer>(true))
            if (r != null && !skip.Contains(r)) rends.Add(r);
        _bodyRenderers = rends.ToArray();

        int n = _parts.Length;
        _finalPos = new Vector3[n];
        _finalRot = new Quaternion[n];
        _finalScale = new Vector3[n];
        _scatterPos = new Vector3[n];
        _scatterRot = new Quaternion[n];
        _startDelay = new float[n];

        // Start window: every part must finish (scale + close-in + settle) by
        // the end of phase 1, so the latest a part may start is this. Scaled by
        // assemblyStagger so 0 collapses all starts to t=0 (one smooth merge).
        float window = Mathf.Max(0f, phase1Duration - partFlight - partSettle) * Mathf.Clamp01(assemblyStagger);

        // Cache final pose + the centroid of all parts (the explosion origin).
        Vector3 centroid = Vector3.zero;
        for (int i = 0; i < n; i++)
        {
            _finalPos[i] = _parts[i].localPosition;
            _finalRot[i] = _parts[i].localRotation;
            _finalScale[i] = _parts[i].localScale;
            centroid += _finalPos[i];
        }
        if (n > 0) centroid /= n;

        // Bounding radius — used to scale the organic jitter to the robot's size.
        float maxR = 0.01f;
        for (int i = 0; i < n; i++)
        {
            float d = (_finalPos[i] - centroid).magnitude;
            if (d > maxR) maxR = d;
        }

        // Bilateral mode works in WORLD space: robotRoot is rotated (the robot
        // faces away from the camera), so a part's LOCAL x is not screen left/
        // right. Classify and launch each part along the WORLD x axis instead,
        // then convert the start point back to local for the assembly math.
        Vector3 worldCentroid = robotRoot.TransformPoint(centroid);
        float worldHalfWidthX = 0.01f;
        for (int i = 0; i < n; i++)
        {
            float wdx = Mathf.Abs(robotRoot.TransformPoint(_finalPos[i]).x - worldCentroid.x);
            if (wdx > worldHalfWidthX) worldHalfWidthX = wdx;
        }

        for (int i = 0; i < n; i++)
        {
            Vector3 fromCenter = _finalPos[i] - centroid;
            // Explicit per-part sides: tripo_part_0..6 enter from the screen-LEFT,
            // tripo_part_11..19 from the screen-RIGHT; every other part (7..10,
            // the Cube, the unparsed ones) keeps the radial explosion. Turn off
            // splitFromSides to make ALL parts radial again.
            int pidx = PartIndex(_parts[i].name);
            int sideSel = 0;
            if (splitFromSides)
            {
                if (pidx >= 0 && pidx <= 6) sideSel = -1;        // left
                else if (pidx >= 11 && pidx <= 19) sideSel = 1;  // right
            }
            if (sideSel != 0)
            {
                // Bilateral origin (WORLD space): the part slides horizontally in
                // from its side at roughly its final height/depth, so the two
                // walls of parts meet in the middle. robotRoot is rotated, so the
                // launch is computed in world space then converted back to local.
                // Jitter on depth/height keeps each wall from looking flat.
                Vector3 worldFinal = robotRoot.TransformPoint(_finalPos[i]);
                float side = sideSel;
                Vector3 worldStart = new Vector3(
                    worldCentroid.x + side * (worldHalfWidthX + sideSpread),
                    worldFinal.y + (Rand(i, 2) - 0.5f) * sideJitter * 2f,
                    worldFinal.z + (Rand(i, 3) - 0.5f) * sideJitter * 2f);
                worldStart.x += (Rand(i, 1) - 0.5f) * sideJitter;
                // Back to local — Phase 1 drives parts by localPosition, and
                // robotRoot is stationary through assembly so this stays valid.
                _scatterPos[i] = robotRoot.InverseTransformPoint(worldStart);
            }
            else
            {
                // Exploded origin: push each part OUTWARD from the centroid by
                // expandFactor (proportional — keeps the robot's silhouette), then
                // add organic jitter so the scatter is asymmetric and has depth
                // rather than a perfect symmetric ring. Parts scale up fast at this
                // spot, then fly in along an arc to form the robot.
                if (fromCenter.sqrMagnitude < 1e-4f)
                    fromCenter = new Vector3(Rand(i, 1) - 0.5f, Rand(i, 2) - 0.5f, Rand(i, 3) - 0.5f).normalized * 0.05f;
                Vector3 jitter = new Vector3(Rand(i, 1) - 0.5f, Rand(i, 2) - 0.5f, Rand(i, 3) - 0.5f) * (maxR * 0.6f);
                _scatterPos[i] = centroid + fromCenter * expandFactor + jitter;
            }

            Vector3 spinAxis = new Vector3(Rand(i, 4) - 0.5f, Rand(i, 5) - 0.5f, Rand(i, 6) - 0.5f).normalized;
            _scatterRot[i] = _finalRot[i] * Quaternion.AngleAxis(spinDegrees, spinAxis);
        }

        // Orchestrated top-down build: parts start in order of height (highest
        // first), spread evenly across the window. Because each part's flight is
        // long relative to the gap between starts, many parts animate at once and
        // the robot reads as one smooth, continuous materialization — not pops.
        // A tiny jitter keeps it organic without breaking the top-down flow.
        int[] order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        if (splitFromSides)
            // Outermost parts first (by WORLD x): the two side walls sweep inward
            // and converge toward the center.
            System.Array.Sort(order, (a, b) =>
                Mathf.Abs(robotRoot.TransformPoint(_finalPos[b]).x - worldCentroid.x)
                  .CompareTo(Mathf.Abs(robotRoot.TransformPoint(_finalPos[a]).x - worldCentroid.x)));
        else
            System.Array.Sort(order, (a, b) => _finalPos[b].y.CompareTo(_finalPos[a].y));
        float slot = n > 1 ? window / (n - 1) : 0f;
        for (int r = 0; r < n; r++)
        {
            float jit = (Rand(order[r], 8) - 0.5f) * slot * 0.5f;
            _startDelay[order[r]] = Mathf.Clamp(r * slot + jit, 0f, window);
        }

        _assembleTotal = Mathf.Max(phase1Duration, window + partFlight + partSettle);
    }

    void Start()
    {
        ApplyInitialPose();
        if (autoPlayOnStart) StartCoroutine(PlaySequence());
    }

    public void Play()
    {
        ApplyInitialPose();
        StartCoroutine(PlaySequence());
    }

    void ApplyInitialPose()
    {
        _robotStartPos = robotRoot.position;

        for (int i = 0; i < _parts.Length; i++)
        {
            _parts[i].localPosition = _scatterPos[i];
            _parts[i].localRotation = _scatterRot[i];
            _parts[i].localScale = Vector3.zero;       // pop in by scaling up
        }

        if (heart != null)
        {
            _heartBaseScale = heart.localScale;
            heart.localScale = Vector3.zero;
            heart.gameObject.SetActive(true);
        }
        // Chest plates start closed (their authored rotation); Phase-1 assembly
        // already lands them there, so nothing to reset here.
        if (smileGlow != null) smileGlow.localScale = Vector3.zero;   // grows uniformly to smileMaxScale

        // The 3D W logo starts hidden (scaled to zero); Phase 3 reveals it.
        if (wLogo != null && _hasWLogo) wLogo.localScale = Vector3.zero;

        if (targetCamera != null) targetCamera.fieldOfView = cameraStartFov;

        SetGroup(morphGroup, 0f, false);
        SetGroup(headlineGroup, 0f, false);
        SetGroup(subheadGroup, 0f, false);
        SetGroup(buttonGroup, 0f, false);
        SetGroup(footerGroup, 0f, false);

        // Start the typewriter texts with no visible characters so they don't
        // flash full before Phase 4 writes them on.
        if (typewriter)
        {
            if (headlineText != null) headlineText.maxVisibleCharacters = 0;
            if (subheadText != null) subheadText.maxVisibleCharacters = 0;
        }

        if (getStartedButton != null)
            getStartedButton.onClick.AddListener(OnGetStarted);
    }

    IEnumerator PlaySequence()
    {
        yield return Phase1_Assembly();
        yield return Phase2_ApproachAndFade();
        yield return Phase3_HeartToW();         // fires haptic at its end
        yield return Phase4_InterfaceReveal();

        // When the sequence ends, auto-advance to the main scene (Flutter loads
        // it on receiving getStartedMessage). The Get Started tap does the same
        // thing early; Complete() is guarded so it only fires once.
        if (autoAdvanceOnEnd)
        {
            yield return new WaitForSeconds(autoAdvanceDelay);
            Complete();
        }
        // Otherwise hold — wait for the Get Started tap (handled by OnGetStarted).
    }

    // ── Phase 1 ─────────────────────────────────────────────────────────
    IEnumerator Phase1_Assembly()
    {
        float t = 0f;
        while (t < _assembleTotal)
        {
            t += Time.deltaTime;
            for (int i = 0; i < _parts.Length; i++)
            {
                float local = t - _startDelay[i];
                if (local <= 0f) { continue; }     // not started: scale 0, exploded out

                if (local < partFlight)
                {
                    // Scale up FAST (full size by ~35%) so the piece becomes
                    // visible and you actually watch it fly in for the rest.
                    float es = SmootherStep(Mathf.Clamp01(local / (partFlight * 0.35f)));
                    _parts[i].localScale = Vector3.LerpUnclamped(Vector3.zero, _finalScale[i], es);

                    // Close in along a gentle arc from the scattered spot home.
                    float ep = SmootherStep(local / partFlight);
                    Vector3 pos = Vector3.LerpUnclamped(_scatterPos[i], _finalPos[i], ep);
                    Vector3 travel = _finalPos[i] - _scatterPos[i];
                    if (arcAmount > 0f && travel.sqrMagnitude > 1e-4f)
                    {
                        Vector3 perp = Vector3.Cross(travel.normalized, Vector3.forward);
                        if (perp.sqrMagnitude < 1e-4f) perp = Vector3.up;
                        float sign = Rand(i, 9) > 0.5f ? 1f : -1f;
                        pos += perp.normalized * (sign * arcAmount * Mathf.Sin(ep * Mathf.PI));
                    }
                    _parts[i].localPosition = pos;
                    _parts[i].localRotation = Quaternion.SlerpUnclamped(_scatterRot[i], _finalRot[i], ep);
                }
                else if (local < partFlight + partSettle && partOvershoot > 0f)
                {
                    float st = (local - partFlight) / partSettle;
                    float wob = partOvershoot * Mathf.Sin(st * Mathf.PI * 2f) * (1f - st);
                    Vector3 dirIn = (_finalPos[i] - _scatterPos[i]);
                    dirIn = dirIn.sqrMagnitude > 0.0001f ? dirIn.normalized : Vector3.up;
                    _parts[i].localScale = _finalScale[i];
                    _parts[i].localPosition = _finalPos[i] + dirIn * wob;
                    _parts[i].localRotation = _finalRot[i];
                }
                else
                {
                    _parts[i].localScale = _finalScale[i];
                    _parts[i].localPosition = _finalPos[i];
                    _parts[i].localRotation = _finalRot[i];
                }
            }
            yield return null;
        }
        for (int i = 0; i < _parts.Length; i++)
        {
            _parts[i].localScale = _finalScale[i];
            _parts[i].localPosition = _finalPos[i];
            _parts[i].localRotation = _finalRot[i];
        }
    }

    // ── Phase 2 — Chest opens → approach → body fade ────────────────────
    // Chest opens revealing the full-scale heart; robot moves toward the camera
    // and stops at world z = approachTargetZ; the body alpha-fades out, leaving
    // only the heart.
    IEnumerator Phase2_ApproachAndFade()
    {
        // Heart at FULL scale (revealed as the chest opens).
        if (heart != null)
        {
            heart.gameObject.SetActive(true);
            heart.localScale = _heartBaseScale;
            SetEmission(heartRenderer, heartColor, heartEmission);
        }
        PrepareBodyFade();

        // 1) Chest opens.
        if (chestLeft != null || chestRight != null)
        {
            float t = 0f, dur = Mathf.Max(0.01f, chestOpenDuration);
            while (t < dur)
            {
                t += Time.deltaTime;
                float e = SmootherStep(Mathf.Clamp01(t / dur));
                if (chestLeft != null)  chestLeft.localRotation  = Quaternion.Slerp(_chestLeftClosed,  _chestLeftOpen,  e);
                if (chestRight != null) chestRight.localRotation = Quaternion.Slerp(_chestRightClosed, _chestRightOpen, e);
                yield return null;
            }
        }

        // 2) Robot moves toward the camera, stopping at world z = approachTargetZ.
        {
            Vector3 from = robotRoot.position;
            Vector3 to = from; to.z = approachTargetZ;
            float t = 0f, dur = Mathf.Max(0.01f, approachDuration);
            while (t < dur)
            {
                t += Time.deltaTime;
                float e = SmootherStep(Mathf.Clamp01(t / dur));
                robotRoot.position = Vector3.LerpUnclamped(from, to, e);
                yield return null;
            }
            robotRoot.position = to;
        }

        // 3) Body fades out (alpha), leaving only the heart.
        {
            float t = 0f, dur = Mathf.Max(0.01f, bodyFadeDuration);
            while (t < dur)
            {
                t += Time.deltaTime;
                float e = SmootherStep(Mathf.Clamp01(t / dur));
                SetBodyAlpha(1f - e);
                yield return null;
            }
            SetBodyAlpha(0f);
            SetPartsVisible(false);     // fully gone — also avoids transparent sorting artifacts
        }
    }

    // ── Phase 3 — Heart spins → becomes W → W moves to center ────────────
    IEnumerator Phase3_HeartToW()
    {
        // Make sure the screen-space morph images stay hidden.
        SetGroup(morphGroup, 0f, false);
        if (morphHeartImage != null) SetImageAlpha(morphHeartImage, 0f);
        if (morphWImage != null) SetImageAlpha(morphWImage, 0f);

        // ── Heart spins, then turns INTO the W at its own spot ───────────
        Vector3 axis = heartSpinAxis.sqrMagnitude > 1e-4f ? heartSpinAxis.normalized : Vector3.up;
        Quaternion hStart = heart != null ? heart.rotation : Quaternion.identity;     // world-space
        Vector3 heartWorld = heart != null ? heart.position
                           : (robotRoot != null ? robotRoot.position : Vector3.zero);
        // The W's "up close" size at the heart's spot. The heart crossfades to a
        // W of this size, then it grows to full as it recedes to its rest center.
        Vector3 wNearScale = _hasWLogo ? _wLogoBaseScale * wLogoSwapStartScale : Vector3.one;

        // Stage the W at the heart's EXACT spot, hidden (scale 0). It grows in
        // during the morph tail so it reads as the heart becoming the W.
        bool wReady = wLogo != null && _hasWLogo;
        if (wReady)
        {
            wLogo.gameObject.SetActive(true);
            wLogo.position = heartWorld;            // appear where the heart is
            wLogo.localScale = Vector3.zero;
            wLogo.localRotation = _wLogoBaseRot;
        }

        // One continuous motion: the heart spins `heartSpins` turns around the
        // WORLD axis (clean regardless of the model's tilted local axes); during
        // the final `heartMorphDuration` the heart shrinks to nothing while the W
        // grows in at the same spot, co-spinning and easing upright — so the
        // heart appears to transform into the W rather than hard-swap.
        {
            float spinDur = Mathf.Max(0.01f, heartSpinDuration);
            float morphDur = Mathf.Clamp(heartMorphDuration, 0f, spinDur);
            float morphStart = spinDur - morphDur;
            float totalAngle = 360f * heartSpins;
            float t = 0f;
            while (t < spinDur)
            {
                t += Time.deltaTime;
                float e = SmootherStep(Mathf.Clamp01(t / spinDur));
                float angle = totalAngle * e;
                if (heart != null) heart.rotation = Quaternion.AngleAxis(angle, axis) * hStart;

                float m = morphDur > 1e-4f ? Mathf.Clamp01((t - morphStart) / morphDur)
                                           : (t >= morphStart ? 1f : 0f);
                float me = SmootherStep(m);
                if (heart != null) heart.localScale = Vector3.LerpUnclamped(_heartBaseScale, Vector3.zero, me);
                if (wReady)
                {
                    wLogo.localScale = Vector3.LerpUnclamped(Vector3.zero, wNearScale, me);
                    // Co-spin the W with the heart, easing to its upright resting
                    // pose as the morph finishes so it reads as one shared spin.
                    wLogo.rotation = Quaternion.Slerp(Quaternion.AngleAxis(angle, axis) * _wLogoBaseRot, _wLogoBaseRot, me);
                }
                yield return null;
            }
        }
        if (heart != null) { heart.localScale = Vector3.zero; heart.gameObject.SetActive(false); }
        if (wReady) { wLogo.localScale = wNearScale; wLogo.localRotation = _wLogoBaseRot; }

        // Slide the W from the heart's spot back to its resting center, growing
        // from its up-close size to full as it recedes.
        if (wReady)
        {
            Vector3 wFrom = wLogo.localPosition;    // root object → local == world
            Vector3 wTo = _wLogoBasePos;            // resting, centered
            float t = 0f;
            float dur = Mathf.Max(0.01f, wLogoMoveDuration);
            while (t < dur)
            {
                t += Time.deltaTime;
                float e = SmootherStep(Mathf.Clamp01(t / dur));
                wLogo.localPosition = Vector3.LerpUnclamped(wFrom, wTo, e);
                wLogo.localScale = Vector3.LerpUnclamped(wNearScale, _wLogoBaseScale, e);
                yield return null;
            }
            wLogo.localPosition = wTo;
            wLogo.localScale = _wLogoBaseScale;
        }

        // 4.5s lock — fire the double-pulse haptic once.
        FireHaptic();
    }

    // ── Body fade helpers ───────────────────────────────────────────────
    // Switch each part's runtime material instance to transparent so the body
    // can alpha-fade. renderer.materials returns play-mode instances, so the
    // source assets are never modified.
    void PrepareBodyFade()
    {
        if (_bodyFadePrepared) return;
        _bodyFadePrepared = true;
        _fadeMats = new List<Material>();
        if (_bodyRenderers == null) return;
        foreach (var r in _bodyRenderers)
        {
            if (r == null) continue;
            foreach (var m in r.materials)
            {
                MakeTransparent(m);
                _fadeMats.Add(m);
            }
        }
    }

    void SetBodyAlpha(float a)
    {
        if (_fadeMats == null) return;
        foreach (var m in _fadeMats)
        {
            if (m == null) continue;
            if (m.HasProperty("_BaseColor")) { var c = m.GetColor("_BaseColor"); c.a = a; m.SetColor("_BaseColor", c); }
            else if (m.HasProperty("_Color")) { var c = m.GetColor("_Color"); c.a = a; m.SetColor("_Color", c); }
        }
    }

    void SetPartsVisible(bool v)
    {
        if (_bodyRenderers == null) return;
        foreach (var r in _bodyRenderers) if (r != null) r.enabled = v;
    }

    // URP/Lit opaque → transparent (alpha blend) so _BaseColor.a controls fade.
    static void MakeTransparent(Material m)
    {
        if (m == null) return;
        m.SetFloat("_Surface", 1f);   // 0=Opaque, 1=Transparent
        m.SetFloat("_Blend", 0f);     // 0=Alpha
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    void FireHaptic()
    {
        if (_haptated) return;
        _haptated = true;
        if (!string.IsNullOrEmpty(hapticMessage)) SendToFlutter.Send(hapticMessage);
    }

    // ── Phase 4 ─────────────────────────────────────────────────────────
    IEnumerator Phase4_InterfaceReveal()
    {
        // Staggered top→bottom reveal: headline + subhead type on, button slides,
        // footer fades.
        StartCoroutine(RevealText(headlineGroup, headlineText, 0f));
        StartCoroutine(RevealText(subheadGroup, subheadText, revealStagger));
        StartCoroutine(RevealButton(revealStagger * 2f));
        StartCoroutine(FadeInGroup(footerGroup, elementFade, revealStagger * 3f));

        float total = Mathf.Max(phase4Duration, revealStagger * 3f + elementFade);
        yield return new WaitForSeconds(total);
    }

    // Reveals a TMP text by writing it on character-by-character (typewriter).
    // Falls back to a plain fade if typewriter is off or no text is wired.
    IEnumerator RevealText(CanvasGroup g, TMP_Text txt, float delay)
    {
        if (!typewriter || txt == null) { yield return FadeInGroup(g, elementFade, delay); yield break; }
        if (delay > 0f) yield return new WaitForSeconds(delay);

        // Make the container fully visible; characters are gated by TMP instead.
        SetGroup(g, 1f, true);
        txt.ForceMeshUpdate();
        int total = txt.textInfo.characterCount;
        txt.maxVisibleCharacters = 0;

        float perChar = 1f / Mathf.Max(1f, typeCharsPerSecond);
        float acc = 0f;
        int shown = 0;
        while (shown < total)
        {
            acc += Time.deltaTime;
            // Catch up multiple characters per frame if the frame was long.
            while (acc >= perChar && shown < total)
            {
                acc -= perChar;
                shown++;
                txt.maxVisibleCharacters = shown;
            }
            yield return null;
        }
        txt.maxVisibleCharacters = total;
    }

    IEnumerator RevealButton(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        Vector2 to = buttonRect != null ? buttonRect.anchoredPosition : Vector2.zero;
        Vector2 from = to - new Vector2(0f, buttonSlideUp);
        float t = 0f;
        while (t < elementFade)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / elementFade);
            float e = 1f - Mathf.Pow(1f - u, 3f);     // ease-out
            if (buttonGroup != null) buttonGroup.alpha = u;
            if (buttonRect != null) buttonRect.anchoredPosition = Vector2.LerpUnclamped(from, to, e);
            yield return null;
        }
        SetGroup(buttonGroup, 1f, true);
        if (buttonRect != null) buttonRect.anchoredPosition = to;
    }

    IEnumerator FadeInGroup(CanvasGroup g, float dur, float delay)
    {
        if (g == null) yield break;
        if (delay > 0f) yield return new WaitForSeconds(delay);
        float t = 0f;
        SetGroup(g, 0f, true);
        while (t < dur)
        {
            t += Time.deltaTime;
            g.alpha = Mathf.Clamp01(t / dur);
            yield return null;
        }
        SetGroup(g, 1f, true);
    }

    void OnGetStarted() { Complete(); }

    // Tells Flutter to load the main scene. Fires once — whichever happens
    // first, the Get Started tap or the auto-advance at the end of the sequence.
    void Complete()
    {
        if (_completed) return;
        _completed = true;
        if (!string.IsNullOrEmpty(getStartedMessage)) SendToFlutter.Send(getStartedMessage);
    }

    // ── Helpers ─────────────────────────────────────────────────────────
    void SetGroup(CanvasGroup g, float alpha, bool interactable)
    {
        if (g == null) return;
        g.alpha = alpha;
        g.interactable = interactable && alpha > 0.99f;
        g.blocksRaycasts = interactable && alpha > 0.5f;
    }

    void SetImageAlpha(Image img, float a)
    {
        if (img == null) return;
        var c = img.color; c.a = a; img.color = c;
    }

    void SetEmission(Renderer r, Color color, float intensity)
    {
        if (r == null) return;
        r.GetPropertyBlock(_mpb);
        _mpb.SetColor("_EmissionColor", color * Mathf.GammaToLinearSpace(intensity));
        _mpb.SetColor("_BaseColor", color);
        r.SetPropertyBlock(_mpb);
    }
}
