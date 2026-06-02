using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// Tools → ProjectW → 10. Create Logo Splash Variant
//
// Builds Splash_Logo.unity: dark studio gradient → Spirit Guardian
// fades in + waves (2s) → flies forward + covers screen → fade to
// black → bouncing lime circle takeover → cursive "w" write-on
// (sprite-sheet driven by Animator) → splash_done.
//
// Self-contained. Doesn't touch existing splash scenes/scripts.
public static class CreateLogoSplashVariant
{
    private const string SourcePath = "Assets/Scenes/Splash.unity";
    private const string DestPath = "Assets/Scenes/Splash_Logo.unity";
    private const string SpriteSheetPath = "Assets/Graphics/output-onlinegiftools.png";
    private const string SoftCirclePath = "Assets/Materials/SplashLimeAurora/SoftCircle.png";
    private const string AnimationFolder = "Assets/Animations/SplashLogo";
    private const string WriteOnClipPath = AnimationFolder + "/LogoWriteOn.anim";
    private const string LogoControllerPath = AnimationFolder + "/LogoWriteOn.controller";
    private const string VolumeFolder = "Assets/PostProcessing/Splash";
    private const string VolumeProfilePath = VolumeFolder + "/LogoSplashVolume.asset";
    private const string LogoNeonVideoPath = "Assets/Backgrounds/Logo_Neon.mp4";
    private const string GreenEnergyVideoPath = "Assets/Backgrounds/greenEnergy.mp4";
    private const string LogoMaterialFolder = "Assets/Materials/SplashLogo";
    private const string AdditiveMaterialPath = LogoMaterialFolder + "/UI_Additive.mat";
    private const string LogoNeonRTPath = LogoMaterialFolder + "/LogoNeonRT.renderTexture";
    private const string GreenEnergyRTPath = LogoMaterialFolder + "/GreenEnergyRT.renderTexture";

    // Re-apply ONLY the lighting on the open Splash_Logo scene. Disables
    // every inherited Light that isn't part of the StudioKey/Fill/Rim trio,
    // then reconfigures the studio lights + ambient. Preserves the saved
    // camera, robot pose, canvases, and SplashLogoController wiring.
    [MenuItem("Tools/ProjectW/10b. Refresh Logo Lighting (current scene)")]
    public static void RefreshLighting()
    {
        var active = EditorSceneManager.GetActiveScene();
        if (!active.path.EndsWith("Splash_Logo.unity"))
        {
            EditorUtility.DisplayDialog(
                "Refresh Logo Lighting",
                $"Open Splash_Logo.unity first.\n\nCurrently active: {(string.IsNullOrEmpty(active.path) ? "(untitled)" : active.path)}",
                "OK");
            return;
        }

        var splashGo = GameObject.Find("SplashController");
        if (splashGo == null)
        {
            EditorUtility.DisplayDialog("Refresh Logo Lighting", "SplashController GameObject missing.", "OK");
            return;
        }

        // Find the robot root from SplashLogoController so rim positioning
        // can target it (falls back to SplashController itself).
        Transform robotRoot = splashGo.transform;
        var slc = splashGo.GetComponent<SplashLogoController>();
        if (slc != null && slc.robotRoot != null) robotRoot = slc.robotRoot;

        EnsureFolder(VolumeFolder);

        int beforeActive = CountActiveLights();
        ConfigureStudioLighting(splashGo, robotRoot);
        int afterActive = CountActiveLights();

        var volumeProfile = BuildVolumeProfile();
        BindVolumeToScene(splashGo, volumeProfile, Camera.main);

        EditorSceneManager.MarkSceneDirty(active);
        EditorSceneManager.SaveScene(active);
        AssetDatabase.SaveAssets();

        int disabled = Mathf.Max(0, beforeActive - afterActive);
        Debug.Log($"[CreateLogoSplashVariant] Refresh complete. Disabled {disabled} inherited lights; StudioKey/Fill/Rim reconfigured.");
        EditorUtility.DisplayDialog(
            "Refresh Logo Lighting",
            $"Lighting refreshed.\n\n" +
            $"Disabled {disabled} inherited light(s) — anything that isn't named StudioKey, StudioFill, or StudioRim is now off.\n\n" +
            $"Reconfigured:\n" +
            $"  • StudioKey (cool-white directional from above)\n" +
            $"  • StudioFill (faint blue from below)\n" +
            $"  • StudioRim (green spot from behind robot)\n" +
            $"  • Trilight ambient (deep space — near-black with cool tint)\n" +
            $"  • LogoSplashVolume.asset (Bloom + ACES + Vignette + DoF + FilmGrain)\n\n" +
            $"Untouched: camera, robot pose, gradient canvas, overlay canvas, SplashLogoController.",
            "OK");
    }

