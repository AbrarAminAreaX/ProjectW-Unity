using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Tools → ProjectW → 7. Boost Splash Quality
//
// Applies a "high-quality" rendering pass to BOTH splash scenes
// (Splash.unity and Splash_Cinematic.unity) without touching anything
// else in the project:
//   * Boosts shared Volume Profile assets — Bloom, ColorAdjustments,
//     adds Vignette + ChromaticAberration if missing
//   * Adds a baked-style Realtime ReflectionProbe so metallic robot
//     surfaces show real reflections instead of flat shading
//   * Bumps Rim Light intensity + enables soft shadows on the key
//     directional light if present
//   * Adds a subtle "Bounce" fill light so shadows aren't crushed
//
// Idempotent: re-running it just re-applies the values, no duplicates.
//
// Things this script CAN'T do (need Editor manual steps — see dialog):
//   * Add SSAO/SSR Renderer Features to the URP Renderer Data
//   * Bump URP Asset render scale / MSAA / shadow resolution
//   * Replace robot materials with PBR textures (model-level work)
public static class BoostSplashQuality
{
    private static readonly string[] ScenePaths = new[]
    {
        "Assets/Scenes/Splash.unity",
        "Assets/Scenes/Splash_Cinematic.unity",
    };

    private static readonly string[] VolumeProfilePaths = new[]
    {
        "Assets/PostProcessing/Splash/ForestSplashVolume.asset",
        "Assets/PostProcessing/Splash/CinematicSplashVolume.asset",
    };

