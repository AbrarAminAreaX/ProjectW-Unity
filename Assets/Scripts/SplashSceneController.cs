using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[System.Serializable]
public class PartTrajectory
{
    [Tooltip("The part GameObject's transform.")]
    public Transform part;

    [Header("Origin (in robotRoot local space)")]
    [Tooltip("Direction the part flies in FROM. Vector3.up = comes from above. Vector3.left = from the robot's left. Etc.")]
    public Vector3 originDirection = Vector3.up;
    [Tooltip("Distance from final position along originDirection.")]
    public float originDistance = 4f;

    [Header("Arc (curved path)")]
    [Tooltip("How much the part curves perpendicular to its straight-line path. 0 = straight line.")]
    public float arcHeight = 0.3f;
    [Tooltip("Direction of the arc bulge. (Defaults to up if zero.)")]
    public Vector3 arcDirection = Vector3.up;

    [Header("Spin during flight")]
    [Tooltip("Total degrees the part spins as it flies in. 0 = no spin. 360 = one full rotation.")]
    public float spinDegrees = 0f;
    [Tooltip("Axis of the spin (local).")]
    public Vector3 spinAxis = Vector3.up;

    [Header("Timing")]
    [Tooltip("Seconds delay before this part starts flying (relative to assembly start).")]
    public float startDelay = 0f;
    [Tooltip("How long this part takes to arrive. 0 = use SplashSceneController.assemblyDuration.")]
    public float flightDuration = 0f;

    [Header("Snap-in")]
    [Tooltip("Damped overshoot wobble after arrival. 0 = no bounce. 0.15 = subtle. 0.3 = punchy.")]
    public float overshoot = 0.15f;
    [Tooltip("Settle wobble duration in seconds.")]
    public float settleDuration = 0.25f;
}

// Splash scene cinematic for ProjectW.
//
// Phase order:
//   1. Pre-buildup darkness (preBuildupDelay)
//   2. Energy buildup — energyRing particles play, flashLight pulses subtly
//   3. Flash peak — flashLight punches up to flashPeakIntensity
//   4. Assembly — parts fly in along configured trajectories with arc + spin + overshoot;
//      camera dollies forward during this; assemblySparks bursts as each part snaps in
//   5. Post-assembly hold
//   6. Rotate from tilted pose to facing camera
//   7. Awakening — eyes/mouth emission ramps with a flicker; awakeningFlash bursts;
//      camera FOV crash-zooms; rimLight intensifies
//   8. Hold then SendToFlutter.Send("splash_done")
//
// All motion uses coroutines + Time.deltaTime so it scales with framerate.
public class SplashSceneController : MonoBehaviour
{
    [Header("Robot")]
    public Transform robotRoot;
    public List<PartTrajectory> partTrajectories = new List<PartTrajectory>();

    [Header("Sequential assembly")]
    [Tooltip("If true, parts arrive one-by-one in list order — each part waits for the previous one to snap in. Overrides the per-part startDelay.")]
    public bool oneAtATime = true;
    [Tooltip("Flight time per part when oneAtATime is on. Per-part flightDuration overrides this if set > 0.")]
    public float oneAtATimeFlightDuration = 0.32f;
    [Tooltip("Settle wobble after each part snaps in (when oneAtATime is on). Set negative to use per-part settleDuration.")]
    public float oneAtATimeSettleDuration = 0.08f;
    [Tooltip("Brief pause between parts in oneAtATime mode (after settle, before next part starts).")]
    public float oneAtATimeGap = 0.03f;

    [Header("Awakening renderers (eyes, mouth, etc.)")]
    public List<Renderer> powerOnRenderers = new List<Renderer>();
    [Tooltip("Color of the eye/mouth glow at power-on. Yellow matches Rob11's eye light style.")]
    public Color powerOnEmissionColor = new Color(1f, 0.85f, 0.2f);
    [Tooltip("How bright the glow is. Keep low — high HDR values look like overexposed white blobs.")]
    public float powerOnEmissionIntensity = 1.2f;
    public float powerOnDuration = 0.6f;

    [Header("Lights")]
    [Tooltip("Bright light at robot chest. Pulses through the sequence.")]
    public Light flashLight;
    [Tooltip("Rim light behind robot. Intensifies during awakening.")]
    public Light rimLight;

    [Header("Particles")]
    [Tooltip("Plays during the buildup phase. Stop() called when assembly begins.")]
    public ParticleSystem energyRing;
    [Tooltip("Single radial burst at the awakening beat.")]
    public ParticleSystem awakeningFlash;

