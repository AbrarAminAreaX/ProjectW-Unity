using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Tools → ProjectW → 8. Create Lime Aurora Splash Variant
//
// Brand-aligned background variant. Project W's brand language is warm,
// organic, "tech as your ally" — bright lime green particle ribbons + soft
// firefly bokeh, NOT cyberpunk teal-neon tunnels.
//
// What this menu does:
//   * Duplicates Splash.unity → Splash_LimeAurora.unity (only on first
//     run — re-runs reuse the existing copy)
//   * Removes the tunnel video infrastructure (BackgroundCamera, quad,
//     VideoPlayer) so we have a clean black canvas
//   * Adds three particle systems for the brand background:
//       - AuroraFlow:    big lime/yellow ribbons drifting horizontally
//                        (the deck's "wind through grass" motif)
//       - BokehFireflies: soft green/yellow bokeh dots floating gently
//                        (warm, alive, "magical companion" feel)
//       - SocialSparkles: occasional pink/purple/orange sparkles drifting
//                        through (hints at the gamified social side)
//   * Resets Main Camera to SolidColor clear, deep warm-shadow black
//
// Original Splash.unity and Splash_Cinematic.unity are untouched.
public static class CreateLimeAuroraSplashVariant
{
    private const string SourcePath = "Assets/Scenes/Splash.unity";
    private const string DestPath = "Assets/Scenes/Splash_LimeAurora.unity";
    private const string MaterialFolder = "Assets/Materials/SplashLimeAurora";