    [MenuItem("Tools/ProjectW/7. Boost Splash Quality")]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().isDirty)
        {
            EditorSceneManager.SaveOpenScenes();
        }
        var originalScenePath = EditorSceneManager.GetActiveScene().path;

        // 1. Boost shared Volume Profile assets
        int profilesBoosted = 0;
        foreach (var path in VolumeProfilePaths)
        {
            if (BoostVolumeProfile(path)) profilesBoosted++;
        }

        // 2. Per-scene enhancements
        int scenesEnhanced = 0;
        foreach (var path in ScenePaths)
        {
            if (ApplyToScene(path)) scenesEnhanced++;
        }

        // Reopen the scene the user was working on
        if (!string.IsNullOrEmpty(originalScenePath) && File.Exists(originalScenePath))
        {
            EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[BoostSplashQuality] Boosted {profilesBoosted} volume profile(s) and {scenesEnhanced} scene(s).");
        EditorUtility.DisplayDialog(
            "Splash Quality Boost",
            $"Done.\n\n" +
            $"Volume profiles updated: {profilesBoosted}\n" +
            $"Scenes enhanced: {scenesEnhanced}\n\n" +
            "What was changed:\n" +
            "  • Bloom 2.5 / threshold 0.8\n" +
            "  • ColorAdjustments +exposure +contrast +saturation\n" +
            "  • Vignette + ChromaticAberration enabled\n" +
            "  • ReflectionProbe (Realtime) added/refreshed\n" +
            "  • RimLight intensity bumped to 12 with soft shadows\n" +
            "  • Bounce fill light added\n\n" +
            "MANUAL STEPS for max quality:\n" +
            "  1. Edit → Project Settings → Quality → URP-HighQuality:\n" +
            "       Render Scale: 1.25,  MSAA: 4×,  Shadow Distance: 50,\n" +
            "       Shadow Resolution: VeryHigh, Soft Shadows: On\n" +
            "  2. Find your URP Renderer asset → Add Renderer Feature →\n" +
            "       'Screen Space Ambient Occlusion' (huge realism win)\n" +
            "  3. Optional: 'Screen Space Reflections' (pricier)\n" +
            "  4. Replace robot materials with PBR textures from\n" +
            "       polyhaven.com or ambientcg.com (search 'painted metal')",
            "OK");
    }

    // ── Volume Profile boost ─────────────────────────────────────────

    private static bool BoostVolumeProfile(string path)
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            Debug.Log($"[BoostSplashQuality] Volume profile not found at {path} — skipping.");
            return false;
        }

        // Bloom — stronger pickup
        var bloom = GetOrAdd<Bloom>(profile);
        Override(bloom.intensity, 2.5f);
        Override(bloom.threshold, 0.8f);
        Override(bloom.scatter, 0.7f);
        Override(bloom.tint, new Color(1f, 0.96f, 0.9f));

        // Color adjustments — slight overall punch
        var colorAdj = GetOrAdd<ColorAdjustments>(profile);
        Override(colorAdj.postExposure, 0.3f);
        Override(colorAdj.contrast, 22f);
        Override(colorAdj.saturation, 5f);

        // Vignette
        var vignette = GetOrAdd<Vignette>(profile);
        Override(vignette.intensity, 0.45f);
        Override(vignette.smoothness, 0.5f);
        Override(vignette.color, new Color(0f, 0.02f, 0.05f));

        // Chromatic aberration — subtle lens feel
        var chroma = GetOrAdd<ChromaticAberration>(profile);
        Override(chroma.intensity, 0.2f);

        // Tonemapping — required for HDR Bloom to look right
        var tonemap = GetOrAdd<Tonemapping>(profile);
        Override(tonemap.mode, TonemappingMode.ACES);

        EditorUtility.SetDirty(profile);
        return true;
    }

    // ── Per-scene enhancements ───────────────────────────────────────

    private static bool ApplyToScene(string scenePath)
    {
        if (!File.Exists(scenePath))
        {
            Debug.Log($"[BoostSplashQuality] Scene not found: {scenePath} — skipping.");
            return false;
        }

        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var controllerGo = GameObject.Find("SplashController");
        if (controllerGo == null)
        {
            Debug.LogWarning($"[BoostSplashQuality] SplashController missing in {scenePath} — skipping.");
            return false;
        }

        var ssc = controllerGo.GetComponent<SplashSceneController>();
        if (ssc == null)
        {
            Debug.LogWarning($"[BoostSplashQuality] SplashSceneController missing in {scenePath} — skipping.");
            return false;
        }

        Bounds bounds = (ssc.robotRoot != null)
            ? ComputeRendererBounds(ssc.robotRoot)
            : new Bounds(Vector3.zero, Vector3.one);

        AddReflectionProbe(controllerGo, bounds);
        BoostRimLight(controllerGo);
        AddBounceFillLight(controllerGo, bounds);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return true;
    }

    private static void AddReflectionProbe(GameObject parent, Bounds bounds)
    {
        var probeGo = FindOrCreateChild(parent, "SplashReflectionProbe", typeof(ReflectionProbe));
        var probe = probeGo.GetComponent<ReflectionProbe>();
        probeGo.transform.position = bounds.center;

        // Box covers the whole robot + breathing room
        Vector3 boxSize = new Vector3(
            Mathf.Max(bounds.size.x, 1f) * 4f,
            Mathf.Max(bounds.size.y, 1f) * 4f,
            Mathf.Max(bounds.size.z, 1f) * 4f);
        probe.size = boxSize;
        probe.center = Vector3.zero;

        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
        probe.boxProjection = true;
        probe.intensity = 1f;
        probe.cullingMask = ~0;
        probe.resolution = 256;
        probe.hdr = true;
    }

    private static void BoostRimLight(GameObject parent)
    {
        var rimT = parent.transform.Find("RimLight");
        if (rimT == null) return;
        var rim = rimT.GetComponent<Light>();
        if (rim == null) return;
        rim.intensity = 12f;
        rim.shadows = LightShadows.Soft;
        rim.shadowStrength = 0.85f;
    }

    private static void AddBounceFillLight(GameObject parent, Bounds bounds)
    {
        var fillGo = FindOrCreateChild(parent, "BounceFillLight", typeof(Light));
        var fill = fillGo.GetComponent<Light>();
        if (fill == null) return;
        // Position below + opposite the key light (warm bounce off ground)
        fillGo.transform.position = bounds.center + new Vector3(-bounds.size.x, -bounds.size.y * 0.6f, -bounds.size.z * 0.5f);
        fillGo.transform.LookAt(bounds.center);
        fill.type = LightType.Directional;
        fill.color = new Color(0.9f, 0.7f, 0.5f); // warm bounce tint
        fill.intensity = 0.35f;
        fill.shadows = LightShadows.None;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet<T>(out var existing)) return existing;
        return profile.Add<T>(true);
    }

    private static void Override<T>(VolumeParameter<T> param, T value)
    {
        param.overrideState = true;
        param.value = value;
    }

    private static GameObject FindOrCreateChild(GameObject parent, string name, params System.Type[] componentTypes)
    {
        var existing = parent.transform.Find(name);
        if (existing != null)
        {
            var go = existing.gameObject;
            foreach (var t in componentTypes)
            {
                if (t != null && go.GetComponent(t) == null) go.AddComponent(t);
            }
            return go;
        }
        var newGo = componentTypes.Length > 0 ? new GameObject(name, componentTypes) : new GameObject(name);
        newGo.transform.SetParent(parent.transform, worldPositionStays: false);
        return newGo;
    }

    private static Bounds ComputeRendererBounds(Transform root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(root.position, Vector3.one);
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }
}
