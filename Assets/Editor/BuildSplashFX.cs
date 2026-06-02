using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.Video;

// Tools → ProjectW → 3. Build Splash Scene FX
//
// Operates on the currently open scene (assumed to be Splash.unity with
// a SplashController GameObject that already has SplashSceneController
// attached and Part Trajectories wired). Creates:
//   * 3 ParticleSystem children (EnergyRing, AssemblySparks, AwakeningFlash)
//     with full module config + URP-compatible additive materials
//   * RimLight (spot light behind robot)
//   * Reasonable camera defaults (FOV 45)
// Then wires all of them as references on the SplashSceneController.
//
// Idempotent: re-running it skips any FX child that already exists.
public static class BuildSplashFX
{
    private const string MaterialFolder = "Assets/Materials/Splash";
    private const string SplashBackgroundVideoPath =
        "Assets/vecteezy_animated-view-through-futuristic-tunnel-with-teal-neon-lights_77190434.mp4";

    [MenuItem("Tools/ProjectW/3. Build Splash Scene FX")]
    public static void Build()
    {
        var controllerGo = GameObject.Find("SplashController");
        if (controllerGo == null)
        {
            EditorUtility.DisplayDialog(
                "Splash FX",
                "Couldn't find a GameObject named 'SplashController' in the open scene. " +
                "Add SplashSceneController to a GameObject called 'SplashController' first, " +
                "then re-run this menu item.",
                "OK"
            );
            return;
        }
        var controller = controllerGo.GetComponent<SplashSceneController>();
        if (controller == null)
        {
            EditorUtility.DisplayDialog(
                "Splash FX",
                "'SplashController' GameObject is missing the SplashSceneController component.",
                "OK"
            );
            return;
        }

        EnsureFolder(MaterialFolder);

        var energyMat = LoadOrCreateAdditiveMaterial(
            $"{MaterialFolder}/EnergyRingMat.mat", new Color(0.2f, 0.9f, 1f));
        var flashMat = LoadOrCreateAdditiveMaterial(
            $"{MaterialFolder}/AwakeningFlashMat.mat", Color.white);

        // ── 1. EnergyRing ─────────────────────────────────────────────
        var energyGo = FindOrCreateChild(controllerGo, "EnergyRing", typeof(ParticleSystem));
        var energy = energyGo.GetComponent<ParticleSystem>();
        if (!energy) { Debug.LogError("[BuildSplashFX] EnergyRing missing ParticleSystem"); return; }
        ConfigureEnergyRing(energy, energyMat);

        // Clean up any leftover AssemblySparks GameObject from earlier
        // versions (sparks were removed — they didn't read well visually)
        var staleSparks = controllerGo.transform.Find("AssemblySparks");
        if (staleSparks != null) Object.DestroyImmediate(staleSparks.gameObject);

        // ── 3. AwakeningFlash ─────────────────────────────────────────
        var flashGo = FindOrCreateChild(controllerGo, "AwakeningFlash", typeof(ParticleSystem));
        var awakening = flashGo.GetComponent<ParticleSystem>();
        if (!awakening) { Debug.LogError("[BuildSplashFX] AwakeningFlash missing ParticleSystem"); return; }
        ConfigureAwakeningFlash(awakening, flashMat);

        // ── 4. RimLight ───────────────────────────────────────────────
        var rimGo = FindOrCreateChild(controllerGo, "RimLight", typeof(Light));
        var rim = rimGo.GetComponent<Light>();
        if (!rim) { Debug.LogError("[BuildSplashFX] RimLight missing Light"); return; }
        ConfigureRimLight(rim);

        // ── 5. Camera + FX positioning (based on robot's actual bounds) ─
        var cam = controller.targetCamera != null ? controller.targetCamera : Camera.main;
        PositionCameraAndFX(controller, energyGo, flashGo, rimGo, cam);

        // ── 5b. Background video (tunnel) — RenderTexture + back-quad ──
        var bgQuadGo = ConfigureSplashBackgroundVideo(controllerGo, cam);

        // ── 5c. Fade-to-black UI overlay (used right before splash_done) ─
        var fadeImage = ConfigureFadeToBlackOverlay(controllerGo);

        // ── 6. Ensure the robot has an Animator + isolate the controller ──
        Animator robotAnim = null;
        if (controller.robotRoot != null)
        {
            robotAnim = controller.robotRoot.GetComponentInChildren<Animator>();
            if (robotAnim == null)
            {
                robotAnim = controller.robotRoot.gameObject.AddComponent<Animator>();
                Debug.Log("[BuildSplashFX] No Animator found on robot — added one to robotRoot.");
            }
            IsolateAnimatorController(robotAnim);
        }

        // ── 7. Wire references on SplashSceneController ───────────────
        var so = new SerializedObject(controller);
        so.FindProperty("energyRing").objectReferenceValue = energy;
        so.FindProperty("awakeningFlash").objectReferenceValue = awakening;
        so.FindProperty("rimLight").objectReferenceValue = rim;
        if (so.FindProperty("targetCamera").objectReferenceValue == null && cam != null)
        {
            so.FindProperty("targetCamera").objectReferenceValue = cam;
        }
        var animProp = so.FindProperty("robotAnimator");
        if (animProp != null && robotAnim != null)
        {
            animProp.objectReferenceValue = robotAnim;
        }
        // Auto-wire EmotionChanger (drives Rob11's eye/mouth texture
        // expression — same component Rob11Scene uses for smiles)
        var emoProp = so.FindProperty("emotionChanger");
        if (emoProp != null && controller.robotRoot != null && emoProp.objectReferenceValue == null)
        {
            var emo = controller.robotRoot.GetComponentInChildren<EmotionChanger>();
            if (emo != null) emoProp.objectReferenceValue = emo;
        }
        // Override the awakening glow to a softer yellow (matches Rob11's
        // eye-light style — cyan-blue at high HDR intensity reads as
        // overexposed white blobs)
        var emissionColorProp = so.FindProperty("powerOnEmissionColor");
        if (emissionColorProp != null) emissionColorProp.colorValue = new Color(1f, 0.85f, 0.2f);
        var emissionIntensityProp = so.FindProperty("powerOnEmissionIntensity");
        if (emissionIntensityProp != null) emissionIntensityProp.floatValue = 1.2f;
        // Force phase timings — total sequence ~6s (slowed down from 4s).
        SetFloat(so, "oneAtATimeFlightDuration", 0.4f);
        SetFloat(so, "oneAtATimeSettleDuration", 0.1f);
        SetFloat(so, "oneAtATimeGap", 0.04f);
        SetFloat(so, "preBuildupDelay", 0.15f);
        SetFloat(so, "buildupDuration", 0.35f);
        SetFloat(so, "flashDuration", 0.3f);
        SetFloat(so, "postAssemblyHold", 0.2f);
        SetFloat(so, "rotateToCameraDuration", 0.5f);
        SetFloat(so, "powerOnDuration", 0.4f);
        SetFloat(so, "postAwakeningHold", 1.2f);
        SetFloat(so, "greetingDuration", 0.9f);
        SetFloat(so, "fadeToBlackDuration", 0.4f);
        SetFloat(so, "cameraEndRise", 1.2f);
        var fadeProp = so.FindProperty("fadeToBlackImage");
        if (fadeProp != null && fadeImage != null) fadeProp.objectReferenceValue = fadeImage;
        var oneProp = so.FindProperty("oneAtATime");
        if (oneProp != null) oneProp.boolValue = true;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(controller);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        Debug.Log("[BuildSplashFX] Done. EnergyRing, AssemblySparks, AwakeningFlash, RimLight created and wired.");
        EditorUtility.DisplayDialog(
            "Splash FX",
            "Done. EnergyRing + AssemblySparks + AwakeningFlash + RimLight are children of SplashController and wired into the controller.\n\n" +
            "Press Play to see the cinematic.",
            "OK"
        );
    }