    [Header("Camera")]
    public Camera targetCamera;
    [Tooltip("How far the camera dollies forward during assembly (in world units along its forward direction).")]
    public float cameraDollyZ = 1.5f;
    public float cameraStartFov = 45f;
    [Tooltip("FOV at the awakening 'crash zoom' moment.")]
    public float cameraAwakeningFov = 35f;
    [Tooltip("How much the camera rises during the awakening phase. Combined with LookAt(robot), this creates a slight high-angle 'looking down' final shot.")]
    public float cameraEndRise = 1.2f;
    [Tooltip("Offset from robotRoot.position used as the LookAt target during the camera end-move. Adjust if the robot's pivot isn't at its visual center (e.g., pivot at feet → use ~Vector3.up * height/2).")]
    public Vector3 cameraLookOffset = new Vector3(0f, 0.5f, 0f);

    [Header("Phase timing (seconds) — total sequence ~4s")]
    public float preBuildupDelay = 0.1f;
    public float buildupDuration = 0.25f;
    public float flashDuration = 0.25f;
    public float assemblyDuration = 0.32f;
    public float postAssemblyHold = 0.1f;
    public float rotateToCameraDuration = 0.4f;
    public float postAwakeningHold = 0.1f;

    [Header("Tilt during assembly")]
    public Vector3 tiltedEuler = new Vector3(-15f, 30f, 15f);

    [Header("Smooth entrance (alternative to part-by-part assembly)")]
    [Tooltip("Skip the assembly + rotate phases. Robot starts at final pose offset by smoothEntranceOffset, then lerps to its final position. Used by the Plant splash variant.")]
    public bool useSmoothEntrance = false;
    [Tooltip("Local-space offset added to the robot's final position at scene start. Robot lerps FROM this offset TO its final position during the entrance.")]
    public Vector3 smoothEntranceOffset = new Vector3(0f, -2f, 0f);
    [Tooltip("How long the smooth-entrance rise takes.")]
    public float smoothEntranceDuration = 1.5f;

    [Header("Bloom entrance (scale up from a flower/light point)")]
    [Tooltip("Skip assembly + rotate. Robot starts at scale 0 at the bloom origin and grows to bloomEntranceEndScale, like emerging from a flower. Mutually exclusive with useSmoothEntrance.")]
    public bool useBloomEntrance = false;
    [Tooltip("Local-space offset from the robot's natural final position to the bloom origin (where it grows FROM). Typically negative Y to spawn at the flower base.")]
    public Vector3 bloomEntranceOriginOffset = new Vector3(0f, -0.6f, 0f);
    [Tooltip("Uniform localScale at the start of the bloom (typically 0 = invisible).")]
    public float bloomEntranceStartScale = 0f;
    [Tooltip("Uniform localScale when the bloom finishes. 1 = prefab default size, 1.5 = 50% larger.")]
    public float bloomEntranceEndScale = 1.5f;
    [Tooltip("How long the bloom (scale + rise) takes.")]
    public float bloomEntranceDuration = 1.5f;

    [Header("Final orientation")]
    public bool faceCamera = true;
    public Vector3 finalEulerOverride = Vector3.zero;

