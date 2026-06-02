using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Tools → ProjectW → 5. Setup Cinematic Sci-Fi Lighting + Post FX
//
// Configures the open scene for a "teal-neon tunnel" cinematic look:
//   * Cool teal key light + warm orange back-rim (the classic sci-fi
//     orange/teal complementary palette)
//   * Near-black ambient (so the bright video tunnel can dominate)
//   * No fog (the tunnel video supplies the depth)
//   * URP Volume with strong Bloom, deep Vignette, ACES tonemapping,
//     orange-shadows / teal-highlights color grading, heavier chromatic
//     aberration + film grain (sci-fi film feel)
//   * Camera post-processing + TAA enabled (better for motion blur)
//
// Idempotent: re-running it updates existing lights / volume / profile
// instead of duplicating them. Disables any leftover forest lights from
// the earlier Setup Forest menu to avoid double-bright.
public static class SetupCinematicSplashEnvironment
{
    private const string ProfileFolder = "Assets/PostProcessing/Splash";
    private const string ProfilePath = ProfileFolder + "/CinematicSplashVolume.asset";

    [MenuItem("Tools/ProjectW/5. Setup Cinematic Sci-Fi Lighting + Post FX")]
    public static void Setup()
    {
        DisableConflictingLights();
        SetupLights();
        SetupRenderSettings();
        var profile = LoadOrCreateProfile();
        SetupVolume(profile);
        SetupCameraPostProcessing();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[SetupCinematicSplashEnvironment] Cinematic sci-fi lighting + post-processing applied.");
        EditorUtility.DisplayDialog(
            "Cinematic Splash",
            "Lighting + post-processing applied for sci-fi tunnel cinematic look.\n\n" +
            "Profile saved to:\n" + ProfilePath + "\n\n" +
            "Press Play. Bloom will pick up the neon tunnel video and the robot's eye glow. " +
            "Color grading pushes shadows toward teal and highlights toward warm orange — " +
            "the standard 'sci-fi blockbuster' palette.",
            "OK"
        );
    }

    // ── Disable forest lights if they exist (avoid double-bright) ─────

    private static void DisableConflictingLights()
    {
        foreach (var name in new[] { "ForestSun", "ForestFill", "Directional Light" })
        {
            var go = GameObject.Find(name);
            if (go != null) go.SetActive(false);
        }
    }

    // ── Lights ────────────────────────────────────────────────────────

    private static void SetupLights()
    {
        // Cool teal key light — picks up edges of the robot in the same
        // hue as the tunnel, so robot reads as part of the environment
        var keyGo = GameObject.Find("CinematicKey");
        if (keyGo == null) keyGo = new GameObject("CinematicKey", typeof(Light));
        else if (keyGo.GetComponent<Light>() == null) keyGo.AddComponent<Light>();
        keyGo.transform.rotation = Quaternion.Euler(35f, 25f, 0f);
        var key = keyGo.GetComponent<Light>();
        key.type = LightType.Directional;
        key.color = new Color(0.45f, 0.85f, 1f); // cool teal
        key.intensity = 1.1f;
        key.shadows = LightShadows.Soft;
        key.shadowStrength = 0.7f;

        // Warm orange back-rim — classic complementary contrast against
        // the teal. Comes from the opposite side, low and behind.
        var rimGo = GameObject.Find("CinematicWarmRim");
        if (rimGo == null) rimGo = new GameObject("CinematicWarmRim", typeof(Light));
        else if (rimGo.GetComponent<Light>() == null) rimGo.AddComponent<Light>();
        rimGo.transform.rotation = Quaternion.Euler(-15f, -150f, 0f);
        var rim = rimGo.GetComponent<Light>();
        rim.type = LightType.Directional;
        rim.color = new Color(1f, 0.55f, 0.2f); // warm orange
        rim.intensity = 0.9f;
        rim.shadows = LightShadows.None;

        // Faint blue fill — fills shadows so they're not pure black
        var fillGo = GameObject.Find("CinematicFill");
        if (fillGo == null) fillGo = new GameObject("CinematicFill", typeof(Light));
        else if (fillGo.GetComponent<Light>() == null) fillGo.AddComponent<Light>();
        fillGo.transform.rotation = Quaternion.Euler(-45f, 90f, 0f);
        var fill = fillGo.GetComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = new Color(0.3f, 0.45f, 0.7f); // dim cool blue
        fill.intensity = 0.25f;
        fill.shadows = LightShadows.None;
    }

    // ── Ambient + fog ─────────────────────────────────────────────────

    private static void SetupRenderSettings()
    {
        // Near-black ambient — the video tunnel provides the bright
        // background, lights provide the figure modeling
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.08f, 0.1f, 0.13f);
        RenderSettings.ambientEquatorColor = new Color(0.04f, 0.05f, 0.07f);
        RenderSettings.ambientGroundColor = new Color(0.02f, 0.02f, 0.03f);