    // ── Particle system configurations ────────────────────────────────

    private static void ConfigureEnergyRing(ParticleSystem ps, Material mat)
    {
        var main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.startLifetime = 1.5f;
        main.startSpeed = 0f;
        main.startSize = 0.1f;
        main.startColor = new Color(0.2f, 0.9f, 1f, 1f);
        main.playOnAwake = false;
        main.maxParticles = 200;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 40f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.5f;
        shape.radiusThickness = 0f; // emit from edge
        shape.arc = 360f;
        shape.arcMode = ParticleSystemShapeMultiModeValue.Random;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        var sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.2f),
            new Keyframe(1f, 4f)
        );
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] {
                new GradientColorKey(new Color(0.4f, 1f, 1f), 0f),
                new GradientColorKey(new Color(0.2f, 0.6f, 1f), 1f),
            },
            new[] {
                new GradientAlphaKey(0.8f, 0f),
                new GradientAlphaKey(0f, 1f),
            }
        );
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        var trails = ps.trails;
        trails.enabled = true;
        trails.lifetime = 0.6f;
        trails.colorOverTrail = new ParticleSystem.MinMaxGradient(gradient);
        trails.widthOverTrail = new ParticleSystem.MinMaxCurve(0.05f);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = mat;
        renderer.trailMaterial = mat;
    }

    private static void ConfigureAwakeningFlash(ParticleSystem ps, Material mat)
    {
        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.startLifetime = 0.4f;
        main.startSpeed = 0f;
        main.startSize = 2f;
        main.startColor = Color.white;
        main.playOnAwake = false;
        main.maxParticles = 5;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] {
            new ParticleSystem.Burst(0f, 1)
        });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.01f;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        var sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.5f),
            new Keyframe(0.3f, 1.5f),
            new Keyframe(1f, 0f)
        );
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(0.3f, 0.9f, 1f), 1f),
            },
            new[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0f, 1f),
            }
        );
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = mat;
    }

    private static void ConfigureRimLight(Light light)
    {
        light.type = LightType.Spot;
        light.color = new Color(0.3f, 0.8f, 1f);
        light.intensity = 8f;
        light.range = 8f;
        light.spotAngle = 35f;
        light.innerSpotAngle = 20f;
        light.shadows = LightShadows.Soft;
        light.renderMode = LightRenderMode.ForcePixel;
    }

    // Camera is placed BEHIND the robot (along -robot.forward) so the
    // assembly happens with the robot's back to camera. The robot then
    // rotates 180° at the rotateToCameraDuration phase to face camera,
    // followed by the awakening (eye-light) phase.
    private static void PositionCameraAndFX(
        SplashSceneController controller,
        GameObject energyGo,
        GameObject flashGo,
        GameObject rimGo,
        Camera cam)
    {
        if (controller.robotRoot == null) return;

        Bounds bounds = ComputeRendererBounds(controller.robotRoot);
        Vector3 center = bounds.center;
        Vector3 size = bounds.size;
        float maxDim = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        if (maxDim < 0.01f) maxDim = 1f;

        Vector3 forward = controller.robotRoot.forward;

        // Distance: tighter framing so the robot dominates the screen.
        // paddedHeight 1.4× means the robot fills ~70% of vertical viewport.
        // Floored at maxDim × 2.2 so very wide robots still fit horizontally.
        const float fov = 45f;
        float halfFovRad = fov * 0.5f * Mathf.Deg2Rad;
        float paddedHeight = size.y * 1.4f;
        float distance = (paddedHeight * 0.5f) / Mathf.Tan(halfFovRad);
        distance = Mathf.Max(distance, maxDim * 2.2f, 2f);

        // CAMERA BEHIND ROBOT (-forward). Assembly happens with the
        // robot's back to camera; the rotate-to-face-camera phase swings
        // it around 180° to greet the viewer.
        if (cam != null)
        {
            cam.transform.position = center - forward * distance + Vector3.up * size.y * 0.05f;
            cam.transform.LookAt(center);
            cam.fieldOfView = fov;
            cam.allowHDR = true;
        }

        // Wire the camera-end framing on the controller so it's safe for
        // ANY robot size: rise scales with robot height, LookAt aims at
        // the renderer center (not the robot's pivot, which is often at
        // the feet — that would tilt the camera way too far down).
        var so = new SerializedObject(controller);
        var dollyProp = so.FindProperty("cameraDollyZ");
        if (dollyProp != null) dollyProp.floatValue = 0f; // no dolly — was making the final shot too tight
        var endRiseProp = so.FindProperty("cameraEndRise");
        if (endRiseProp != null) endRiseProp.floatValue = size.y * 0.25f; // subtle high-angle, scaled to robot
        var lookOffsetProp = so.FindProperty("cameraLookOffset");
        if (lookOffsetProp != null && controller.robotRoot != null)
        {
            // Convert the bounds.center (world) into an offset from
            // robotRoot.position, so LookAt(robotRoot.position + offset)
            // hits the visual center regardless of pivot location.
            lookOffsetProp.vector3Value = center - controller.robotRoot.position;
        }
        var awakeningFovProp = so.FindProperty("cameraAwakeningFov");
        if (awakeningFovProp != null) awakeningFovProp.floatValue = 40f; // less aggressive than 35°
        so.ApplyModifiedProperties();

        // Energy ring sits at the robot's center of mass
        energyGo.transform.position = center;
        energyGo.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

        // Awakening flash centered around the upper-third of the robot
        // (where the eyes/face usually are)
        flashGo.transform.position = center + Vector3.up * size.y * 0.35f;

        // Rim light is on the SAME side as the camera now (also behind
        // the robot from the model's frame of reference, in front of the
        // camera) — gives a nice rim halo on the rotating-to-face-camera
        // moment.
        Vector3 rimPos = center - forward * (maxDim * 1.5f) + Vector3.up * size.y * 0.45f;
        rimGo.transform.position = rimPos;
        rimGo.transform.LookAt(center);

        Debug.Log($"[BuildSplashFX] Robot bounds: center={center}, size={size}. " +
                  $"Camera placed BEHIND robot at {(cam != null ? cam.transform.position.ToString() : "(none)")} " +
                  $"facing it at distance {distance:F2}.");
    }

    // Layer used for the background-only camera. Layer 30 is rarely
    // assigned in user projects; if your project uses it, change here.
    private const int SplashBackgroundLayer = 30;

    // TWO-CAMERA STACK approach for the splash background video. Most
    // reliable cross-pipeline pattern; the single-Quad approach failed
    // silently in URP.
    //
    // Setup:
    //   * VideoPlayer fills a RenderTexture
    //   * Background quad displays the RT, lives on layer 30
    //   * SplashBackgroundCamera (depth -10, SolidColor clear) renders
    //     ONLY layer 30 — it draws the video to the framebuffer
    //   * Main camera switched to clearFlags = Depth so it preserves
    //     the video framebuffer when it draws the robot on top
    private static GameObject ConfigureSplashBackgroundVideo(GameObject parent, Camera cam)
    {
        var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(SplashBackgroundVideoPath);
        if (clip == null)
        {
            Debug.LogWarning($"[BuildSplashFX] Background video not found at {SplashBackgroundVideoPath}.");
            return null;
        }
        if (cam == null)
        {
            Debug.LogWarning("[BuildSplashFX] No camera available for background video.");
            return null;
        }

        EnsureFolder(MaterialFolder);
        const string rtPath = "Assets/Materials/Splash/SplashBackgroundVideoRT.renderTexture";
        const string matPath = "Assets/Materials/Splash/SplashBackgroundVideoMat.mat";

        var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(rtPath);
        if (rt == null)
        {
            rt = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32);
            rt.name = "SplashBackgroundVideoRT";
            AssetDatabase.CreateAsset(rt, rtPath);
        }

        // VideoPlayer
        var videoGo = FindOrCreateChild(parent, "SplashBackgroundVideo", typeof(VideoPlayer));
        var player = videoGo.GetComponent<VideoPlayer>();
        player.clip = clip;
        player.playOnAwake = true;
        player.isLooping = true;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = rt;
        player.audioOutputMode = VideoAudioOutputMode.None;
        player.skipOnDrop = true;
        player.waitForFirstFrame = true;

        // Material (Unlit so it ignores scene lighting)
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", rt);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", rt);
        mat.mainTexture = rt;
        // Render both sides so we don't have to worry about which way
        // the quad's normals face. URP/Unlit + legacy both honor _Cull = 0.
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f); // 0 = Off
        mat.doubleSidedGI = true;

        // ── Background camera (renders ONLY the video Quad) ──
        var bgCamGo = FindOrCreateChild(parent, "SplashBackgroundCamera", typeof(Camera));
        bgCamGo.transform.SetParent(cam.transform, worldPositionStays: false);
        bgCamGo.transform.localPosition = Vector3.zero;
        bgCamGo.transform.localRotation = Quaternion.identity;

        var bgCam = bgCamGo.GetComponent<Camera>();
        bgCam.clearFlags = CameraClearFlags.SolidColor;
        bgCam.backgroundColor = Color.black;
        bgCam.depth = -10; // renders before main camera
        bgCam.cullingMask = 1 << SplashBackgroundLayer;
        bgCam.allowHDR = false;
        bgCam.useOcclusionCulling = false;
        bgCam.fieldOfView = cam.fieldOfView;
        bgCam.nearClipPlane = 0.1f;
        bgCam.farClipPlane = 100f;

        // Disable post-processing on the BG camera (only main cam gets PP)
        var bgCamData = bgCam.GetUniversalAdditionalCameraData();
        if (bgCamData != null)
        {
            bgCamData.renderPostProcessing = false;
            bgCamData.antialiasing = AntialiasingMode.None;
        }

        // Switch main camera to Depth Only clear so it preserves the
        // video framebuffer rendered by the BG camera.
        cam.clearFlags = CameraClearFlags.Depth;

        // ── Background Quad on layer 30 (rendered only by bgCam) ──
        var quadGo = FindOrCreateChild(parent, "SplashBackgroundQuad", typeof(MeshFilter), typeof(MeshRenderer));
        quadGo.transform.SetParent(bgCamGo.transform, worldPositionStays: false);
        quadGo.layer = SplashBackgroundLayer;

        var meshFilter = quadGo.GetComponent<MeshFilter>();
        if (meshFilter.sharedMesh == null || meshFilter.sharedMesh.name != "Quad")
        {
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Quad);
            meshFilter.sharedMesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(primitive);
        }

        var renderer = quadGo.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        // Place quad in BG camera's view, scaled to over-fill
        const float zDist = 10f;
        quadGo.transform.localPosition = new Vector3(0, 0, zDist);
        quadGo.transform.localRotation = Quaternion.identity;
        float fovRad = bgCam.fieldOfView * Mathf.Deg2Rad;
        float height = 2f * zDist * Mathf.Tan(fovRad * 0.5f);
        float width = height * 16f / 9f;
        quadGo.transform.localScale = new Vector3(width * 2f, height * 2f, 1f);

        EditorUtility.SetDirty(player);
        EditorUtility.SetDirty(bgCam);
        EditorUtility.SetDirty(cam);
        EditorUtility.SetDirty(renderer);
        EditorUtility.SetDirty(mat);
        Debug.Log($"[BuildSplashFX] Background video: two-camera stack. BG cam depth=-10 renders layer {SplashBackgroundLayer}. Main cam clear=Depth.");
        return quadGo;
    }

    // Full-screen black UI Image faded in by SplashSceneController right
    // before splash_done so the user doesn't see a Splash → Bootstrap →
    // Rob11Scene flicker as scenes swap.
    private static Image ConfigureFadeToBlackOverlay(GameObject parent)
    {
        var canvasGo = FindOrCreateChild(
            parent, "FadeToBlackCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)
        );
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // above everything

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var imageGo = FindOrCreateChild(
            canvasGo, "FadeToBlackImage",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)
        );
        var rt = (RectTransform)imageGo.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var image = imageGo.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f); // start fully transparent
        image.raycastTarget = false;
        return image;
    }

    private static Bounds ComputeRendererBounds(Transform root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return new Bounds(root.position, Vector3.one);
        }
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            b.Encapsulate(renderers[i].bounds);
        }
        return b;
    }

    private const string DefaultRob11ControllerPath = "Assets/GrigoriyArx/DrollRobots/Art/Animations/Rob11.controller";

    // Make the Animator use a Splash-only copy of its controller so any
    // tweaks (different transitions, additional states, retimed clips)
    // don't leak back to Rob11Scene. If the Animator has no controller
    // assigned, falls back to copying Rob11.controller.
    // Idempotent: if the Animator already uses a Splash copy, no-op.
    private static void IsolateAnimatorController(Animator animator)
    {
        if (animator == null) return;

        var current = animator.runtimeAnimatorController;
        string sourcePath = current != null ? AssetDatabase.GetAssetPath(current) : null;

        if (string.IsNullOrEmpty(sourcePath))
        {
            // No controller — fall back to Rob11.controller
            sourcePath = DefaultRob11ControllerPath;
            if (!File.Exists(sourcePath))
            {
                Debug.LogWarning("[BuildSplashFX] No Animator controller assigned and Rob11.controller not found — skipping isolation.");
                return;
            }
            Debug.Log($"[BuildSplashFX] Animator had no controller; using {sourcePath} as the source for isolation.");
        }

        // Already isolated?
        if (sourcePath.Contains("/Splash/") || sourcePath.Contains("_Splash."))
        {
            Debug.Log($"[BuildSplashFX] Animator already uses an isolated controller: {sourcePath}");
            return;
        }

        EnsureFolder("Assets/Animations/Splash");
        string ext = Path.GetExtension(sourcePath);
        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
        string destPath = $"Assets/Animations/Splash/{baseName}_Splash{ext}";

        if (!File.Exists(destPath))
        {
            bool ok = AssetDatabase.CopyAsset(sourcePath, destPath);
            if (!ok)
            {
                Debug.LogError($"[BuildSplashFX] Failed to copy {sourcePath} → {destPath}");
                return;
            }
            AssetDatabase.ImportAsset(destPath);
        }

        var copy = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(destPath);
        if (copy == null)
        {
            Debug.LogError($"[BuildSplashFX] Could not load duplicated controller at {destPath}");
            return;
        }

        // Assign via SerializedObject so the change is recorded as a scene
        // override on the Animator, not on any prefab the robot might be an
        // instance of.
        var so = new SerializedObject(animator);
        var prop = so.FindProperty("m_Controller");
        if (prop != null)
        {
            prop.objectReferenceValue = copy;
            so.ApplyModifiedProperties();
        }
        else
        {
            animator.runtimeAnimatorController = copy;
            EditorUtility.SetDirty(animator);
        }

        Debug.Log($"[BuildSplashFX] Isolated Animator controller: {sourcePath} → {destPath}. Tweaks here won't affect Rob11Scene.");
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static void SetFloat(SerializedObject so, string propName, float value)
    {
        var p = so.FindProperty(propName);
        if (p != null) p.floatValue = value;
    }

    private static GameObject FindOrCreateChild(GameObject parent, string name, params System.Type[] componentTypes)
    {
        var existing = parent.transform.Find(name);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
            // Ensure required components exist on the existing GameObject
            foreach (var t in componentTypes)
            {
                if (t != null && go.GetComponent(t) == null) go.AddComponent(t);
            }
        }
        else
        {
            // Construct GameObject with components in one shot — avoids the
            // ParticleSystem post-AddComponent timing issue where the
            // returned reference is technically valid but its modules can't
            // be accessed for one frame.
            go = componentTypes.Length > 0
                ? new GameObject(name, componentTypes)
                : new GameObject(name);
            go.transform.SetParent(parent.transform, worldPositionStays: false);
        }
        return go;
    }

    private static Material LoadOrCreateAdditiveMaterial(string assetPath, Color tint)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (existing != null) return existing;

        // Try URP particle shader, fall back to legacy.
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogWarning("[BuildSplashFX] No suitable particle shader found; the materials may render incorrectly.");
            shader = Shader.Find("Hidden/InternalErrorShader");
        }

        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);

        // URP particle/unlit transparency + additive setup.
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);   // Transparent
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 1f);       // Additive
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.One);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_BLENDMODE_ADD");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)RenderQueue.Transparent;

        AssetDatabase.CreateAsset(mat, assetPath);
        return mat;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parts = path.Split('/');
        string acc = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            var next = $"{acc}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(acc, parts[i]);
            }
            acc = next;
        }
    }
}