    private static int CountActiveLights()
    {
        int count = 0;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (l.gameObject.activeInHierarchy) count++;
        }
        return count;
    }

    // Add the Logo_Neon + greenEnergy dual video reveal to the open
    // Splash_Logo scene without touching anything else (camera, robot,
    // lighting, lime/sprite-sheet reveal stay intact — just sets
    // revealMode = DualVideo so the videos play instead).
    [MenuItem("Tools/ProjectW/10c. Refresh Logo Video Reveal (current scene)")]
    public static void RefreshVideoReveal()
    {
        var active = EditorSceneManager.GetActiveScene();
        if (!active.path.EndsWith("Splash_Logo.unity"))
        {
            EditorUtility.DisplayDialog(
                "Refresh Logo Video Reveal",
                $"Open Splash_Logo.unity first.\n\nCurrently active: {(string.IsNullOrEmpty(active.path) ? "(untitled)" : active.path)}",
                "OK");
            return;
        }
        if (!File.Exists(LogoNeonVideoPath) || !File.Exists(GreenEnergyVideoPath))
        {
            EditorUtility.DisplayDialog(
                "Refresh Logo Video Reveal",
                $"Missing one or both videos:\n  {LogoNeonVideoPath}\n  {GreenEnergyVideoPath}\n\nDrop them in and re-run.",
                "OK");
            return;
        }

        var splashGo = GameObject.Find("SplashController");
        if (splashGo == null)
        {
            EditorUtility.DisplayDialog("Refresh Logo Video Reveal", "SplashController GameObject missing.", "OK");
            return;
        }
        var slc = splashGo.GetComponent<SplashLogoController>();
        if (slc == null)
        {
            EditorUtility.DisplayDialog("Refresh Logo Video Reveal", "SplashLogoController component missing.", "OK");
            return;
        }
        var overlay = splashGo.transform.Find("LogoOverlayCanvas");
        if (overlay == null)
        {
            EditorUtility.DisplayDialog("Refresh Logo Video Reveal", "LogoOverlayCanvas missing under SplashController — run menu 10 first to build the scene structure.", "OK");
            return;
        }

        EnsureFolder(LogoMaterialFolder);
        var (neonPlayer, neonImg, energyPlayer, energyImg) = ConfigureDualVideoReveal(overlay.gameObject);

        var so = new SerializedObject(slc);
        SetObject(so, "logoNeonPlayer", neonPlayer);
        SetObject(so, "logoNeonImage", neonImg);
        SetObject(so, "greenEnergyPlayer", energyPlayer);
        SetObject(so, "greenEnergyImage", energyImg);
        var revealModeProp = so.FindProperty("revealMode");
        if (revealModeProp != null)
        {
            revealModeProp.enumValueIndex = (int)SplashLogoController.LogoRevealMode.DualVideo;
        }
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(slc);

        EditorSceneManager.MarkSceneDirty(active);
        EditorSceneManager.SaveScene(active);
        AssetDatabase.SaveAssets();

        Debug.Log($"[CreateLogoSplashVariant] Dual-video reveal wired. revealMode = DualVideo. Lime circle + sprite-sheet write-on remain in the scene but are bypassed.");
        EditorUtility.DisplayDialog(
            "Refresh Logo Video Reveal",
            "Logo_Neon + greenEnergy wired as the reveal.\n\n" +
            "Setup:\n" +
            "  • LogoNeonVideo + GreenEnergyVideo GameObjects (VideoPlayers → RenderTextures)\n" +
            "  • Two RawImages on LogoOverlayCanvas, additive material\n" +
            "  • SplashLogoController.revealMode = DualVideo\n\n" +
            "Choose what plays via SplashLogoController.videoSelection:\n" +
            "  • Both — Logo_Neon + greenEnergy together (default)\n" +
            "  • LogoNeonOnly — just the dripping logo stroke\n" +
            "  • GreenEnergyOnly — just the energy burst\n\n" +
            "Timing: videos Prepare() in Awake (pre-buffered) and Play() the instant the black fade-in STARTS — so by the time the screen is fully black, the videos are mid-playback and reveal with zero delay. Toggle off via startVideosDuringFade if you want them to start after the fade.\n\n" +
            "Untouched: camera, robot, lighting, lime circle + sprite-sheet (switch back via the revealMode dropdown).",
            "OK");
    }

    [MenuItem("Tools/ProjectW/10. Create Logo Splash Variant")]
    public static void Build()
    {
        if (!File.Exists(SourcePath))
        {
            EditorUtility.DisplayDialog("Logo Splash", $"Source scene not found: {SourcePath}", "OK");
            return;
        }
        if (!File.Exists(SpriteSheetPath))
        {
            EditorUtility.DisplayDialog("Logo Splash", $"Sprite sheet not found: {SpriteSheetPath}", "OK");
            return;
        }

        if (EditorSceneManager.GetActiveScene().isDirty)
        {
            EditorSceneManager.SaveOpenScenes();
        }

        // Don't-overwrite protection — same pattern as the Plant variant.
        if (File.Exists(DestPath))
        {
            int choice = EditorUtility.DisplayDialogComplex(
                "Logo Splash",
                $"{DestPath} already exists and may contain saved tweaks.\n\n" +
                "Open without modifying — your saved changes are kept.\n" +
                "Recreate from scratch — wipes the scene and rebuilds.",
                "Open (keep my changes)",
                "Cancel",
                "Recreate from scratch");
            if (choice == 0) { EditorSceneManager.OpenScene(DestPath, OpenSceneMode.Single); return; }
            if (choice == 1) return;
            AssetDatabase.DeleteAsset(DestPath);
            AssetDatabase.Refresh();
        }

        EnsureFolder(AnimationFolder);
        EnsureFolder(VolumeFolder);
        EnsureSpriteImport(SoftCirclePath);

        // ── 1. Slice/build the write-on AnimationClip + Animator first
        var clip = BuildLogoWriteOnClip(out int frameCount, out float clipLength);
        if (clip == null)
        {
            EditorUtility.DisplayDialog("Logo Splash",
                $"Failed to load sprites from {SpriteSheetPath}. Make sure the sprite sheet is sliced (Multiple mode, 10×8 grid).",
                "OK");
            return;
        }
        var logoController = BuildLogoAnimatorController(clip);

        // ── 2. Duplicate Splash.unity → Splash_Logo.unity
        AssetDatabase.CopyAsset(SourcePath, DestPath);
        AssetDatabase.Refresh();
        var scene = EditorSceneManager.OpenScene(DestPath, OpenSceneMode.Single);

        // Find the Splash controller GO (used as parent for our new
        // structures, and we'll repurpose its robotRoot reference).
        var splashGo = GameObject.Find("SplashController");
        if (splashGo == null)
        {
            EditorUtility.DisplayDialog("Logo Splash", "SplashController GameObject missing in scene.", "OK");
            return;
        }

        // Find the existing robotRoot via the old SplashSceneController so
        // we can hand it to the new controller. Then remove the old
        // controller component so it doesn't run alongside ours.
        Transform robotRoot = null;
        var oldSsc = splashGo.GetComponent<SplashSceneController>();
        if (oldSsc != null)
        {
            robotRoot = oldSsc.robotRoot;
            Object.DestroyImmediate(oldSsc);
        }
        if (robotRoot == null) robotRoot = splashGo.transform; // fallback

        // ── 3. Strip any backgrounds inherited from Splash.unity
        StripInheritedBackgrounds(splashGo);

        // ── 4. Studio gradient background — black + two soft-circle glows
        var mainCam = Camera.main;
        BuildStudioGradient(splashGo, mainCam);

        // ── 5. Studio lighting — neutral key, faint fill, soft green rim
        ConfigureStudioLighting(splashGo, robotRoot);

        // ── 5b. URP Volume Profile — bloom (for the glow + rim), vignette,
        //       ACES tonemap, subtle film grain.
        var volumeProfile = BuildVolumeProfile();
        BindVolumeToScene(splashGo, volumeProfile, mainCam);

        // ── 6. UI overlay canvas (black + lime circle + logo image)
        var (blackOverlay, limeCircleRect, limeImage, logoImage, logoAnim) =
            BuildOverlayCanvas(splashGo, logoController);

        // ── 6b. Dual-video reveal — Logo_Neon + greenEnergy additive layer
        var (neonPlayer, neonImg, energyPlayer, energyImg) =
            ConfigureDualVideoReveal(blackOverlay.transform.parent.gameObject);

        // ── 7. Camera position — in front of the robot, facing it
        if (mainCam != null && robotRoot != null)
        {
            PositionCameraInFront(robotRoot, mainCam);
        }

        // ── 8. Wire the new SplashLogoController
        var slc = splashGo.GetComponent<SplashLogoController>();
        if (slc == null) slc = splashGo.AddComponent<SplashLogoController>();
        var so = new SerializedObject(slc);
        SetObject(so, "robotRoot", robotRoot);
        SetObject(so, "mainCamera", mainCam);
        SetObject(so, "blackOverlay", blackOverlay);
        SetObject(so, "limeCircle", limeCircleRect);
        SetObject(so, "limeCircleImage", limeImage);
        SetObject(so, "logoWriteOnImage", logoImage);
        SetObject(so, "logoAnimator", logoAnim);
        SetFloat(so, "logoWriteOnDuration", clipLength);
        SetObject(so, "logoNeonPlayer", neonPlayer);
        SetObject(so, "logoNeonImage", neonImg);
        SetObject(so, "greenEnergyPlayer", energyPlayer);
        SetObject(so, "greenEnergyImage", energyImg);
        // Default to DualVideo since both videos are present
        var revealModeProp = so.FindProperty("revealMode");
        if (revealModeProp != null)
        {
            revealModeProp.enumValueIndex = (int)SplashLogoController.LogoRevealMode.DualVideo;
        }
        // Auto-find Animator on the robot for the wave
        var robotAnim = robotRoot != null ? robotRoot.GetComponentInChildren<Animator>() : null;
        SetObject(so, "robotAnimator", robotAnim);
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(slc);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"[CreateLogoSplashVariant] Built {DestPath}. Write-on clip = {frameCount} frames, {clipLength:F2}s.");
        EditorUtility.DisplayDialog("Logo Splash",
            $"Splash_Logo.unity ready.\n\n" +
            $"Sequence:\n" +
            $"  1. Studio gradient (dark + top/bottom green glow)\n" +
            $"  2. Spirit Guardian fades in\n" +
            $"  3. Wave (2 seconds)\n" +
            $"  4. Fly forward + cover screen\n" +
            $"  5. Fade to black\n" +
            $"  6. Lime circle bounces from bottom-right, fills screen\n" +
            $"  7. Cursive \"w\" writes itself ({frameCount} frames, {clipLength:F2}s)\n" +
            $"  8. SendToFlutter(\"splash_done\") + load Bootstrap\n\n" +
            $"Lime color (SplashLogoController.limeColor) and bounce curve " +
            $"are exposed in the inspector — tune freely.",
            "OK");
    }

    // ── Sprite-sheet → AnimationClip ────────────────────────────────

    private static AnimationClip BuildLogoWriteOnClip(out int frameCount, out float clipLength)
    {
        frameCount = 0;
        clipLength = 0f;

        // Load all sub-sprites
        var assets = AssetDatabase.LoadAllAssetsAtPath(SpriteSheetPath);
        var sprites = new List<Sprite>();
        foreach (var a in assets)
        {
            if (a is Sprite s) sprites.Add(s);
        }
        if (sprites.Count == 0)
        {
            Debug.LogError("[CreateLogoSplashVariant] Sprite sheet has no sliced sprites. Open the Sprite Editor, hit Slice with 10×8 grid (550×550), Apply.");
            return null;
        }

        // Sort by the trailing _N suffix
        sprites.Sort((a, b) =>
        {
            int ai = ParseTrailingIndex(a.name);
            int bi = ParseTrailingIndex(b.name);
            return ai.CompareTo(bi);
        });

        frameCount = sprites.Count;
        const float fps = 30f;
        clipLength = frameCount / fps;

        var clip = new AnimationClip { frameRate = fps };
        var binding = new EditorCurveBinding
        {
            path = "",
            propertyName = "m_Sprite",
            type = typeof(Image)
        };
        var keyframes = new ObjectReferenceKeyframe[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            keyframes[i] = new ObjectReferenceKeyframe
            {
                time = i / fps,
                value = sprites[i]
            };
        }
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

        // One-shot: don't loop
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        AssetDatabase.CreateAsset(clip, WriteOnClipPath);
        EditorUtility.SetDirty(clip);
        return AssetDatabase.LoadAssetAtPath<AnimationClip>(WriteOnClipPath);
    }

    private static int ParseTrailingIndex(string name)
    {
        int u = name.LastIndexOf('_');
        if (u < 0 || u == name.Length - 1) return 0;
        return int.TryParse(name.Substring(u + 1), out var n) ? n : 0;
    }

    private static AnimatorController BuildLogoAnimatorController(AnimationClip clip)
    {
        // Always recreate so the state graph matches the current clip
        if (File.Exists(LogoControllerPath)) AssetDatabase.DeleteAsset(LogoControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(LogoControllerPath);

        // Idle (empty) → Write (clip) on trigger Play
        var sm = controller.layers[0].stateMachine;
        var idle = sm.AddState("Idle");
        idle.motion = null;
        var write = sm.AddState("Write");
        write.motion = clip;
        sm.defaultState = idle;

        controller.AddParameter("Play", AnimatorControllerParameterType.Trigger);

        var trans = idle.AddTransition(write);
        trans.AddCondition(AnimatorConditionMode.If, 0, "Play");
        trans.hasExitTime = false;
        trans.duration = 0f;

        EditorUtility.SetDirty(controller);
        return controller;
    }

    // ── Studio gradient (black base + soft top/bottom green glow) ───

    private static void BuildStudioGradient(GameObject parent, Camera mainCam)
    {
        if (mainCam != null)
        {
            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = new Color(0.015f, 0.02f, 0.018f); // near-black
            mainCam.allowHDR = true;
        }

        // Background canvas, Screen Space–Camera, behind the robot
        var canvasGo = FindOrCreateChild(parent, "StudioGradientCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = mainCam;
        if (mainCam != null) canvas.planeDistance = mainCam.farClipPlane * 0.5f;
        canvas.sortingOrder = -100;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var raycaster = canvasGo.GetComponent<GraphicRaycaster>();
        raycaster.enabled = false;

        var softCircle = AssetDatabase.LoadAssetAtPath<Sprite>(SoftCirclePath);
        Color glowColor = new Color(0.18f, 0.32f, 0.20f, 0.9f); // muted forest green

        BuildGlow(canvasGo, "TopGlow", softCircle, glowColor,
            anchorMin: new Vector2(0.5f, 1f), anchorMax: new Vector2(0.5f, 1f),
            pivot: new Vector2(0.5f, 0.5f),
            anchored: Vector2.zero, size: new Vector2(2400f, 900f));
        BuildGlow(canvasGo, "BottomGlow", softCircle, glowColor,
            anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f),
            pivot: new Vector2(0.5f, 0.5f),
            anchored: Vector2.zero, size: new Vector2(2400f, 900f));
    }

    private static void BuildGlow(GameObject canvasGo, string name, Sprite sprite, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchored, Vector2 size)
    {
        var go = FindOrCreateChild(canvasGo, name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchored;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        if (sprite != null) img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        img.preserveAspect = false;
    }

    // ── Studio lighting ─────────────────────────────────────────────

    private static void ConfigureStudioLighting(GameObject parent, Transform robotRoot)
    {
        // Disable EVERY scene light that isn't one of our three studio
        // lights. Broader than the previous named-list approach — handles
        // RimLight, BounceFillLight, Point Light, FlashLight, etc., from
        // the inherited Splash.unity regardless of naming.
        var ourLightNames = new HashSet<string> { "StudioKey", "StudioFill", "StudioRim" };
        var allLights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var l in allLights)
        {
            if (ourLightNames.Contains(l.gameObject.name)) continue;
            if (l.gameObject.activeSelf) l.gameObject.SetActive(false);
        }

        // Key — neutral white-cool directional from above
        var keyGo = FindOrCreateChild(parent, "StudioKey", typeof(Light));
        keyGo.transform.rotation = Quaternion.Euler(55f, 15f, 0f);
        var key = keyGo.GetComponent<Light>();
        key.type = LightType.Directional;
        key.color = new Color(0.95f, 0.97f, 1.0f);
        key.intensity = 1.0f;
        key.shadows = LightShadows.Soft;
        key.shadowStrength = 0.7f;

        // Fill — very faint cool from below
        var fillGo = FindOrCreateChild(parent, "StudioFill", typeof(Light));
        fillGo.transform.rotation = Quaternion.Euler(-25f, 180f, 0f);
        var fill = fillGo.GetComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = new Color(0.5f, 0.6f, 0.75f);
        fill.intensity = 0.18f;
        fill.shadows = LightShadows.None;

        // Rim — subtle green from behind to echo the gradient glow
        var rimGo = FindOrCreateChild(parent, "StudioRim", typeof(Light));
        Vector3 rimPos = robotRoot != null
            ? robotRoot.position + new Vector3(0f, 1.0f, 2.5f)
            : new Vector3(0f, 1.0f, 2.5f);
        rimGo.transform.position = rimPos;
        if (robotRoot != null) rimGo.transform.LookAt(robotRoot.position);
        var rim = rimGo.GetComponent<Light>();
        rim.type = LightType.Spot;
        rim.color = new Color(0.5f, 0.95f, 0.45f);
        rim.intensity = 6f;
        rim.range = 8f;
        rim.spotAngle = 50f;
        rim.innerSpotAngle = 25f;
        rim.shadows = LightShadows.None;

        // Dim ambient — feels like deep space
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.08f, 0.10f, 0.10f);
        RenderSettings.ambientEquatorColor = new Color(0.04f, 0.05f, 0.05f);
        RenderSettings.ambientGroundColor = new Color(0.01f, 0.015f, 0.01f);
        RenderSettings.fog = false;
    }

    // ── UI overlay (black + lime circle + logo image) ───────────────

    private static (Image blackOverlay, RectTransform limeRect, Image limeImage, Image logoImage, Animator logoAnim)
        BuildOverlayCanvas(GameObject parent, AnimatorController logoController)
    {
        var canvasGo = FindOrCreateChild(parent, "LogoOverlayCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.GetComponent<GraphicRaycaster>().enabled = false;

        // Black full-screen overlay (covers robot for fade)
        var blackGo = FindOrCreateChild(canvasGo, "BlackOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var blackRt = blackGo.GetComponent<RectTransform>();
        Stretch(blackRt);
        var blackImg = blackGo.GetComponent<Image>();
        blackImg.color = new Color(0f, 0f, 0f, 0f);
        blackImg.raycastTarget = false;

        // Lime circle — child of canvas, anchored bottom-right initially
        var limeGo = FindOrCreateChild(canvasGo, "LimeCircle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var limeRt = limeGo.GetComponent<RectTransform>();
        limeRt.anchorMin = new Vector2(1f, 0f);
        limeRt.anchorMax = new Vector2(1f, 0f);
        limeRt.pivot = new Vector2(0.5f, 0.5f);
        limeRt.anchoredPosition = new Vector2(-200f, 250f);
        limeRt.sizeDelta = new Vector2(200f, 200f);
        var limeImg = limeGo.GetComponent<Image>();
        var softCircle = AssetDatabase.LoadAssetAtPath<Sprite>(SoftCirclePath);
        if (softCircle != null) limeImg.sprite = softCircle;
        limeImg.preserveAspect = true;
        limeImg.color = new Color(0.78f, 1f, 0.32f, 1f); // brand lime
        limeImg.raycastTarget = false;
        limeGo.SetActive(false);

        // Logo write-on image — centered, square, gets sprite-driven by Animator
        var logoGo = FindOrCreateChild(canvasGo, "LogoWriteOn",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Animator));
        var logoRt = logoGo.GetComponent<RectTransform>();
        logoRt.anchorMin = new Vector2(0.5f, 0.5f);
        logoRt.anchorMax = new Vector2(0.5f, 0.5f);
        logoRt.pivot = new Vector2(0.5f, 0.5f);
        logoRt.anchoredPosition = Vector2.zero;
        logoRt.sizeDelta = new Vector2(900f, 900f);
        var logoImg = logoGo.GetComponent<Image>();
        logoImg.preserveAspect = true;
        logoImg.raycastTarget = false;
        // Default to the first sliced sprite so the inspector preview isn't blank
        var assets = AssetDatabase.LoadAllAssetsAtPath(SpriteSheetPath);
        foreach (var a in assets)
        {
            if (a is Sprite s && s.name.EndsWith("_0"))
            {
                logoImg.sprite = s;
                break;
            }
        }
        var logoAnim = logoGo.GetComponent<Animator>();
        logoAnim.runtimeAnimatorController = logoController;
        logoAnim.applyRootMotion = false;
        logoAnim.updateMode = AnimatorUpdateMode.Normal;
        logoAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        logoGo.SetActive(false);

        return (blackImg, limeRt, limeImg, logoImg, logoAnim);
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // ── Strip inherited bgs from Splash.unity duplicate ─────────────

    private static void StripInheritedBackgrounds(GameObject parent)
    {
        foreach (var n in new[] {
            "SplashBackgroundVideo", "SplashBackgroundQuad", "SplashBackgroundCamera",
            "BrandBackground", "PlantBackgroundVideo", "PlantBackgroundCanvas",
            "PlantBackgroundCamera", "PlantBackgroundQuad", "PlantPostProcessVolume"
        })
        {
            var t = parent.transform.Find(n);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }
        var mainCam = Camera.main;
        if (mainCam != null)
        {
            foreach (var n in new[] {
                "SplashBackgroundQuad", "SplashBackgroundCamera", "BrandBackground",
                "PlantBackgroundCamera", "PlantBackgroundQuad"
            })
            {
                var t = mainCam.transform.Find(n);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }
        }
    }

    // ── Camera placement ────────────────────────────────────────────

    private static void PositionCameraInFront(Transform robotRoot, Camera cam)
    {
        var bounds = ComputeRendererBounds(robotRoot);
        Vector3 center = bounds.center;
        Vector3 size = bounds.size;
        const float fov = 38f;
        float halfFov = fov * 0.5f * Mathf.Deg2Rad;
        float paddedHeight = Mathf.Max(0.5f, size.y) * 1.8f;
        float distance = (paddedHeight * 0.5f) / Mathf.Tan(halfFov);
        distance = Mathf.Max(distance, 2.4f);

        Vector3 forward = robotRoot.forward;
        cam.transform.position = center + forward * distance + Vector3.up * size.y * 0.08f;
        cam.transform.LookAt(center);
        cam.fieldOfView = fov;
        cam.allowHDR = true;
    }

    private static Bounds ComputeRendererBounds(Transform root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(root.position, Vector3.one);
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    // ── Dual video reveal (Logo_Neon + greenEnergy, additive) ───────
    //
    // Two VideoPlayers, each rendering to its own RenderTexture, displayed
    // by RawImages on the overlay canvas with an additive material so each
    // video's black background contributes nothing and the green content
    // composites cleanly over the full-black BlackOverlay underneath.
    private static (UnityEngine.Video.VideoPlayer neonPlayer, RawImage neonImg,
                    UnityEngine.Video.VideoPlayer energyPlayer, RawImage energyImg)
        ConfigureDualVideoReveal(GameObject overlayCanvas)
    {
        EnsureFolder(LogoMaterialFolder);
        var additiveMat = EnsureAdditiveMaterial();

        var neon = BuildVideoLayer(overlayCanvas, "LogoNeonVideo", LogoNeonVideoPath, LogoNeonRTPath, additiveMat, sortingHint: 1);
        var energy = BuildVideoLayer(overlayCanvas, "GreenEnergyVideo", GreenEnergyVideoPath, GreenEnergyRTPath, additiveMat, sortingHint: 2);

        return (neon.player, neon.img, energy.player, energy.img);
    }

    private static (UnityEngine.Video.VideoPlayer player, RawImage img)
        BuildVideoLayer(GameObject canvasGo, string name, string videoPath, string rtPath, Material material, int sortingHint)
    {
        var clip = AssetDatabase.LoadAssetAtPath<UnityEngine.Video.VideoClip>(videoPath);
        if (clip == null)
        {
            Debug.LogWarning($"[CreateLogoSplashVariant] Video missing: {videoPath} — layer '{name}' will be wired but won't play until the file is in place.");
        }

        int w = clip != null ? (int)clip.width : 1080;
        int h = clip != null ? (int)clip.height : 1920;

        // RenderTexture asset (persists, reused on re-run)
        var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(rtPath);
        if (rt == null)
        {
            rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            rt.name = name + "RT";
            AssetDatabase.CreateAsset(rt, rtPath);
        }

        // VideoPlayer GO (child of canvas — simplest place to park it)
        var playerGo = FindOrCreateChild(canvasGo, name, typeof(UnityEngine.Video.VideoPlayer));
        var player = playerGo.GetComponent<UnityEngine.Video.VideoPlayer>();
        player.clip = clip;
        player.playOnAwake = false; // controller calls Play() explicitly
        player.isLooping = false;
        player.renderMode = UnityEngine.Video.VideoRenderMode.RenderTexture;
        player.targetTexture = rt;
        player.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.None;
        player.skipOnDrop = true;
        player.waitForFirstFrame = true;
        player.aspectRatio = UnityEngine.Video.VideoAspectRatio.Stretch;

        // RawImage display — full-screen, additive blend
        var imgGo = FindOrCreateChild(canvasGo, name + "Image",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(AspectRatioFitter));
        var rect = imgGo.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        var fitter = imgGo.GetComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = h > 0 ? (float)w / h : 9f / 16f;
        var img = imgGo.GetComponent<RawImage>();
        img.texture = rt;
        img.material = material;
        img.raycastTarget = false;
        img.color = Color.white;
        // Set the sibling order so the videos draw above BlackOverlay and
        // above each other in a predictable order.
        imgGo.transform.SetSiblingIndex(sortingHint + 1);
        imgGo.SetActive(false); // controller activates when reveal starts

        EditorUtility.SetDirty(player);
        EditorUtility.SetDirty(img);
        return (player, img);
    }

    private static Material EnsureAdditiveMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(AdditiveMaterialPath);
        if (mat != null) return mat;
        // UI/Default supports the standard blend properties — set them to
        // additive (One, One). _ZWrite stays 0 (UI doesn't write depth).
        var shader = Shader.Find("UI/Default");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        mat = new Material(shader) { name = "UI_Additive" };
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_ZWrite", 0);
        AssetDatabase.CreateAsset(mat, AdditiveMaterialPath);
        return mat;
    }

    // ── URP Volume Profile — studio look ────────────────────────────
    //
    // Bloom is the headline effect: the top/bottom green glows + the
    // green StudioRim all read flat without it. Threshold is low and
    // scatter is high so the soft falloff reads as atmospheric haze
    // rather than a hot spot. ACES tonemap + subtle vignette frame the
    // dark studio composition.

    private static VolumeProfile BuildVolumeProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        // Wipe a previously broken profile (components weren't registered
        // as sub-assets) so we don't carry an empty overrides list forward.
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

        var bloom = GetOrAdd<Bloom>(profile);
        Override(bloom.intensity, 1.8f);
        Override(bloom.threshold, 0.70f);
        Override(bloom.scatter, 0.70f);
        Override(bloom.tint, new Color(0.85f, 1f, 0.80f)); // slight green so the rim/glow read warmly

        var vignette = GetOrAdd<Vignette>(profile);
        Override(vignette.intensity, 0.30f);
        Override(vignette.smoothness, 0.55f);
        Override(vignette.color, new Color(0f, 0f, 0f));

        var colorAdj = GetOrAdd<ColorAdjustments>(profile);
        Override(colorAdj.contrast, 15f);
        Override(colorAdj.saturation, -5f); // slightly desaturated for the "deep space" feel
        Override(colorAdj.colorFilter, new Color(0.96f, 1f, 0.98f));
        Override(colorAdj.postExposure, 0.05f);

        var whiteBalance = GetOrAdd<WhiteBalance>(profile);
        Override(whiteBalance.temperature, -8f); // slight cool shift
        Override(whiteBalance.tint, 0f);

        var dof = GetOrAdd<DepthOfField>(profile);
        Override(dof.mode, DepthOfFieldMode.Bokeh);
        Override(dof.focusDistance, 4.5f);
        Override(dof.aperture, 6.3f);
        Override(dof.focalLength, 45f);
        Override(dof.bladeCount, 6);

        var chroma = GetOrAdd<ChromaticAberration>(profile);
        Override(chroma.intensity, 0.10f);

        var grain = GetOrAdd<FilmGrain>(profile);
        Override(grain.type, FilmGrainLookup.Thin1);
        Override(grain.intensity, 0.10f);

        var tonemap = GetOrAdd<Tonemapping>(profile);
        Override(tonemap.mode, TonemappingMode.ACES);

        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static void BindVolumeToScene(GameObject parent, VolumeProfile profile, Camera mainCam)
    {
        var volumeGo = FindOrCreateChild(parent, "LogoPostProcessVolume", typeof(Volume));
        var volume = volumeGo.GetComponent<Volume>();
        volume.isGlobal = true;
        volume.profile = profile;
        volume.priority = 0;
        volume.weight = 1f;

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

    private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet<T>(out var existing)) return existing;
        var component = profile.Add<T>(true);
        // CRITICAL: register as sub-asset or the override list reloads empty.
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

    // ── Helpers ─────────────────────────────────────────────────────

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

    private static void SetObject(SerializedObject so, string propName, Object value)
    {
        var p = so.FindProperty(propName);
        if (p != null) p.objectReferenceValue = value;
    }

    private static void SetFloat(SerializedObject so, string propName, float value)
    {
        var p = so.FindProperty(propName);
        if (p != null) p.floatValue = value;
    }

    private static void SetInt(SerializedObject so, string propName, int value)
    {
        var p = so.FindProperty(propName);
        if (p != null) p.intValue = value;
    }

    // Force the texture at `path` to be imported as Sprite (Single) so it
    // can be assigned to an Image component. Idempotent — re-imports only
    // when settings actually differ. Used for SoftCircle.png which ships
    // as a Default texture.
    private static void EnsureSpriteImport(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;
        bool dirty = false;
        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            dirty = true;
        }
        if (importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.spriteImportMode = SpriteImportMode.Single;
            dirty = true;
        }
        if (importer.alphaIsTransparency != true)
        {
            importer.alphaIsTransparency = true;
            dirty = true;
        }
        if (dirty)
        {
            importer.SaveAndReimport();
        }
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