    [MenuItem("Tools/ProjectW/8. Create Lime Aurora Splash Variant")]
    public static void Build()
    {
        if (!File.Exists(SourcePath))
        {
            EditorUtility.DisplayDialog("Lime Aurora Splash", $"Source scene not found: {SourcePath}", "OK");
            return;
        }

        if (EditorSceneManager.GetActiveScene().isDirty)
        {
            EditorSceneManager.SaveOpenScenes();
        }

        if (!File.Exists(DestPath))
        {
            AssetDatabase.CopyAsset(SourcePath, DestPath);
            AssetDatabase.Refresh();
        }

        var scene = EditorSceneManager.OpenScene(DestPath, OpenSceneMode.Single);

        var controllerGo = GameObject.Find("SplashController");
        if (controllerGo == null)
        {
            EditorUtility.DisplayDialog("Lime Aurora Splash", "SplashController GameObject missing in scene.", "OK");
            return;
        }

        var ssc = controllerGo.GetComponent<SplashSceneController>();
        if (ssc == null)
        {
            EditorUtility.DisplayDialog("Lime Aurora Splash", "SplashSceneController component missing.", "OK");
            return;
        }

        var bounds = (ssc.robotRoot != null)
            ? ComputeRendererBounds(ssc.robotRoot)
            : new Bounds(Vector3.zero, Vector3.one);

        // ── 1. Strip the tunnel-video infrastructure ───────────────────
        RemoveVideoInfrastructure(controllerGo);

        // ── 2. Reset Main Camera to single-camera SolidColor ──────────
        var mainCam = Camera.main;
        if (mainCam != null)
        {
            mainCam.clearFlags = CameraClearFlags.SolidColor;
            // Deep warm-shadow black — not pure 0,0,0, has a tiny hint of
            // green so it reads "alive shadow" not "void"
            mainCam.backgroundColor = new Color(0.04f, 0.05f, 0.04f);
            mainCam.allowHDR = true;
        }

        // ── 3. Create brand-background container + particle systems ───
        // Parent to the Main Camera so the background is ALWAYS framed
        // in view, regardless of where SplashSceneController moves the
        // camera during the cinematic.
        EnsureFolder(MaterialFolder);
        var softTex = CreateOrLoadSoftCircleTexture();

        var bgRoot = FindOrCreateChild(controllerGo, "BrandBackground");
        if (mainCam != null)
        {
            bgRoot.transform.SetParent(mainCam.transform, worldPositionStays: false);
        }
        bgRoot.transform.localPosition = new Vector3(0f, 0f, 15f); // 15 units forward in camera local space
        bgRoot.transform.localRotation = Quaternion.identity;

        // Camera frustum dimensions at z=15 with FOV 45°
        const float zDist = 15f;
        float fovRad = (mainCam != null ? mainCam.fieldOfView : 45f) * Mathf.Deg2Rad;
        float frustumHeight = 2f * zDist * Mathf.Tan(fovRad * 0.5f);
        float frustumWidth = frustumHeight * 16f / 9f;

        var auroraMat = LoadOrCreateAdditiveMaterial(
            $"{MaterialFolder}/AuroraMat.mat", new Color(0.6f, 1f, 0.4f), softTex);
        var bokehMat = LoadOrCreateAdditiveMaterial(
            $"{MaterialFolder}/BokehMat.mat", new Color(0.85f, 1f, 0.5f), softTex);
        var sparkleMat = LoadOrCreateAdditiveMaterial(
            $"{MaterialFolder}/SparkleMat.mat", Color.white, softTex);

        ConfigureAuroraFlow(bgRoot, frustumWidth, frustumHeight, auroraMat);
        ConfigureBokehFireflies(bgRoot, frustumWidth, frustumHeight, bokehMat);
        ConfigureSocialSparkles(bgRoot, frustumWidth, frustumHeight, sparkleMat);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"[CreateLimeAuroraSplashVariant] Built {DestPath} with brand-aligned aurora + bokeh background.");
        EditorUtility.DisplayDialog(
            "Lime Aurora Splash",
            "Splash_LimeAurora.unity is ready.\n\n" +
            "Background composition:\n" +
            "  • AuroraFlow — slow lime/yellow ribbons drifting right\n" +
            "  • BokehFireflies — soft green/yellow floating dots\n" +
            "  • SocialSparkles — pink/purple/orange punctuation\n\n" +
            "Camera clear → deep warm black, no tunnel video.\n\n" +
            "Press Play to preview. Original Splash.unity and Splash_Cinematic.unity are untouched.",
            "OK");
    }

    // ── Cleanup ──────────────────────────────────────────────────────

    private static void RemoveVideoInfrastructure(GameObject controllerGo)
    {
        DestroyChild(controllerGo, "SplashBackgroundVideo");
        DestroyChild(controllerGo, "SplashBackgroundQuad");
        DestroyChild(controllerGo, "SplashBackgroundCamera");

        // BuildSplashFX parented some video objects to Main Camera too
        var mainCam = Camera.main;
        if (mainCam != null)
        {
            DestroyChild(mainCam.gameObject, "SplashBackgroundQuad");
            DestroyChild(mainCam.gameObject, "SplashBackgroundCamera");
        }
    }

    private static void DestroyChild(GameObject parent, string name)
    {
        var t = parent.transform.Find(name);
        if (t != null) Object.DestroyImmediate(t.gameObject);
    }

    // ── Aurora wash (background, ~6 huge soft glows drifting) ────────

    private static void ConfigureAuroraFlow(GameObject parent, float frustumW, float frustumH, Material mat)
    {
        var go = FindOrCreateChild(parent, "AuroraFlow", typeof(ParticleSystem));
        go.transform.localPosition = new Vector3(0f, 0f, 6f); // behind bokeh in camera-local space
        go.transform.localRotation = Quaternion.identity;
        var ps = go.GetComponent<ParticleSystem>();

        var main = ps.main;
        main.duration = 60f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
        // Big soft glows — Billboard with soft circle texture, NOT
        // stretched (stretched billboards look hard-edged)
        main.startSize = new ParticleSystem.MinMaxCurve(3f, 6f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.55f, 1f, 0.35f),
            new Color(0.95f, 1f, 0.55f)
        );
        main.gravityModifier = 0f;
        main.playOnAwake = true;
        main.maxParticles = 32;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 3f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(frustumW * 0.9f, frustumH * 0.7f, 0.1f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0.25f);
        vel.y = new ParticleSystem.MinMaxCurve(0f);
        vel.z = new ParticleSystem.MinMaxCurve(0f);

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.3f, 1f),
            new Keyframe(0.7f, 1f), new Keyframe(1f, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] {
                new GradientColorKey(new Color(0.5f, 1f, 0.4f), 0f),
                new GradientColorKey(new Color(0.95f, 1f, 0.5f), 1f),
            },
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.18f, 0.5f),
                new GradientAlphaKey(0f, 1f),
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.4f;
        noise.frequency = 0.1f;
        noise.scrollSpeed = 0.2f;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = mat;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // ── Bokeh fireflies (mid layer, soft floating dots) ──────────────

    private static void ConfigureBokehFireflies(GameObject parent, float frustumW, float frustumH, Material mat)
    {
        var go = FindOrCreateChild(parent, "BokehFireflies", typeof(ParticleSystem));
        go.transform.localPosition = new Vector3(0f, 0f, 3f);
        go.transform.localRotation = Quaternion.identity;
        var ps = go.GetComponent<ParticleSystem>();

        var main = ps.main;
        main.duration = 60f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 11f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.7f, 1f, 0.4f),
            new Color(1f, 1f, 0.65f)
        );
        main.gravityModifier = 0f;
        main.playOnAwake = true;
        main.maxParticles = 140;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 12f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(frustumW * 0.95f, frustumH * 0.85f, 4f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.25f);
        vel.z = new ParticleSystem.MinMaxCurve(0f);

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.2f, 1f),
            new Keyframe(0.7f, 1f), new Keyframe(1f, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] {
                new GradientColorKey(new Color(0.7f, 1f, 0.4f), 0f),
                new GradientColorKey(new Color(1f, 1f, 0.65f), 1f),
            },
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.7f, 0.3f),
                new GradientAlphaKey(0.7f, 0.75f),
                new GradientAlphaKey(0f, 1f),
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 0.25f;
        noise.scrollSpeed = 0.5f;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = mat;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // ── Social sparkles (front layer, brand-color punctuation) ──────

    private static void ConfigureSocialSparkles(GameObject parent, float frustumW, float frustumH, Material mat)
    {
        var go = FindOrCreateChild(parent, "SocialSparkles", typeof(ParticleSystem));
        go.transform.localPosition = new Vector3(0f, 0f, 2f);
        go.transform.localRotation = Quaternion.identity;
        var ps = go.GetComponent<ParticleSystem>();

        var main = ps.main;
        main.duration = 60f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.4f, 0.7f),
            new Color(0.7f, 0.5f, 1f)
        );
        main.gravityModifier = 0f;
        main.playOnAwake = true;
        main.maxParticles = 35;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 2.5f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(frustumW * 1.0f, frustumH * 0.9f, 2.5f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.3f);
        vel.z = new ParticleSystem.MinMaxCurve(0f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] {
                new GradientColorKey(new Color(1f, 0.4f, 0.7f), 0f),
                new GradientColorKey(new Color(0.75f, 0.55f, 1f), 0.5f),
                new GradientColorKey(new Color(1f, 0.6f, 0.3f), 1f),
            },
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.55f, 0.3f),
                new GradientAlphaKey(0.55f, 0.7f),
                new GradientAlphaKey(0f, 1f),
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.5f;
        noise.frequency = 0.3f;
        noise.scrollSpeed = 0.6f;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = mat;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // ── Material / helpers ──────────────────────────────────────────

    private static Material LoadOrCreateAdditiveMaterial(string assetPath, Color tint, Texture2D tex)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        bool isNew = mat == null;

        if (isNew)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            mat = new Material(shader);
        }

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
        if (tex != null)
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            mat.mainTexture = tex;
        }
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 1f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.One);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_BLENDMODE_ADD");
        mat.renderQueue = (int)RenderQueue.Transparent;

        if (isNew) AssetDatabase.CreateAsset(mat, assetPath);
        else EditorUtility.SetDirty(mat);
        return mat;
    }

    // Procedurally generates a 128² soft white circle (radial alpha
    // falloff) and saves as PNG. Used by all particle materials so they
    // render as soft glows, not hard squares.
    private static Texture2D CreateOrLoadSoftCircleTexture()
    {
        const string path = MaterialFolder + "/SoftCircle.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        const int size = 128;
        const int half = size / 2;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / (float)half;
                float dy = (y - half) / (float)half;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(1f - dist);
                // Squared falloff = much softer edge
                alpha = alpha * alpha;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);

        // Configure import: alpha-is-transparency, clamp wrap, bilinear
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static GameObject FindOrCreateChild(GameObject parent, string name, params System.Type[] types)
    {
        var existing = parent.transform.Find(name);
        if (existing != null)
        {
            var go = existing.gameObject;
            foreach (var t in types)
            {
                if (t != null && go.GetComponent(t) == null) go.AddComponent(t);
            }
            return go;
        }
        var newGo = types.Length > 0 ? new GameObject(name, types) : new GameObject(name);
        newGo.transform.SetParent(parent.transform, worldPositionStays: false);
        return newGo;
    }

    private static Bounds ComputeRendererBounds(Transform root)
    {
        if (root == null) return new Bounds(Vector3.zero, Vector3.one);
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(root.position, Vector3.one);
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
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
