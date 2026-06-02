using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.Video;

// Tools → ProjectW → 9. Create Plant Splash Variant
//
// Self-contained "Pixar bioluminescent plant" splash variant. Generates
// Splash_Plant.unity from the original Splash.unity and configures it
// for a Veo-generated plant background video. Nothing else in the
// project is touched (no shared materials, no shared Volume Profile,
// no other scenes).
//
// Setup expected from you:
//   1. Generate the plant background video in Google Flow (use the
//      prompt I gave you).
//   2. Save the MP4 as:
//        Assets/Backgrounds/PlantSplashBg.mp4
//   3. Run this menu — it will:
//        - duplicate Splash.unity → Splash_Plant.unity
//        - strip ALL existing background infrastructure from the copy
//        - set up two-camera video stack pointing at PlantSplashBg.mp4
//        - configure Pixar-style lighting (warm key + cool fill +
//          lime-green rim + subsurface-suggesting ambient)
//        - bind a NEW Volume Profile (PlantSplashVolume.asset) tuned
//          for the warm wholesome aesthetic
//   4. Press Play in the new scene to preview.
//
// If PlantSplashBg.mp4 doesn't exist yet, the lighting + Volume still
// get applied; the background will just be solid color until you drop
// the file in.
public static class CreatePlantSplashVariant
{
    private const string SourcePath = "Assets/Scenes/Splash.unity";
    private const string DestPath = "Assets/Scenes/Splash_Plant.unity";
    private const string VideoFolder = "Assets/Backgrounds";
    private const string VideoPath = VideoFolder + "/PlantSplashBg.mp4";
    private const string MaterialFolder = "Assets/Materials/SplashPlant";
    private const string VolumeFolder = "Assets/PostProcessing/Splash";
    private const string VolumeProfilePath = VolumeFolder + "/PlantSplashVolume.asset";

    // Re-apply ONLY the lighting + Volume Profile to the open Plant scene.
    // Preserves bloom/knock/camera tweaks and everything else. Use after
    // switching the background video to a new theme.
    [MenuItem("Tools/ProjectW/9b. Refresh Plant Lighting (current scene)")]
    public static void RefreshLighting()
    {
        var active = EditorSceneManager.GetActiveScene();
        if (!active.path.EndsWith("Splash_Plant.unity"))
        {
            EditorUtility.DisplayDialog(
                "Refresh Plant Lighting",
                $"Open Splash_Plant.unity first.\n\nCurrently active: {(string.IsNullOrEmpty(active.path) ? "(untitled)" : active.path)}",
                "OK");
            return;
        }

        var controllerGo = GameObject.Find("SplashController");
        if (controllerGo == null)
        {
            EditorUtility.DisplayDialog("Refresh Plant Lighting", "SplashController GameObject missing.", "OK");
            return;
        }
        var ssc = controllerGo.GetComponent<SplashSceneController>();
        if (ssc == null)
        {
            EditorUtility.DisplayDialog("Refresh Plant Lighting", "SplashSceneController component missing.", "OK");
            return;
        }

        EnsureFolder(VolumeFolder);

        var bounds = (ssc.robotRoot != null)
            ? ComputeRendererBounds(ssc.robotRoot)
            : new Bounds(Vector3.zero, Vector3.one);

        ConfigurePixarLighting(controllerGo, bounds);
        var profile = BuildVolumeProfile();
        BindVolumeToScene(controllerGo, profile);

        EditorSceneManager.MarkSceneDirty(active);
        EditorSceneManager.SaveScene(active);
        AssetDatabase.SaveAssets();

        Debug.Log("[CreatePlantSplashVariant] Lighting + Volume Profile refreshed for Petal1 theme.");
        EditorUtility.DisplayDialog(
            "Refresh Plant Lighting",
            "Lighting + Volume Profile updated to the Petal1 theme.\n\n" +
            "Changed: PlantKey (warm honey), PlantFill (pink magenta), " +
            "PlantRim (golden amber), Trilight ambient (peach/pink-gold/" +
            "magenta-brown), Bloom/Vignette/ColorAdj retuned.\n\n" +
            "Untouched: bloom-entrance settings, knock settings, camera " +
            "position, background video, robot pose.",
            "OK");
    }

