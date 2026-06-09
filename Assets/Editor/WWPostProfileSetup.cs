using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;

// One-shot setup for the Western World post-processing look (warm cinematic desert).
// Idempotent: clears existing overrides before re-adding, so it is safe to re-run.
public static class WWPostProfileSetup
{
    const string ProfilePath = "Assets/Western_World_spiritGuardian/WW_PostProfile.asset";

    [MenuItem("Tools/Western World/Setup PostFX Profile")]
    public static void Setup()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            Debug.LogError($"[WWPostProfileSetup] WW_PostProfile not found at {ProfilePath}");
            return;
        }

        // Make idempotent: detach + destroy any existing override sub-assets.
        foreach (var c in profile.components.ToArray())
        {
            profile.components.Remove(c);
            Object.DestroyImmediate(c, true);
        }
        // Safety net: remove any orphaned VolumeComponent sub-assets left in the file.
        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(ProfilePath))
        {
            if (sub is VolumeComponent vc)
                Object.DestroyImmediate(vc, true);
        }

        // Tonemapping - Neutral (filmic but uncolored; plays nice with day/night controller)
        var tone = Add<Tonemapping>(profile);
        tone.mode.overrideState = true;
        tone.mode.value = TonemappingMode.Neutral;

        // White Balance - push warm for the sunbaked desert feel
        var wb = Add<WhiteBalance>(profile);
        wb.temperature.overrideState = true; wb.temperature.value = 15f;
        wb.tint.overrideState = true;        wb.tint.value = 3f;

        // Color Adjustments - subtle contrast + saturation, warm color filter
        var ca = Add<ColorAdjustments>(profile);
        ca.contrast.overrideState = true;    ca.contrast.value = 8f;
        ca.saturation.overrideState = true;  ca.saturation.value = 5f;
        ca.colorFilter.overrideState = true; ca.colorFilter.value = new Color(1f, 0.96f, 0.90f, 1f);

        // Bloom - soft, warm-tinted highlights
        var bloom = Add<Bloom>(profile);
        bloom.intensity.overrideState = true; bloom.intensity.value = 0.6f;
        bloom.threshold.overrideState = true; bloom.threshold.value = 1.1f;
        bloom.scatter.overrideState = true;   bloom.scatter.value = 0.7f;
        bloom.tint.overrideState = true;      bloom.tint.value = new Color(1f, 0.95f, 0.85f, 1f);

        // Vignette - gentle, slightly warm-dark
        var vig = Add<Vignette>(profile);
        vig.intensity.overrideState = true;  vig.intensity.value = 0.25f;
        vig.smoothness.overrideState = true; vig.smoothness.value = 0.4f;
        vig.color.overrideState = true;      vig.color.value = new Color(0.05f, 0.03f, 0.02f, 1f);

        // Film Grain - light, fine
        var grain = Add<FilmGrain>(profile);
        grain.type.overrideState = true;      grain.type.value = FilmGrainLookup.Thin1;
        grain.intensity.overrideState = true; grain.intensity.value = 0.15f;
        grain.response.overrideState = true;  grain.response.value = 0.8f;

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(ProfilePath);
        Debug.Log("[WWPostProfileSetup] WW_PostProfile configured: Tonemapping, WhiteBalance, ColorAdjustments, Bloom, Vignette, FilmGrain.");
    }

    // Creates a VolumeComponent, registers it as a hidden sub-asset of the profile
    // (the step VolumeProfile.Add does NOT do, which is why nothing was persisted before).
    static T Add<T>(VolumeProfile profile) where T : VolumeComponent
    {
        var comp = ScriptableObject.CreateInstance<T>();
        comp.hideFlags = HideFlags.HideInHierarchy;
        comp.name = typeof(T).Name;
        profile.components.Add(comp);
        AssetDatabase.AddObjectToAsset(comp, profile);
        return comp;
    }
}
