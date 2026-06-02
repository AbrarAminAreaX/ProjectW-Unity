using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Tools → ProjectW → 4. Setup Forest Lighting + Post FX
//
// Configures the open scene for a "forest splash" cinematic look:
//   * Warm sun directional + cool fill light
//   * Trilight ambient (sky/equator/ground) tinted green for canopy spill
//   * Exponential squared fog (mossy green) for atmospheric depth
//   * URP Volume with Bloom, Vignette, ColorAdjustments, DepthOfField,
//     ChromaticAberration, Tonemapping (ACES)
//   * Camera post-processing + SMAA enabled
//
// Idempotent: re-running it updates the existing lights / volume / profile
// instead of duplicating them. The post-processing profile is saved as a
// reusable asset at Assets/PostProcessing/Splash/ForestSplashVolume.asset.
public static class SetupForestSplashEnvironment
{
    private const string ProfileFolder = "Assets/PostProcessing/Splash";
    private const string ProfilePath = ProfileFolder + "/ForestSplashVolume.asset";

    [MenuItem("Tools/ProjectW/4. Setup Forest Lighting + Post FX")]
    public static void Setup()
    {
        SetupLights();
        SetupRenderSettings();
        var profile = LoadOrCreateProfile();
        SetupVolume(profile);
        SetupCameraPostProcessing();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[SetupForestSplashEnvironment] Forest lighting + post-processing configured.");
        EditorUtility.DisplayDialog(
            "Forest Splash",
            "Lighting + post-processing applied.\n\n" +
            "Profile saved to:\n" + ProfilePath + "\n\n" +
            "To complete the forest theme, drop in a forest skybox (Window → Rendering → Lighting → Environment), " +
            "terrain or ground plane, and tree assets — the post-processing is tuned to make those look cinematic.",
            "OK"
        );
    }

    // ── Lights ────────────────────────────────────────────────────────

    private static void SetupLights()
    {
        // Warm key light (sun through canopy gaps)
        var sunGo = GameObject.Find("ForestSun");
        if (sunGo == null) sunGo = new GameObject("ForestSun", typeof(Light));
        else if (sunGo.GetComponent<Light>() == null) sunGo.AddComponent<Light>();
        sunGo.transform.rotation = Quaternion.Euler(40f, 35f, 0f);
        var sun = sunGo.GetComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.92f, 0.74f);
        sun.intensity = 1.4f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.75f;
        sun.shadowBias = 0.05f;
        sun.shadowNormalBias = 0.4f;

        // Cool fill light (diffuse blue-green spill from foliage)
        var fillGo = GameObject.Find("ForestFill");
        if (fillGo == null) fillGo = new GameObject("ForestFill", typeof(Light));
        else if (fillGo.GetComponent<Light>() == null) fillGo.AddComponent<Light>();
        fillGo.transform.rotation = Quaternion.Euler(-25f, -150f, 0f);
        var fill = fillGo.GetComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = new Color(0.55f, 0.75f, 0.65f);
        fill.intensity = 0.45f;
        fill.shadows = LightShadows.None;

        // Disable any leftover default scene light to avoid double-bright
        var defaultLight = GameObject.Find("Directional Light");
        if (defaultLight != null && defaultLight != sunGo && defaultLight != fillGo)
        {
            defaultLight.SetActive(false);
        }
    }

    // ── Ambient + fog ─────────────────────────────────────────────────

    private static void SetupRenderSettings()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.32f, 0.46f, 0.4f);
        RenderSettings.ambientEquatorColor = new Color(0.22f, 0.28f, 0.22f);
        RenderSettings.ambientGroundColor = new Color(0.08f, 0.1f, 0.07f);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.32f, 0.42f, 0.36f);
        RenderSettings.fogDensity = 0.035f;
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
        // Bloom — warm tint, picks up the eye glow + any spec hits
        var bloom = GetOrAdd<Bloom>(profile);
        Override(bloom.intensity, 1.2f);
        Override(bloom.threshold, 0.9f);
        Override(bloom.scatter, 0.7f);
        Override(bloom.tint, new Color(1f, 0.94f, 0.82f));

        // Vignette — dark green corners frame the robot
        var vignette = GetOrAdd<Vignette>(profile);
        Override(vignette.intensity, 0.42f);
        Override(vignette.smoothness, 0.55f);
        Override(vignette.color, new Color(0.04f, 0.08f, 0.05f));

        // Color adjustments — slight green push, modest contrast
        var colorAdj = GetOrAdd<ColorAdjustments>(profile);
        Override(colorAdj.contrast, 15f);
        Override(colorAdj.saturation, 6f);
        Override(colorAdj.colorFilter, new Color(0.96f, 1f, 0.93f));
        Override(colorAdj.postExposure, 0f);

        // Depth of field — Bokeh, focus mid-distance for cinematic feel
        var dof = GetOrAdd<DepthOfField>(profile);
        Override(dof.mode, DepthOfFieldMode.Bokeh);
        Override(dof.focusDistance, 5f);
        Override(dof.aperture, 4.5f);
        Override(dof.focalLength, 50f);
        Override(dof.bladeCount, 6);

        // Subtle chromatic aberration — adds "lens" feel
        var chroma = GetOrAdd<ChromaticAberration>(profile);
        Override(chroma.intensity, 0.15f);

        // Film grain — very subtle
        var grain = GetOrAdd<FilmGrain>(profile);
        Override(grain.type, FilmGrainLookup.Thin1);
        Override(grain.intensity, 0.15f);

        // Tonemapping — ACES gives the "filmic" look; required for HDR Bloom
        // to look right
        var tonemap = GetOrAdd<Tonemapping>(profile);
        Override(tonemap.mode, TonemappingMode.ACES);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        // Create or update the global Volume GameObject in the scene
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
            Debug.LogWarning("[SetupForestSplashEnvironment] No Main Camera found — skipping camera config.");
            return;
        }
        var data = cam.GetUniversalAdditionalCameraData();
        if (data == null)
        {
            Debug.LogWarning("[SetupForestSplashEnvironment] Camera has no UniversalAdditionalCameraData (URP missing?) — skipping.");
            return;
        }
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.High;
        cam.allowHDR = true;
        EditorUtility.SetDirty(cam);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet<T>(out var existing)) return existing;
        return profile.Add<T>(true);
    }

    // Mark a parameter as overridden and set its value in one call
    private static void Override<T>(VolumeParameter<T> param, T value)
    {
        param.overrideState = true;
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