    [MenuItem("Tools/ProjectW/9. Create Plant Splash Variant")]
    public static void Build()
    {
        if (!File.Exists(SourcePath))
        {
            EditorUtility.DisplayDialog("Plant Splash", $"Source scene not found: {SourcePath}", "OK");
            return;
        }

        if (EditorSceneManager.GetActiveScene().isDirty)
        {
            EditorSceneManager.SaveOpenScenes();
        }

        // Protect saved edits: if Splash_Plant.unity already exists, ask
        // before doing anything that could overwrite it. Default = just
        // open the scene, leave its contents alone.
        if (File.Exists(DestPath))
        {
            int choice = EditorUtility.DisplayDialogComplex(
                "Plant Splash",
                $"{DestPath} already exists and may contain saved tweaks " +
                "(bloom origin, knock hand, lighting, etc.).\n\n" +
                "Open without modifying — your saved changes are kept.\n" +
                "Recreate from scratch — wipes the scene and rebuilds.",
                "Open (keep my changes)",  // 0 — safe default
                "Cancel",                  // 1
                "Recreate from scratch"    // 2 — destructive
            );
            if (choice == 0)
            {
                EditorSceneManager.OpenScene(DestPath, OpenSceneMode.Single);
                return;
            }
            if (choice == 1) return;
            // choice == 2: continue and let CopyAsset overwrite. Delete
            // first so CopyAsset writes a clean duplicate of Splash.unity.
            AssetDatabase.DeleteAsset(DestPath);
            AssetDatabase.Refresh();
        }

        EnsureFolder(VideoFolder);
        EnsureFolder(MaterialFolder);
        EnsureFolder(VolumeFolder);

        if (!File.Exists(DestPath))
        {
            AssetDatabase.CopyAsset(SourcePath, DestPath);
            AssetDatabase.Refresh();
        }

        var scene = EditorSceneManager.OpenScene(DestPath, OpenSceneMode.Single);

        var controllerGo = GameObject.Find("SplashController");
        if (controllerGo == null)
        {
            EditorUtility.DisplayDialog("Plant Splash", "SplashController GameObject missing in scene.", "OK");
            return;
        }
        var ssc = controllerGo.GetComponent<SplashSceneController>();
        if (ssc == null)
        {
            EditorUtility.DisplayDialog("Plant Splash", "SplashSceneController component missing.", "OK");
            return;
        }

        var bounds = (ssc.robotRoot != null)
            ? ComputeRendererBounds(ssc.robotRoot)
            : new Bounds(Vector3.zero, Vector3.one);

        // ── 1. Strip all existing background infrastructure ──────────
        StripExistingBackgrounds(controllerGo);

        // ── 2. Configure Pixar-style 3-point lighting ────────────────
        ConfigurePixarLighting(controllerGo, bounds);

        // ── 3. Build new Volume Profile + bind to scene volume ───────
        var profile = BuildVolumeProfile();
        BindVolumeToScene(controllerGo, profile);

        // ── 4. Set Main Camera to deep warm tone (fallback if video missing) ─
        var mainCam = Camera.main;
        if (mainCam != null)
        {
            mainCam.allowHDR = true;
            // Warm shadow tone — feels like inside a glowing plant rather
            // than space. Will be overdrawn by the video when present.
            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = new Color(0.08f, 0.06f, 0.04f);
        }

        // ── 5. Build the two-camera video stack (only if MP4 exists) ─
        bool videoFound = ConfigureBackgroundVideo(controllerGo, mainCam);

        // ── 6. Switch to bloom-entrance mode (scale-up from flower) ──
        //    Configure BEFORE camera placement so the camera can scale its
        //    distance to fit the post-bloom robot size (1.5× by default).
        ConfigureBloomEntrance(ssc, bounds);

        // ── 7. Place camera IN FRONT of robot (faces it throughout) ──
        if (mainCam != null && ssc.robotRoot != null)
        {
            PositionCameraInFront(ssc, mainCam, bounds, ssc.bloomEntranceEndScale);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"[CreatePlantSplashVariant] Built {DestPath}. Video {(videoFound ? "wired" : "not found — drop MP4 at " + VideoPath + " then re-run")}.");
        EditorUtility.DisplayDialog(
            "Plant Splash",
            $"Splash_Plant.unity is ready.\n\n" +
            $"Background video: {(videoFound ? "wired ✓ (RawImage in PlantBackgroundCanvas)" : "MISSING — save MP4 to " + VideoPath + " then re-run this menu")}\n\n" +
            "Layering:\n" +
            "  • PlantBackgroundCanvas (sortingOrder -100) — VIDEO\n" +
            "  • Main Camera 3D — ROBOT\n" +
            "  • Add your own UI Canvas (sortingOrder ≥ 0) — LOGOS / TEXT\n\n" +
            "Aspect: AspectRatioFitter / EnvelopeParent — auto-fits to screen.\n\n" +
            "Entrance: BLOOM (no part assembly). Robot starts at scale 0 at " +
            "the flower-base point (lower-center), grows to 1.5× while rising " +
            "into frame — like emerging from the glowing cavity in the video. " +
            "Then eyes light up + greeting beat.\n\n" +
            "Greeting (SplashController.greetingMode):\n" +
            "  • None — skip the beat\n" +
            "  • WaveOnly — original wave\n" +
            "  • KnockOnly — 3-tap knock toward camera\n" +
            "  • KnockThenWave — knock, then wave (default for plant)\n\n" +
            "Knock hand bone auto-assigned (check SplashController.knockHand " +
            "in the inspector — reassign if it picked the wrong bone).\n\n" +
            "Lighting: warm key (golden hour), cool cyan fill (subsurface bounce), " +
            "lime-green rim (bioluminescence), trilight ambient.\n\n" +
            "Original Splash.unity, Splash_Cinematic.unity, Splash_LimeAurora.unity untouched.",
            "OK");
    }