    [Header("Flash light")]
    public float flashPeakIntensity = 12f;
    public AnimationCurve flashCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 8f),
        new Keyframe(0.2f, 1f, 0f, 0f),
        new Keyframe(1f, 0.05f, -1f, 0f)
    );

    [Header("Easing")]
    [Tooltip("Motion curve per part. Eases out toward the end (parts accelerate slightly into the final position). Linear-ish so the part is visible moving the whole flight, not hanging then teleporting.")]
    public AnimationCurve assemblyEase = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0.6f),
        new Keyframe(0.5f, 0.4f, 1.1f, 1.1f),
        new Keyframe(1f, 1f, 1.6f, 0f)
    );
    public AnimationCurve rotateEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Greeting after awakening (uses robot Animator + EmotionChanger)")]
    [Tooltip("Animator on the robot. Auto-found from robotRoot if null. BuildSplashFX duplicates Rob11.controller to a Splash-only copy so tweaks don't leak back to Rob11Scene.")]
    public Animator robotAnimator;
    [Tooltip("Animator Bool parameter that drives the wave. Rob11's parameter is 'Hello'. The same SetBool() pattern as GeminiAudioBridge.")]
    public string waveBoolName = "Hello";
    [Tooltip("EmotionChanger on the robot — controls the eye/mouth texture offsets to swap face expressions. Auto-found from robotRoot if null.")]
    public EmotionChanger emotionChanger;
    [Tooltip("Emotion index for the smile. Rob11 emotion list: 0=Neutral, 1=Happy, 2=Sad, 3=Distrust, 4=Wonder, 5=Death, 6=Disgust, 7=Evil, 8=Cry, 9=Love.")]
    public int happyEmotionIndex = 1;
    [Tooltip("How long to hold the wave bool before resetting and notifying Flutter.")]
    public float greetingDuration = 0.5f;

    public enum GreetingMode { None, WaveOnly, KnockOnly, KnockThenWave }
    [Tooltip("Post-awakening greeting. WaveOnly = original behavior. KnockOnly = code-driven knock toward the camera. KnockThenWave = knock first, then wave.")]
    public GreetingMode greetingMode = GreetingMode.WaveOnly;

    [Header("Knock on screen — motion (greetingMode uses Knock)")]
    [Tooltip("Hand/forearm transform that punches forward toward the camera. Assign the right hand bone (e.g. Rob11's RightHand).")]
    public Transform knockHand;
    [Tooltip("Number of knock taps in sequence.")]
    public int knockCount = 3;
    [Tooltip("World-space distance the hand moves toward the camera per tap.")]
    public float knockReach = 0.30f;
    [Tooltip("How far the whole robot leans toward the camera per knock (world units). Adds body engagement so it doesn't look like just a floating hand. 0 = no lean.")]
    public float knockBodyLean = 0.06f;
    [Tooltip("Duration of one tap (strike + return), in seconds.")]
    public float knockTapDuration = 0.22f;
    [Tooltip("Frozen 'impact' hold at the strike position before the return — sells the contact moment.")]
    public float knockStrikeHold = 0.04f;
    [Tooltip("Pause between consecutive taps.")]
    public float knockBetweenTaps = 0.08f;
    [Tooltip("Delay between the previous beat finishing and the first knock.")]
    public float preKnockDelay = 0.2f;
    [Tooltip("Optional wrist/forearm rotation at the strike, in degrees around knockRotationAxis (local space). 0 = pure translation.")]
    public float knockRotationAngle = 0f;
    [Tooltip("Local rotation axis on the hand transform during the strike.")]
    public Vector3 knockRotationAxis = Vector3.right;

    [Header("Knock on screen — feedback")]
    [Tooltip("Knock SFX, played once per tap. Null = silent.")]
    public AudioClip knockSound;
    [Tooltip("Optional AudioSource override. If null, one is auto-added to this GameObject on first play (2D, no spatial blend).")]
    public AudioSource knockAudioSource;
    [Range(0f, 1f)] public float knockSoundVolume = 0.8f;
    [Tooltip("World-units of camera offset at peak shake. 0 = no shake.")]
    public float cameraShakeAmount = 0.04f;
    [Tooltip("How long each impact shake decays over.")]
    public float cameraShakeDuration = 0.15f;
    [Tooltip("Sin-wave frequency of the shake rattle. Higher = faster jitter.")]
    public int cameraShakeFreq = 22;

    [Header("Behavior")]
    public bool autoPlayOnStart = true;
    public bool notifyFlutterOnComplete = true;
    [Tooltip("After splash_done, auto-load this scene so Unity returns to a known state for downstream Flutter screens (chat home etc.). Empty = skip.")]
    public string sceneToLoadAfterSplash = "Bootstrap";

    [Header("Fade to black before scene transition")]
    [Tooltip("Full-screen black UI Image faded in before SceneManager.LoadScene fires. Hides the visible flicker as Unity unloads Splash and loads Bootstrap.")]
    public Image fadeToBlackImage;
    public float fadeToBlackDuration = 0.4f;

    // ── Internal state ──────────────────────────────────────────────────
    private Vector3[] _finalLocalPositions;
    private Quaternion[] _finalLocalRotations;
    private Vector3[] _startLocalPositions;
    private MaterialPropertyBlock _mpb;
    private float _baselineFlashIntensity;
    private float _baselineRimIntensity;
    private Vector3 _cameraStartWorldPos;
    private Vector3 _cameraDollyDirection;
    private Vector3 _smoothEntranceTargetLocalPos;
    private Vector3 _bloomEntranceTargetLocalPos;

    void Awake()
    {
        if (robotRoot == null) robotRoot = transform;
        if (targetCamera == null) targetCamera = Camera.main;
        if (robotAnimator == null && robotRoot != null)
        {
            robotAnimator = robotRoot.GetComponentInChildren<Animator>();
        }
        if (emotionChanger == null && robotRoot != null)
        {
            emotionChanger = robotRoot.GetComponentInChildren<EmotionChanger>();
        }
        _mpb = new MaterialPropertyBlock();
        CacheTrajectories();
        EnableEmissionKeywords();
    }

    void Start()
    {
        ApplyInitialPose();
        if (autoPlayOnStart) StartCoroutine(PlaySplashSequence());
    }

    /// <summary>Public entry point if autoPlayOnStart = false.</summary>
    public void PlaySplash()
    {
        ApplyInitialPose();
        StartCoroutine(PlaySplashSequence());
    }

    void CacheTrajectories()
    {
        int count = partTrajectories.Count;
        _finalLocalPositions = new Vector3[count];
        _finalLocalRotations = new Quaternion[count];
        _startLocalPositions = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            var pt = partTrajectories[i];
            if (pt == null || pt.part == null) continue;
            _finalLocalPositions[i] = pt.part.localPosition;
            _finalLocalRotations[i] = pt.part.localRotation;
            Vector3 dir = pt.originDirection.sqrMagnitude > 0.0001f
                ? pt.originDirection.normalized
                : Vector3.up;
            _startLocalPositions[i] = _finalLocalPositions[i] + dir * pt.originDistance;
        }
    }

    void EnableEmissionKeywords()
    {
        for (int i = 0; i < powerOnRenderers.Count; i++)
        {
            var r = powerOnRenderers[i];
            if (r == null) continue;
            foreach (var mat in r.materials) // .materials = per-renderer instance — safe to mutate
            {
                if (mat == null) continue;
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
            }
        }
    }

    void ApplyInitialPose()
    {
        if (useBloomEntrance)
        {
            // Robot starts already-assembled, facing the camera, at the
            // bloom origin (typically the flower-base point) with scale 0.
            // BloomEntranceRoutine grows it to bloomEntranceEndScale while
            // lifting it to the natural final position.
            robotRoot.localRotation = ComputeFinalRotation();
            for (int i = 0; i < partTrajectories.Count; i++)
            {
                var pt = partTrajectories[i];
                if (pt == null || pt.part == null) continue;
                pt.part.localPosition = _finalLocalPositions[i];
                pt.part.localRotation = _finalLocalRotations[i];
            }
            _bloomEntranceTargetLocalPos = robotRoot.localPosition;
            robotRoot.localPosition = _bloomEntranceTargetLocalPos + bloomEntranceOriginOffset;
            robotRoot.localScale = Vector3.one * bloomEntranceStartScale;
        }
        else if (useSmoothEntrance)
        {
            // No tilt + no part scattering. Robot starts already-assembled,
            // facing the camera, but offset (typically down) — the entrance
            // routine slides it into the final position.
            robotRoot.localRotation = ComputeFinalRotation();
            for (int i = 0; i < partTrajectories.Count; i++)
            {
                var pt = partTrajectories[i];
                if (pt == null || pt.part == null) continue;
                pt.part.localPosition = _finalLocalPositions[i];
                pt.part.localRotation = _finalLocalRotations[i];
            }
            _smoothEntranceTargetLocalPos = robotRoot.localPosition;
            robotRoot.localPosition = _smoothEntranceTargetLocalPos + smoothEntranceOffset;
        }
        else
        {
            robotRoot.localRotation = Quaternion.Euler(tiltedEuler);
            for (int i = 0; i < partTrajectories.Count; i++)
            {
                var pt = partTrajectories[i];
                if (pt == null || pt.part == null) continue;
                pt.part.localPosition = _startLocalPositions[i];
                pt.part.localRotation = _finalLocalRotations[i];
                if (pt.spinDegrees != 0f)
                {
                    pt.part.localRotation = _finalLocalRotations[i] *
                        Quaternion.AngleAxis(pt.spinDegrees, pt.spinAxis);
                }
            }
        }
        if (flashLight != null)
        {
            _baselineFlashIntensity = flashLight.intensity;
            flashLight.intensity = 0f;
        }
        if (rimLight != null)
        {
            _baselineRimIntensity = rimLight.intensity;
            rimLight.intensity = 0f;
        }
        if (targetCamera != null)
        {
            _cameraStartWorldPos = targetCamera.transform.position;
            _cameraDollyDirection = targetCamera.transform.forward;
            targetCamera.fieldOfView = cameraStartFov;
        }
        SetEmission(Color.black, 0f);
        // Hide eye/mouth renderers until the awakening beat. Setting
        // emission to black isn't enough — the base color would still
        // render. We disable the Renderer entirely.
        SetPowerOnRenderersEnabled(false);
        // Disable the Animator during assembly so it doesn't fight our
        // procedural part positioning. Re-enabled at the greeting phase.
        if (robotAnimator != null) robotAnimator.enabled = false;
    }

    IEnumerator PlaySplashSequence()
    {
        yield return new WaitForSeconds(preBuildupDelay);

        if (useBloomEntrance)
        {
            yield return BloomEntranceRoutine();
        }
        else if (useSmoothEntrance)
        {
            // Replace the part-by-part assembly + 180° rotate with a single
            // smooth rise. Robot is already facing the camera (set in
            // ApplyInitialPose) and just slides into final position.
            yield return SmoothEntranceRoutine();
        }
        else
        {
            // Phase 1: Energy buildup
            if (energyRing != null) energyRing.Play();
            yield return BuildupRoutine();

            // Phase 2: Flash peak (runs in parallel with assembly start)
            StartCoroutine(FlashRoutine());

            // Phase 3: Assembly + camera dolly
            if (energyRing != null)
            {
                var em = energyRing.emission;
                em.enabled = false;
            }
            StartCoroutine(CameraDollyRoutine(GetTotalAssemblyTime()));
            yield return AssemblyRoutine();

            if (energyRing != null) energyRing.Stop();

            yield return new WaitForSeconds(postAssemblyHold);

            // Phase 4: Rotate from tilted pose to facing camera
            Quaternion from = robotRoot.localRotation;
            Quaternion to = ComputeFinalRotation();
            float t = 0f;
            while (t < rotateToCameraDuration)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / rotateToCameraDuration);
                robotRoot.localRotation = Quaternion.SlerpUnclamped(from, to, rotateEase.Evaluate(u));
                yield return null;
            }
            robotRoot.localRotation = to;
        }

        // Phase 5: Awakening — eyes/mouth glow, FOV crash, rim light up
        if (awakeningFlash != null) awakeningFlash.Play();
        StartCoroutine(CameraFovCrashRoutine());
        StartCoroutine(RimLightUpRoutine());
        yield return PowerOnRoutine();

        // Phase 6: Greeting beat — knock and/or wave per greetingMode.
        // KnockOnScreenRoutine runs with the Animator suspended; WaveAndSmile
        // re-enables it. So knock must come first when both run.
        if (greetingMode == GreetingMode.KnockOnly || greetingMode == GreetingMode.KnockThenWave)
        {
            yield return KnockOnScreenRoutine();
        }
        if (greetingMode == GreetingMode.WaveOnly || greetingMode == GreetingMode.KnockThenWave)
        {
            yield return WaveAndSmileRoutine();
        }

        // Phase 7: Hold
        yield return new WaitForSeconds(postAwakeningHold);

        // Phase 8: Fade to black BEFORE notifying Flutter or loading
        // Bootstrap. The black overlay hides the visible flicker as
        // Unity unloads Splash and loads Bootstrap.
        yield return FadeToBlackRoutine();

        // Phase 9: Notify Flutter (under the black fade)
        if (notifyFlutterOnComplete) SendToFlutter.Send("splash_done");

        // Phase 10: Return Unity to a known scene (Bootstrap) so the next
        // Flutter screen's EmbedUnity gets a fresh scene_loaded event from
        // BootstrapManager.Start(). Without this, Unity stays on Splash
        // and downstream screens hang waiting for a handshake.
        if (!string.IsNullOrEmpty(sceneToLoadAfterSplash))
        {
            yield return null;
            SceneManager.LoadScene(sceneToLoadAfterSplash, LoadSceneMode.Single);
        }
    }

    IEnumerator FadeToBlackRoutine()
    {
        if (fadeToBlackImage == null) yield break;
        Color c = fadeToBlackImage.color;
        c.a = 0f;
        fadeToBlackImage.color = c;
        float t = 0f;
        while (t < fadeToBlackDuration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / fadeToBlackDuration);
            c.a = u;
            fadeToBlackImage.color = c;
            yield return null;
        }
        c.a = 1f;
        fadeToBlackImage.color = c;
    }

    IEnumerator BuildupRoutine()
    {
        float t = 0f;
        while (t < buildupDuration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / buildupDuration);
            // Subtle pulse that grows in amplitude as buildup progresses
            if (flashLight != null)
            {
                float pulse = 0.3f + 0.2f * Mathf.Sin(t * 12f) + 0.5f * u;
                flashLight.intensity = flashPeakIntensity * 0.15f * pulse;
            }
            yield return null;
        }
    }

    IEnumerator FlashRoutine()
    {
        if (flashLight == null) yield break;
        float t = 0f;
        while (t < flashDuration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / flashDuration);
            flashLight.intensity = Mathf.Lerp(0f, flashPeakIntensity, flashCurve.Evaluate(u));
            yield return null;
        }
        flashLight.intensity = _baselineFlashIntensity;
    }

    float GetEffectiveSettleDuration(int i)
    {
        if (oneAtATime && oneAtATimeSettleDuration >= 0f) return oneAtATimeSettleDuration;
        return partTrajectories[i].settleDuration;
    }

    float GetEffectiveStartDelay(int i)
    {
        if (!oneAtATime) return partTrajectories[i].startDelay;
        // Sum of (flightDuration + settleDuration + gap) for all earlier parts
        float cursor = 0f;
        for (int k = 0; k < i; k++)
        {
            var prev = partTrajectories[k];
            if (prev == null) continue;
            float prevDur = prev.flightDuration > 0f ? prev.flightDuration : oneAtATimeFlightDuration;
            cursor += prevDur + GetEffectiveSettleDuration(k) + oneAtATimeGap;
        }
        return cursor;
    }

    float GetEffectiveFlightDuration(int i)
    {
        var pt = partTrajectories[i];
        if (pt.flightDuration > 0f) return pt.flightDuration;
        return oneAtATime ? oneAtATimeFlightDuration : assemblyDuration;
    }

    float GetTotalAssemblyTime()
    {
        float max = 0f;
        for (int i = 0; i < partTrajectories.Count; i++)
        {
            var pt = partTrajectories[i];
            if (pt == null) continue;
            float end = GetEffectiveStartDelay(i) + GetEffectiveFlightDuration(i) + GetEffectiveSettleDuration(i);
            if (end > max) max = end;
        }
        return max;
    }

    IEnumerator AssemblyRoutine()
    {
        float total = GetTotalAssemblyTime();
        float startTime = Time.time;
        while (Time.time - startTime < total)
        {
            float now = Time.time - startTime;
            for (int i = 0; i < partTrajectories.Count; i++)
            {
                var pt = partTrajectories[i];
                if (pt == null || pt.part == null) continue;
                float dur = GetEffectiveFlightDuration(i);
                float local = now - GetEffectiveStartDelay(i);
                if (local < 0) continue;

                if (local < dur)
                {
                    // In flight — thrust-curve interpolation along an arc
                    float u = Mathf.Clamp01(local / dur);
                    float eased = assemblyEase.Evaluate(u);
                    pt.part.localPosition = ComputeArcPosition(_startLocalPositions[i], _finalLocalPositions[i], pt, eased);
                    if (pt.spinDegrees != 0f)
                    {
                        Quaternion spinOffset = Quaternion.AngleAxis((1f - u) * pt.spinDegrees, pt.spinAxis);
                        pt.part.localRotation = _finalLocalRotations[i] * spinOffset;
                    }
                }
                else if (local < dur + GetEffectiveSettleDuration(i) && pt.overshoot > 0f)
                {
                    // Damped settle wobble
                    float settleDur = GetEffectiveSettleDuration(i);
                    float settleT = (local - dur) / settleDur;
                    float wobble = pt.overshoot * Mathf.Sin(settleT * Mathf.PI * 2f) * (1f - settleT);
                    Vector3 dirIn = (_finalLocalPositions[i] - _startLocalPositions[i]).normalized;
                    pt.part.localPosition = _finalLocalPositions[i] + dirIn * wobble;
                    pt.part.localRotation = _finalLocalRotations[i];
                }
                else
                {
                    pt.part.localPosition = _finalLocalPositions[i];
                    pt.part.localRotation = _finalLocalRotations[i];
                }
            }
            yield return null;
        }
        // Snap final
        for (int i = 0; i < partTrajectories.Count; i++)
        {
            var pt = partTrajectories[i];
            if (pt == null || pt.part == null) continue;
            pt.part.localPosition = _finalLocalPositions[i];
            pt.part.localRotation = _finalLocalRotations[i];
        }
    }


    Vector3 ComputeArcPosition(Vector3 start, Vector3 end, PartTrajectory pt, float u)
    {
        Vector3 lin = Vector3.LerpUnclamped(start, end, u);
        // sin(πu) peaks at 0.5, returns to 0 at endpoints — natural arc bulge
        float arcU = Mathf.Sin(u * Mathf.PI);
        Vector3 arc = pt.arcDirection.sqrMagnitude > 0.0001f
            ? pt.arcDirection.normalized
            : Vector3.up;
        return lin + arc * pt.arcHeight * arcU;
    }


    // Smooth lerp of the whole robotRoot from (final + offset) → final.
    // Used by the Plant splash variant in place of the part-by-part
    // assembly: cleaner, more "wholesome" entrance for the bioluminescent
    // plant background.
    IEnumerator SmoothEntranceRoutine()
    {
        Vector3 startLocal = robotRoot.localPosition;
        Vector3 endLocal = _smoothEntranceTargetLocalPos;
        float t = 0f;
        float duration = Mathf.Max(0.01f, smoothEntranceDuration);
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            // Smoothstep ease — gentle in, soft landing.
            float eased = u * u * (3f - 2f * u);
            robotRoot.localPosition = Vector3.LerpUnclamped(startLocal, endLocal, eased);
            yield return null;
        }
        robotRoot.localPosition = endLocal;
    }

    // Scale-up bloom from the flower-base origin → natural final position.
    // Used by the Plant splash variant: robot emerges from the glowing
    // cavity in the Veo background, growing from scale 0 to
    // bloomEntranceEndScale while rising into frame.
    IEnumerator BloomEntranceRoutine()
    {
        Vector3 startLocal = robotRoot.localPosition;
        Vector3 endLocal = _bloomEntranceTargetLocalPos;
        float startScale = bloomEntranceStartScale;
        float endScale = bloomEntranceEndScale;
        float t = 0f;
        float duration = Mathf.Max(0.01f, bloomEntranceDuration);
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            // Ease-out cubic — fast burst of growth, soft settle. Feels
            // like a flower opening into its final form.
            float eased = 1f - Mathf.Pow(1f - u, 3f);
            robotRoot.localPosition = Vector3.LerpUnclamped(startLocal, endLocal, eased);
            float s = Mathf.LerpUnclamped(startScale, endScale, eased);
            robotRoot.localScale = new Vector3(s, s, s);
            yield return null;
        }
        robotRoot.localPosition = endLocal;
        robotRoot.localScale = new Vector3(endScale, endScale, endScale);
    }

    IEnumerator CameraDollyRoutine(float duration)
    {
        if (targetCamera == null) yield break;
        Vector3 startPos = _cameraStartWorldPos;
        Vector3 endPos = startPos + _cameraDollyDirection * cameraDollyZ;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            targetCamera.transform.position = Vector3.LerpUnclamped(startPos, endPos, assemblyEase.Evaluate(u));
            yield return null;
        }
        targetCamera.transform.position = endPos;
    }

    IEnumerator CameraFovCrashRoutine()
    {
        if (targetCamera == null) yield break;
        float startFov = targetCamera.fieldOfView;
        Vector3 startPos = targetCamera.transform.position;
        Vector3 endPos = startPos + Vector3.up * cameraEndRise;
        Vector3 lookAt = robotRoot != null ? (robotRoot.position + cameraLookOffset) : Vector3.zero;
        float duration = powerOnDuration * 0.9f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            // Quadratic ease-in for the "snap zoom" feel
            float eased = u * u;
            targetCamera.fieldOfView = Mathf.Lerp(startFov, cameraAwakeningFov, eased);
            targetCamera.transform.position = Vector3.Lerp(startPos, endPos, eased);
            // Re-aim at the robot every frame so the camera tracks down
            // smoothly as it rises (creates the "looking down" angle).
            if (robotRoot != null) targetCamera.transform.LookAt(lookAt);
            yield return null;
        }
        targetCamera.fieldOfView = cameraAwakeningFov;
        targetCamera.transform.position = endPos;
        if (robotRoot != null) targetCamera.transform.LookAt(lookAt);
    }

    IEnumerator RimLightUpRoutine()
    {
        if (rimLight == null) yield break;
        float startIntensity = rimLight.intensity;
        float endIntensity = _baselineRimIntensity > 0f ? _baselineRimIntensity * 2f : 8f;
        float duration = powerOnDuration;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            rimLight.intensity = Mathf.Lerp(startIntensity, endIntensity, u);
            yield return null;
        }
        rimLight.intensity = endIntensity;
    }

    IEnumerator PowerOnRoutine()
    {
        // Reveal eye/mouth meshes the moment power-on starts.
        SetPowerOnRenderersEnabled(true);
        float t = 0f;
        while (t < powerOnDuration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / powerOnDuration);
            // Flicker for the first 40% — mimics a system booting up
            float flicker = u < 0.4f ? Mathf.Abs(Mathf.Sin(t * 30f)) : 1f;
            SetEmission(powerOnEmissionColor, powerOnEmissionIntensity * u * flicker);
            yield return null;
        }
        SetEmission(powerOnEmissionColor, powerOnEmissionIntensity);
    }

    void SetPowerOnRenderersEnabled(bool enabled)
    {
        for (int i = 0; i < powerOnRenderers.Count; i++)
        {
            var r = powerOnRenderers[i];
            if (r == null) continue;
            r.enabled = enabled;
        }
    }

    // Greeting after awakening — re-enable the Animator, swap the face
    // textures to the happy expression via EmotionChanger, and drive the
    // wave Bool the same way GeminiAudioBridge does.
    IEnumerator WaveAndSmileRoutine()
    {
        // Smiling face — slides the eye/mouth texture offset to the
        // Happy index (1). Mirrors what Rob11Scene does via
        // GeminiAudioBridge → robotController.setEmotion(1).
        if (emotionChanger != null)
        {
            emotionChanger.SetEmotionEyes(happyEmotionIndex);
            emotionChanger.SetEmotionMouth(happyEmotionIndex);
        }

        if (robotAnimator == null) yield break;
        robotAnimator.enabled = true;
        // Let the Animator settle into its default state for one frame
        // before applying parameters.
        yield return null;
        if (!string.IsNullOrEmpty(waveBoolName))
        {
            robotAnimator.SetBool(waveBoolName, true);
        }
        yield return new WaitForSeconds(greetingDuration);
        // Reset bool so the Animator can return to idle (matches the
        // pattern in GeminiAudioBridge where bools are toggled off after
        // the action completes).
        if (!string.IsNullOrEmpty(waveBoolName))
        {
            robotAnimator.SetBool(waveBoolName, false);
        }
    }

    // Code-driven knock: hand punches forward toward the camera N times,
    // body leans in slightly with the hand, camera shakes on impact, and
    // knockSound (if assigned) fires per tap. Animator is suspended so the
    // procedural changes aren't overwritten by the active state's bones.
    // Everything returns to rest between taps and at the end.
    IEnumerator KnockOnScreenRoutine()
    {
        if (knockHand == null || targetCamera == null || knockCount <= 0) yield break;

        if (preKnockDelay > 0f) yield return new WaitForSeconds(preKnockDelay);

        bool animatorWasEnabled = robotAnimator != null && robotAnimator.enabled;
        if (robotAnimator != null) robotAnimator.enabled = false;

        Vector3 handRest = knockHand.position;
        Quaternion handRestRot = knockHand.localRotation;
        Vector3 bodyRest = robotRoot.position;
        // Single camera anchor for all shakes — avoids drift if a shake
        // overlaps with the next strike when durations are tuned aggressively.
        Vector3 cameraAnchor = targetCamera.transform.position;
        float half = Mathf.Max(0.01f, knockTapDuration * 0.5f);

        for (int k = 0; k < knockCount; k++)
        {
            Vector3 toCam = (targetCamera.transform.position - handRest).normalized;
            if (toCam.sqrMagnitude < 0.0001f) toCam = -robotRoot.forward;
            Vector3 handStrike = handRest + toCam * knockReach;
            Vector3 bodyStrike = bodyRest + toCam * knockBodyLean;
            Quaternion handStrikeRot = handRestRot * Quaternion.AngleAxis(knockRotationAngle, knockRotationAxis);

            // Strike — ease-out (fast snap forward)
            float t = 0f;
            while (t < half)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / half);
                float eased = 1f - Mathf.Pow(1f - u, 2.5f);
                knockHand.position = Vector3.LerpUnclamped(handRest, handStrike, eased);
                if (knockBodyLean != 0f) robotRoot.position = Vector3.LerpUnclamped(bodyRest, bodyStrike, eased);
                if (knockRotationAngle != 0f) knockHand.localRotation = Quaternion.SlerpUnclamped(handRestRot, handStrikeRot, eased);
                yield return null;
            }

            // Impact frame — sound + shake + brief hold
            if (knockSound != null)
            {
                var src = GetOrCreateKnockAudioSource();
                if (src != null) src.PlayOneShot(knockSound, knockSoundVolume);
            }
            if (cameraShakeAmount > 0f && cameraShakeDuration > 0f)
            {
                StartCoroutine(CameraShakeRoutine(cameraAnchor));
            }
            if (knockStrikeHold > 0f) yield return new WaitForSeconds(knockStrikeHold);

            // Return — ease-in
            t = 0f;
            while (t < half)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / half);
                float eased = u * u;
                knockHand.position = Vector3.LerpUnclamped(handStrike, handRest, eased);
                if (knockBodyLean != 0f) robotRoot.position = Vector3.LerpUnclamped(bodyStrike, bodyRest, eased);
                if (knockRotationAngle != 0f) knockHand.localRotation = Quaternion.SlerpUnclamped(handStrikeRot, handRestRot, eased);
                yield return null;
            }
            knockHand.position = handRest;
            if (knockBodyLean != 0f) robotRoot.position = bodyRest;
            if (knockRotationAngle != 0f) knockHand.localRotation = handRestRot;

            if (k < knockCount - 1 && knockBetweenTaps > 0f)
            {
                yield return new WaitForSeconds(knockBetweenTaps);
            }
        }

        // Make sure the camera is exactly back at the anchor when we leave
        // (in case a shake was still in flight when the routine ended).
        targetCamera.transform.position = cameraAnchor;

        if (animatorWasEnabled && robotAnimator != null) robotAnimator.enabled = true;
    }

    AudioSource GetOrCreateKnockAudioSource()
    {
        if (knockAudioSource != null) return knockAudioSource;
        knockAudioSource = gameObject.AddComponent<AudioSource>();
        knockAudioSource.playOnAwake = false;
        knockAudioSource.spatialBlend = 0f; // 2D — UI/cinematic SFX
        return knockAudioSource;
    }

    // Per-impact camera shake. Uses a passed-in anchor so multiple shakes
    // can run back-to-back without their offsets accumulating into drift.
    IEnumerator CameraShakeRoutine(Vector3 anchor)
    {
        if (targetCamera == null) yield break;
        float t = 0f;
        while (t < cameraShakeDuration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / cameraShakeDuration);
            float decay = 1f - u;
            float phase = t * cameraShakeFreq;
            float ox = Mathf.Sin(phase) * decay;
            float oy = Mathf.Cos(phase * 1.3f + 0.7f) * decay;
            Vector3 offset = (targetCamera.transform.right * ox + targetCamera.transform.up * oy) * cameraShakeAmount;
            targetCamera.transform.position = anchor + offset;
            yield return null;
        }
        targetCamera.transform.position = anchor;
    }

    Quaternion ComputeFinalRotation()
    {
        if (faceCamera && targetCamera != null)
        {
            Vector3 toCam = targetCamera.transform.position - robotRoot.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude < 0.0001f) return Quaternion.identity;
            return Quaternion.LookRotation(toCam.normalized, Vector3.up);
        }
        return Quaternion.Euler(finalEulerOverride);
    }

    void SetEmission(Color baseColor, float intensity)
    {
        Color emission = baseColor * Mathf.GammaToLinearSpace(intensity);
        for (int i = 0; i < powerOnRenderers.Count; i++)
        {
            var r = powerOnRenderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_EmissionColor", emission);
            r.SetPropertyBlock(_mpb);
        }
    }
}