        // No fog — tunnel video has its own depth cues; fog would muddy
        // the image
        RenderSettings.fog = false;
    }

    // ── Post-processing volume ────────────────────────────────────────

    private static VolumeProfile LoadOrCreateProfile()
    {
        EnsureFolder(ProfileFolder);
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }
        return profile;
    }

    private static void SetupVolume(VolumeProfile profile)
    {
        // Strong Bloom — picks up the neon tunnel + robot eye glow
        var bloom = GetOrAdd<Bloom>(profile);
        Override(bloom.intensity, 1.8f);
        Override(bloom.threshold, 0.7f);
        Override(bloom.scatter, 0.75f);
        Override(bloom.tint, new Color(0.9f, 1f, 1f)); // very slight cool tint
        Override(bloom.dirtIntensity, 0.4f);

        // Heavy vignette — frames the robot, deepens corners
        var vignette = GetOrAdd<Vignette>(profile);
        Override(vignette.intensity, 0.55f);
        Override(vignette.smoothness, 0.45f);
        Override(vignette.color, new Color(0.0f, 0.02f, 0.04f));

        // Color adjustments — boost contrast, slightly desaturated
        var colorAdj = GetOrAdd<ColorAdjustments>(profile);
        Override(colorAdj.contrast, 25f);
        Override(colorAdj.saturation, -8f);
        Override(colorAdj.colorFilter, new Color(0.92f, 0.98f, 1f));
        Override(colorAdj.postExposure, -0.1f);

        // White balance — push toward cool
        var whiteBalance = GetOrAdd<WhiteBalance>(profile);
        Override(whiteBalance.temperature, -15f);
        Override(whiteBalance.tint, 5f);

        // Split toning — orange shadows / teal highlights (sci-fi classic)
        var splitToning = GetOrAdd<SplitToning>(profile);
        Override(splitToning.shadows, new Color(1f, 0.55f, 0.2f));
        Override(splitToning.highlights, new Color(0.3f, 0.85f, 1f));
        Override(splitToning.balance, -10f);

        // Depth of Field — Bokeh focused on robot
        var dof = GetOrAdd<DepthOfField>(profile);
        Override(dof.mode, DepthOfFieldMode.Bokeh);
        Override(dof.focusDistance, 5f);
        Override(dof.aperture, 4f);
        Override(dof.focalLength, 50f);
        Override(dof.bladeCount, 6);

        // Stronger chromatic aberration — sci-fi lens feel
        var chroma = GetOrAdd<ChromaticAberration>(profile);
        Override(chroma.intensity, 0.3f);

        // Lens distortion — subtle barrel for "wide-angle blockbuster" feel
        var lens = GetOrAdd<LensDistortion>(profile);
        Override(lens.intensity, -0.1f);
        Override(lens.scale, 1.0f);

        // Film grain — visible, sells the cinematic feel
        var grain = GetOrAdd<FilmGrain>(profile);
        Override(grain.type, FilmGrainLookup.Medium2);
        Override(grain.intensity, 0.3f);
        Override(grain.response, 0.8f);

        // Motion blur — subtle but adds movement when robot rotates
        var motionBlur = GetOrAdd<MotionBlur>(profile);
        Override(motionBlur.intensity, 0.25f);
        Override(motionBlur.quality, MotionBlurQuality.Medium);

        // Tonemapping — ACES for filmic HDR mapping (essential for Bloom)
        var tonemap = GetOrAdd<Tonemapping>(profile);
        Override(tonemap.mode, TonemappingMode.ACES);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        // Create or update the global Volume in the scene. If the forest
        // setup already created one, reuse the GameObject and just swap
        // the profile.
        var volumeGo = GameObject.Find("SplashPostProcessVolume");
        if (volumeGo == null)
        {
            volumeGo = new GameObject("SplashPostProcessVolume", typeof(Volume));
        }
        else if (volumeGo.GetComponent<Volume>() == null)
        {
            volumeGo.AddComponent<Volume>();
        }
        var volume = volumeGo.GetComponent<Volume>();
        volume.isGlobal = true;
        volume.profile = profile;
        volume.priority = 0;
        volume.weight = 1f;
    }

    // ── Camera ────────────────────────────────────────────────────────

    private static void SetupCameraPostProcessing()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[SetupCinematicSplashEnvironment] No Main Camera found — skipping camera config.");
            return;
        }
        var data = cam.GetUniversalAdditionalCameraData();
        if (data == null)
        {
            Debug.LogWarning("[SetupCinematicSplashEnvironment] Camera has no UniversalAdditionalCameraData — skipping.");
            return;
        }
        data.renderPostProcessing = true;
        // TAA gives smoother results with motion blur; SMAA also fine
        data.antialiasing = AntialiasingMode.TemporalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.High;
        cam.allowHDR = true;
        // Solid black background (covered by the tunnel video, but sane
        // default if video is missing)
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        EditorUtility.SetDirty(cam);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet<T>(out var existing)) return existing;
        return profile.Add<T>(true);
    }

    // Sets the parameter's stored value WITHOUT toggling its override
    // checkbox on. The volume component is added to the profile so the
    // user can see + tune everything in Inspector, but nothing actually
    // applies until they tick the override toggle on each parameter
    // they want active.
    private static void Override<T>(VolumeParameter<T> param, T value)
    {
        param.value = value;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parts = path.Split('/');
        string acc = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            var next = $"{acc}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(acc, parts[i]);
            acc = next;
        }
    }
}