    // ── Strip everything previous splash menus added ────────────────

    private static void StripExistingBackgrounds(GameObject controllerGo)
    {
        // Inherited from the copied Splash.unity
        DestroyChild(controllerGo, "SplashBackgroundVideo");
        DestroyChild(controllerGo, "SplashBackgroundQuad");
        DestroyChild(controllerGo, "SplashBackgroundCamera");
        DestroyChild(controllerGo, "BrandBackground"); // lime aurora variant's particle root
        // Leftover from earlier two-camera version of this menu
        DestroyChild(controllerGo, "PlantBackgroundCamera");
        DestroyChild(controllerGo, "PlantBackgroundQuad");

        var mainCam = Camera.main;
        if (mainCam != null)
        {
            DestroyChild(mainCam.gameObject, "SplashBackgroundQuad");
            DestroyChild(mainCam.gameObject, "SplashBackgroundCamera");
            DestroyChild(mainCam.gameObject, "BrandBackground");
            DestroyChild(mainCam.gameObject, "PlantBackgroundCamera");
            DestroyChild(mainCam.gameObject, "PlantBackgroundQuad");
        }
    }

    // ── Pixar-style 3-point lighting (Petal1 theme) ─────────────────
    //
    // Tuned to match Backgrounds/Petal1.mp4 — pastel bloom with magenta-pink
    // outer petals, lime-tipped centers, honey-cream sky, and a bright
    // golden central light shaft. The old bioluminescent (lime + cyan) mix
    // would clash with this palette, so:
    //   - Key: warm honey-gold (the central light feels like sun)
    //   - Fill: soft magenta-pink (petal bounce on the shadow side)
    //   - Rim: bright amber from behind (sells the central light shaft)
    //   - Ambient: peach → pink-gold → warm magenta-brown
    private static void ConfigurePixarLighting(GameObject controllerGo, Bounds bounds)
    {
        // Disable any leftover lights from earlier menus that would
        // double-bright the scene
        foreach (var n in new[] { "ForestSun", "ForestFill", "CinematicKey", "CinematicWarmRim", "CinematicFill", "Directional Light", "BounceFillLight" })
        {
            var go = GameObject.Find(n);
            if (go != null) go.SetActive(false);
        }

        // Key — warm honey-gold directional, soft shadows. Comes from
        // above and slightly camera-side to feel like the bloom's central
        // light beam catches the robot.
        var keyGo = FindOrCreateChild(controllerGo, "PlantKey", typeof(Light));
        keyGo.transform.rotation = Quaternion.Euler(50f, 20f, 0f);
        var key = keyGo.GetComponent<Light>();
        key.type = LightType.Directional;
        key.color = new Color(1.0f, 0.85f, 0.55f);
        key.intensity = 1.4f;
        key.shadows = LightShadows.Soft;
        key.shadowStrength = 0.75f;

        // Fill — warm magenta-pink from the opposite side. Replaces the
        // old cyan fill — Petal1's petal bounce is pink/rose, not cool.
        var fillGo = FindOrCreateChild(controllerGo, "PlantFill", typeof(Light));
        fillGo.transform.rotation = Quaternion.Euler(-20f, -150f, 0f);
        var fill = fillGo.GetComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = new Color(0.95f, 0.55f, 0.7f);
        fill.intensity = 0.55f;
        fill.shadows = LightShadows.None;

        // Rim — bright golden amber spot from behind, mimicking the
        // central light shaft in the video silhouetting the robot.
        var rimGo = FindOrCreateChild(controllerGo, "PlantRim", typeof(Light));
        var rp = bounds.center + new Vector3(0f, bounds.size.y * 0.55f, bounds.size.z * 1.5f);
        rimGo.transform.position = rp;
        rimGo.transform.LookAt(bounds.center);
        var rim = rimGo.GetComponent<Light>();
        rim.type = LightType.Spot;
        rim.color = new Color(1.0f, 0.72f, 0.35f);
        rim.intensity = 16f;
        rim.range = Mathf.Max(8f, bounds.size.magnitude * 4f);
        rim.spotAngle = 55f;
        rim.innerSpotAngle = 30f;
        rim.shadows = LightShadows.None;

        // Trilight ambient — peach sky / pink-gold equator / warm magenta-brown ground
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.70f, 0.55f, 0.45f);
        RenderSettings.ambientEquatorColor = new Color(0.55f, 0.35f, 0.40f);
        RenderSettings.ambientGroundColor = new Color(0.12f, 0.06f, 0.10f);

