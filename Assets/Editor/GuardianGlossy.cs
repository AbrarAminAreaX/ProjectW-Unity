using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

// Makes the Spirit Guardian glossy/shiny and lights it so highlights are
// visible. Idempotent: re-running re-applies material values and rebuilds the
// light rig + reflection probe. Tweak the constants and re-run to taste.
public static class GuardianGlossy
{
    const float Smoothness = 0.88f;
    const float Metallic   = 0.45f;

    [MenuItem("Tools/ProjectW/Make Guardian Glossy")]
    public static void Apply()
    {
        var robot = GameObject.Find("SpiritGuardian");
        if (robot == null) { Debug.LogError("[GuardianGlossy] SpiritGuardian not found."); return; }

        // ── 1. Shiny materials ───────────────────────────────────────────
        int matCount = 0;
        var seen = new HashSet<Material>();
        foreach (var r in robot.GetComponentsInChildren<Renderer>(true))
        {
            // Skip the additive glow quads — they aren't lit surfaces.
            if (r.name == "ChestHeart" || r.name == "FaceSmile") continue;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || !seen.Add(m)) continue;
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", Smoothness);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", Smoothness); // Standard fallback
                if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", Metallic);
                // URP Lit: make sure specular + environment reflections are on.
                m.EnableKeyword("_SPECULARHIGHLIGHTS_ON");
                m.EnableKeyword("_ENVIRONMENTREFLECTIONS_ON");
                m.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
                m.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                EditorUtility.SetDirty(m);
                matCount++;
            }
        }

        // ── 2. Light rig (key + rim + accent) ────────────────────────────
        // Parented to the scene (not the robot) so it doesn't collapse when the
        // robot scales away in Phase 3. Robot sits near (0, 0.5, 0); camera is
        // on the -Z side, so the key light is on -Z to light the front.
        var oldLights = GameObject.Find("GuardianLights");
        if (oldLights != null) Object.DestroyImmediate(oldLights);
        var rig = new GameObject("GuardianLights");

        AddPoint(rig.transform, "KeyLight",    new Vector3( 1.3f, 1.5f, -1.8f), new Color(1f, 0.92f, 0.78f), 220f, 8f);
        AddPoint(rig.transform, "RimLight",    new Vector3(-1.4f, 1.1f,  1.6f), new Color(0.55f, 0.8f, 1f), 260f, 8f);
        AddPoint(rig.transform, "AccentLight", new Vector3( 1.7f, 0.2f,  0.6f), new Color(1f, 0.45f, 0.85f), 120f, 6f);

        // ── 3. Reflection probe (so the gloss reflects the nebula) ────────
        var oldProbe = GameObject.Find("GuardianReflection");
        if (oldProbe != null) Object.DestroyImmediate(oldProbe);
        var probeGo = new GameObject("GuardianReflection");
        probeGo.transform.position = new Vector3(0f, 0.6f, 0f);
        var probe = probeGo.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting; // captured below; cheap
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.resolution = 128;
        probe.size = new Vector3(8f, 8f, 8f);
        probe.intensity = 1f;
        probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
        probe.backgroundColor = new Color(0.04f, 0.02f, 0.08f, 1f);
        probe.cullingMask = ~0;
        probe.RenderProbe();

        EditorUtility.SetDirty(rig);
        AssetDatabase.SaveAssets();
        Debug.Log($"[GuardianGlossy] Set {matCount} materials to smoothness {Smoothness}/metallic {Metallic}, added 3 lights + reflection probe.");
    }

    static void AddPoint(Transform parent, string name, Vector3 pos, Color color, float intensity, float range)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
    }
}