        // No fog — video already supplies depth/atmosphere
        RenderSettings.fog = false;
    }

    // ── Volume Profile ──────────────────────────────────────────────

    private static VolumeProfile BuildVolumeProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        // If the previous run created the profile without registering its
        // VolumeComponents as sub-assets, the asset will load with an empty
        // components list. Wipe and recreate so we don't carry forward broken state.
        if (profile != null && (profile.components == null || profile.components.Count == 0))
        {
            AssetDatabase.DeleteAsset(VolumeProfilePath);
            profile = null;
        }
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);
        }

        // Petal1 theme — punchier bloom for the glowing spores + central
        // light shaft, warm pink-magenta vignette to pull into the petals.
        var bloom = GetOrAdd<Bloom>(profile);
        Override(bloom.intensity, 2.0f);
        Override(bloom.threshold, 0.80f);
        Override(bloom.scatter, 0.75f);
        Override(bloom.tint, new Color(1f, 0.90f, 0.78f));

        var vignette = GetOrAdd<Vignette>(profile);
        Override(vignette.intensity, 0.40f);
        Override(vignette.smoothness, 0.65f);
        Override(vignette.color, new Color(0.12f, 0.04f, 0.08f));

        var colorAdj = GetOrAdd<ColorAdjustments>(profile);
        Override(colorAdj.contrast, 22f);
        Override(colorAdj.saturation, 12f);
        Override(colorAdj.colorFilter, new Color(1.0f, 0.95f, 0.88f));
        Override(colorAdj.postExposure, 0.15f);

        var whiteBalance = GetOrAdd<WhiteBalance>(profile);
        Override(whiteBalance.temperature, 12f);
        Override(whiteBalance.tint, 5f);

        var dof = GetOrAdd<DepthOfField>(profile);
        Override(dof.mode, DepthOfFieldMode.Bokeh);
        Override(dof.focusDistance, 5f);
        Override(dof.aperture, 5.6f);
        Override(dof.focalLength, 50f);
        Override(dof.bladeCount, 6);

        var chroma = GetOrAdd<ChromaticAberration>(profile);
        Override(chroma.intensity, 0.18f);

        var grain = GetOrAdd<FilmGrain>(profile);
        Override(grain.type, FilmGrainLookup.Thin1);
        Override(grain.intensity, 0.10f);

        var tonemap = GetOrAdd<Tonemapping>(profile);
        Override(tonemap.mode, TonemappingMode.ACES);

        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static void BindVolumeToScene(GameObject controllerGo, VolumeProfile profile)
    {
        var volumeGo = FindOrCreateChild(controllerGo, "PlantPostProcessVolume", typeof(Volume));
        var volume = volumeGo.GetComponent<Volume>();
        volume.isGlobal = true;
        volume.profile = profile;
        volume.priority = 0;
        volume.weight = 1f;

        var mainCam = Camera.main;
        if (mainCam != null)
        {
            var data = mainCam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
            }
        }
    }

    // ── Background video (RawImage in a Screen Space–Camera canvas) ─
    //
    // The video lives on a RawImage inside a Canvas behind the robot, NOT
    // on a 3D quad. This lets the user drop other UI canvases (logos,
    // text, buttons) on top of the video using normal Overlay canvases,
    // with the 3D robot rendering in between.
    //
    // Aspect handling is delegated to Unity's built-in AspectRatioFitter
    // in EnvelopeParent mode (= Cover): picks the right axis based on
    // clip vs screen aspect, no custom script needed.

    private static bool ConfigureBackgroundVideo(GameObject parent, Camera mainCam)
    {
        if (!File.Exists(VideoPath))
        {
            Debug.LogWarning($"[CreatePlantSplashVariant] No video at {VideoPath} — skipping video setup.");
            return false;
        }
        var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(VideoPath);
        if (clip == null)
        {
            AssetDatabase.ImportAsset(VideoPath);
            clip = AssetDatabase.LoadAssetAtPath<VideoClip>(VideoPath);
        }
        if (clip == null || mainCam == null) return false;

        const string rtPath = MaterialFolder + "/PlantBackgroundRT.renderTexture";

        // Clean up the old two-camera stack if it exists (older menu runs).
        DestroyChild(parent, "PlantBackgroundCamera");
        DestroyChild(parent, "PlantBackgroundQuad");
        DestroyChild(mainCam.gameObject, "PlantBackgroundCamera");
        DestroyChild(mainCam.gameObject, "PlantBackgroundQuad");

        var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(rtPath);
        if (rt == null)
        {
            rt = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32);
            rt.name = "PlantBackgroundRT";
            AssetDatabase.CreateAsset(rt, rtPath);
        }

        // VideoPlayer — renders the clip into the RenderTexture.
        var videoGo = FindOrCreateChild(parent, "PlantBackgroundVideo", typeof(VideoPlayer));
        var player = videoGo.GetComponent<VideoPlayer>();
        player.clip = clip;
        player.playOnAwake = true;
        player.isLooping = true;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = rt;
        player.audioOutputMode = VideoAudioOutputMode.None;
        player.skipOnDrop = true;
        player.waitForFirstFrame = true;
        // Stretch fills the RT regardless of source aspect. The RawImage's
        // AspectRatioFitter then sizes its rect to the source aspect so
        // any RT-aspect mismatch is undone on display.
        player.aspectRatio = VideoAspectRatio.Stretch;

        // Background canvas — Screen Space – Camera, far behind robot, low
        // sorting order so any user-added overlay canvas sits on top.
        var canvasGo = FindOrCreateChild(parent, "PlantBackgroundCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = mainCam;
        canvas.planeDistance = mainCam.farClipPlane * 0.5f;
        canvas.sortingOrder = -100;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var raycaster = canvasGo.GetComponent<GraphicRaycaster>();
        raycaster.enabled = false; // background — don't intercept clicks

        // RawImage — displays the RT. AspectRatioFitter handles fit.
        var imageGo = FindOrCreateChild(canvasGo, "PlantBackgroundImage",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(AspectRatioFitter));
        var rawImage = imageGo.GetComponent<RawImage>();
        rawImage.texture = rt;
        rawImage.raycastTarget = false;
        rawImage.color = Color.white;

        var rect = imageGo.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;

        var fitter = imageGo.GetComponent<AspectRatioFitter>();
        // EnvelopeParent = Cover: fills the parent rect, crops overflow.
        // Auto-picks the right axis based on aspect vs parent aspect.
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = (clip.height > 0)
            ? clip.width / (float)clip.height
            : 16f / 9f;

        // Main camera now clears with a warm fallback color (visible in the
        // unlikely case the video / canvas don't render). Robot + canvas
        // both go through normal URP rendering.
        mainCam.clearFlags = CameraClearFlags.SolidColor;
        mainCam.backgroundColor = new Color(0.08f, 0.06f, 0.04f);

        EditorUtility.SetDirty(player);
        EditorUtility.SetDirty(mainCam);
        EditorUtility.SetDirty(canvas);
        EditorUtility.SetDirty(rawImage);
        EditorUtility.SetDirty(fitter);

        Debug.Log($"[CreatePlantSplashVariant] Video wired: clip={clip.name} ({clip.width}x{clip.height}, aspect {fitter.aspectRatio:F2}), RT={rt.width}x{rt.height}, fit=EnvelopeParent (auto cover), Canvas planeDistance={canvas.planeDistance:F1}, sortingOrder={canvas.sortingOrder}. Add your own overlay canvases at sortingOrder > -100 to draw on top.");
        return true;
    }

    // ── Camera placement (in FRONT of robot — robot faces camera) ───

    private static void PositionCameraInFront(SplashSceneController ssc, Camera cam, Bounds bounds, float finalScale)
    {
        Vector3 center = bounds.center;
        Vector3 size = bounds.size;
        // Account for the bloom's final scale — robot ends 1.5× bigger than
        // bounds captured at scale 1, so the camera needs proportionally
        // more distance to frame it with the same padding.
        float scale = Mathf.Max(0.01f, finalScale);
        float maxDim = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * scale;
        if (maxDim < 0.01f) maxDim = 1f;

        const float fov = 42f;
        float halfFovRad = fov * 0.5f * Mathf.Deg2Rad;
        float paddedHeight = size.y * scale * 1.5f;
        float distance = (paddedHeight * 0.5f) / Mathf.Tan(halfFovRad);
        distance = Mathf.Max(distance, maxDim * 2.4f, 2.2f);

        Vector3 forward = ssc.robotRoot.forward;
        // IN FRONT (+forward) — robot faces camera throughout.
        cam.transform.position = center + forward * distance + Vector3.up * size.y * 0.05f;
        cam.transform.LookAt(center);
        cam.fieldOfView = fov;
        cam.allowHDR = true;

        // Wire awakening framing — subtle, since we don't need the big
        // "rotate to greet" beat anymore.
        var so = new SerializedObject(ssc);
        SetFloat(so, "cameraStartFov", fov);
        SetFloat(so, "cameraAwakeningFov", fov - 4f); // gentle tighten
        SetFloat(so, "cameraEndRise", size.y * 0.08f); // tiny rise
        SetFloat(so, "cameraDollyZ", 0f); // no dolly (no assembly)
        var lookOffsetProp = so.FindProperty("cameraLookOffset");
        if (lookOffsetProp != null) lookOffsetProp.vector3Value = center - ssc.robotRoot.position;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(ssc);
    }

    // ── Bloom-entrance configuration ────────────────────────────────
    //
    // Robot spawns at scale 0 at the flower-base point (lower-center,
    // matching where the Veo plant's glowing cavity sits), then grows to
    // 1.5× while lifting into the natural framing position. Replaces the
    // previous smooth-rise entrance.

    private static void ConfigureBloomEntrance(SplashSceneController ssc, Bounds bounds)
    {
        var so = new SerializedObject(ssc);
        var bloom = so.FindProperty("useBloomEntrance");
        if (bloom == null)
        {
            Debug.LogError("[CreatePlantSplashVariant] SplashSceneController is missing useBloomEntrance field — recompile scripts and re-run.");
            return;
        }
        bloom.boolValue = true;

        // Turn off the old smooth-rise so the two modes don't both try to
        // claim the entrance phase.
        var smooth = so.FindProperty("useSmoothEntrance");
        if (smooth != null) smooth.boolValue = false;

        // Bloom origin: ~0.6× robot height below the final position, so the
        // spawn point reads as "the flower base at lower-center of frame".
        // The bloom routine grows the robot UP from here into the final
        // pose, which gives the visual impression of emerging from the
        // flower's glowing cavity in the Veo video.
        var originProp = so.FindProperty("bloomEntranceOriginOffset");
        if (originProp != null)
        {
            float drop = Mathf.Max(0.5f, bounds.size.y * 0.6f);
            originProp.vector3Value = new Vector3(0f, -drop, 0f);
        }

        SetFloat(so, "bloomEntranceStartScale", 0f);
        SetFloat(so, "bloomEntranceEndScale", 1.5f);
        SetFloat(so, "bloomEntranceDuration", 1.6f);

        // Trim the phase timings that no longer apply (assembly), and give
        // the awakening + greeting beats a little more room since we removed
        // the dramatic 180° rotate.
        SetFloat(so, "preBuildupDelay", 0.4f);
        SetFloat(so, "postAssemblyHold", 0f);
        SetFloat(so, "rotateToCameraDuration", 0f);
        SetFloat(so, "powerOnDuration", 0.55f);
        SetFloat(so, "postAwakeningHold", 1.4f);
        SetFloat(so, "greetingDuration", 1.1f);

        // Greeting beat: knock then wave by default for the plant variant.
        // User can switch to KnockOnly or WaveOnly via the SplashController
        // dropdown without re-running the menu.
        var greetingModeProp = so.FindProperty("greetingMode");
        if (greetingModeProp != null)
        {
            greetingModeProp.enumValueIndex = (int)SplashSceneController.GreetingMode.KnockThenWave;
        }

        // Auto-find the right hand bone by name — falls back to the deepest
        // right-side limb if no exact match. User can override the assigned
        // transform in the inspector if the rig uses a different naming
        // convention.
        var knockHandProp = so.FindProperty("knockHand");
        if (knockHandProp != null && ssc.robotRoot != null)
        {
            var hand = FindHandBone(ssc.robotRoot);
            if (hand != null) knockHandProp.objectReferenceValue = hand;
            else Debug.LogWarning("[CreatePlantSplashVariant] Couldn't auto-find a right hand bone — assign SplashController.knockHand manually in the inspector.");
        }
        SetInt(so, "knockCount", 3);
        SetFloat(so, "knockReach", 0.30f);
        SetFloat(so, "knockBodyLean", 0.06f);
        SetFloat(so, "knockTapDuration", 0.22f);
        SetFloat(so, "knockStrikeHold", 0.04f);
        SetFloat(so, "knockBetweenTaps", 0.08f);
        SetFloat(so, "preKnockDelay", 0.2f);
        SetFloat(so, "cameraShakeAmount", 0.04f);
        SetFloat(so, "cameraShakeDuration", 0.15f);
        SetInt(so, "cameraShakeFreq", 22);
        SetFloat(so, "knockSoundVolume", 0.8f);

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(ssc);
    }

    // Walk the rig looking for a bone whose name matches common right-hand
    // conventions (Mixamo, Maya, Blender, generic). Returns the first match.
    private static Transform FindHandBone(Transform root)
    {
        string[] candidates = {
            "RightHand", "Right_Hand", "R_Hand", "RHand",
            "Hand_R", "hand_R", "hand_r", "hand.R", "Hand.R",
            "mixamorig:RightHand",
            "Bip01 R Hand", "B_R_Hand", "DEF-hand.R",
        };
        var all = root.GetComponentsInChildren<Transform>(includeInactive: true);
        foreach (var name in candidates)
        {
            foreach (var t in all)
            {
                if (t.name == name) return t;
            }
        }
        // Fallback: any transform whose lowercased name contains both
        // "hand" and a right-side marker.
        foreach (var t in all)
        {
            var n = t.name.ToLowerInvariant();
            if (!n.Contains("hand")) continue;
            if (n.Contains("right") || n.EndsWith("_r") || n.EndsWith(".r") || n.Contains("_r_") || n.Contains(" r ")) return t;
        }
        return null;
    }

    private static void SetInt(SerializedObject so, string propName, int value)
    {
        var p = so.FindProperty(propName);
        if (p != null) p.intValue = value;
    }

    private static void SetFloat(SerializedObject so, string propName, float value)
    {
        var p = so.FindProperty(propName);
        if (p != null) p.floatValue = value;
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet<T>(out var existing)) return existing;
        var component = profile.Add<T>(true);
        // CRITICAL: components must be registered as sub-assets of the
        // profile, otherwise they aren't serialized into the .asset file
        // and the profile shows zero overrides after a reload.
        component.hideFlags = HideFlags.HideInInspector;
        AssetDatabase.AddObjectToAsset(component, profile);
        EditorUtility.SetDirty(profile);
        return component;
    }

    private static void Override<T>(VolumeParameter<T> param, T value)
    {
        param.overrideState = true;
        param.value = value;
    }

    private static void DestroyChild(GameObject parent, string name)
    {
        var t = parent.transform.Find(name);
        if (t != null) Object.DestroyImmediate(t.gameObject);
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
