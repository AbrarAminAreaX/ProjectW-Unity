using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using TMPro;

// One-shot builder for the Onboarding_Animation UI + glowing overlay elements.
// The Unity MCP can't assign object references, so the scene is authored here
// with conventional names that OnboardingAnimationController resolves at
// runtime. Idempotent: re-running rebuilds UICanvas + FaceSmile from scratch.
public static class OnboardingSceneBuilder
{
    const string Dir = "Assets/Onboarding_Animation/";

    [MenuItem("Tools/ProjectW/Build Onboarding UI")]
    public static void Build()
    {
        var robot = GameObject.Find("SpiritGuardian");
        if (robot == null) { Debug.LogError("[OnboardingSceneBuilder] SpiritGuardian not found."); return; }

        var heartSprite = AssetDatabase.LoadAssetAtPath<Sprite>(Dir + "Heart.png");
        var wSprite = AssetDatabase.LoadAssetAtPath<Sprite>(Dir + "W_Logo.png");
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        var smileMat = AssetDatabase.LoadAssetAtPath<Material>(Dir + "SmileGlowMat.mat");

        var uiAdd = AssetDatabase.LoadAssetAtPath<Material>(Dir + "UIAdditiveMat.mat");
        if (uiAdd == null)
        {
            uiAdd = new Material(Shader.Find("ProjectW/UIAdditive"));
            AssetDatabase.CreateAsset(uiAdd, Dir + "UIAdditiveMat.mat");
        }

        Color lime = new Color(0.78f, 1f, 0.32f, 1f);

        // ── FaceSmile (world-space quad on the robot's face) ─────────────
        var oldSmile = robot.transform.Find("FaceSmile");
        if (oldSmile != null) Object.DestroyImmediate(oldSmile.gameObject);
        var smile = GameObject.CreatePrimitive(PrimitiveType.Quad);
        smile.name = "FaceSmile";
        Object.DestroyImmediate(smile.GetComponent<MeshCollider>());
        smile.transform.SetParent(robot.transform, false);
        smile.transform.localPosition = new Vector3(0f, 0.27f, 0.19f);
        smile.transform.localRotation = Quaternion.identity;
        smile.transform.localScale = new Vector3(0.16f, 0.16f, 0.16f);
        if (smileMat != null) smile.GetComponent<MeshRenderer>().sharedMaterial = smileMat;

        // ── EventSystem (needed for the Get Started button) ──────────────
        if (Object.FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(es, "EventSystem");
        }

        // ── UICanvas ─────────────────────────────────────────────────────
        var old = GameObject.Find("UICanvas");
        if (old != null) Object.DestroyImmediate(old);

        var canvasGo = new GameObject("UICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        // Screen Space - Camera so the UI renders INTO the main camera (shows in
        // captures and composites reliably into the Flutter embed's render
        // texture, unlike Overlay). planeDistance 1 keeps it in front of the
        // robot (10 units away) and the nebula quads.
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = Camera.main;
        canvas.planeDistance = 1f;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        // Morph root (heart → W happen here, positioned where the logo lands).
        var morphRoot = NewRect("MorphRoot", canvasGo.transform, new Vector2(0, 430), new Vector2(420, 420));
        var morphCg = morphRoot.gameObject.AddComponent<CanvasGroup>();
        morphCg.alpha = 0f;

        var morphHeart = NewImage("MorphHeart", morphRoot, new Vector2(0, 0), new Vector2(420, 420), heartSprite, uiAdd, Color.white);
        var morphW = NewImage("MorphW", morphRoot, new Vector2(0, 0), new Vector2(420, 420), wSprite, uiAdd, Color.white);
        SetAlpha(morphW, 0f);

        // Headline / Subhead / Footer (TMP) + Button — each on a CanvasGroup.
        var headline = NewText("Headline", canvasGo.transform, new Vector2(0, 150), new Vector2(960, 160),
            "The Double of You", font, 70, FontStyles.Bold, Color.white);
        var subhead = NewText("Subhead", canvasGo.transform, new Vector2(0, 30), new Vector2(900, 120),
            "Own & Control Your Digital Self", font, 40, FontStyles.Normal, new Color(0.82f, 0.86f, 0.92f, 1f));
        var footer = NewText("Footer", canvasGo.transform, new Vector2(0, -840), new Vector2(900, 60),
            "Project W produced by AreaX", font, 28, FontStyles.Normal, new Color(0.6f, 0.62f, 0.68f, 1f));
        // Start hidden — the controller fades each in during Phase 4.
        headline.alpha = 0f; subhead.alpha = 0f; footer.alpha = 0f;

        // Get Started button (lime, rounded).
        var btnRect = NewRect("GetStarted", canvasGo.transform, new Vector2(0, -470), new Vector2(620, 150));
        var btnImg = btnRect.gameObject.AddComponent<Image>();
        btnImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        btnImg.type = Image.Type.Sliced;
        btnImg.color = lime;
        var btn = btnRect.gameObject.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btnRect.gameObject.AddComponent<CanvasGroup>().alpha = 0f;
        var btnLabel = NewText("Label", btnRect, Vector2.zero, new Vector2(580, 130),
            "Get Started", font, 46, FontStyles.Bold, new Color(0.05f, 0.08f, 0.02f, 1f));

        EnsurePostFX();

        EditorUtility.SetDirty(canvasGo);
        if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isLoaded)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[OnboardingSceneBuilder] Built UICanvas (MorphRoot/MorphHeart/MorphW, Headline, Subhead, GetStarted, Footer) + FaceSmile.");
    }

    // Global post-FX Volume — Bloom makes the nebula, heart and W glow; a
    // gentle warm grade ties the palette together.
    static void EnsurePostFX()
    {
        string profilePath = Dir + "Onboarding_PostProfile.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
        }
        foreach (var c in profile.components.ToArray())
        {
            profile.components.Remove(c);
            Object.DestroyImmediate(c, true);
        }

        var bloom = AddVc<Bloom>(profile);
        bloom.intensity.overrideState = true;  bloom.intensity.value = 1.1f;
        bloom.threshold.overrideState = true;  bloom.threshold.value = 0.85f;
        bloom.scatter.overrideState = true;    bloom.scatter.value = 0.75f;
        bloom.tint.overrideState = true;       bloom.tint.value = new Color(1f, 0.92f, 0.85f, 1f);

        var ca = AddVc<ColorAdjustments>(profile);
        ca.contrast.overrideState = true;    ca.contrast.value = 6f;
        ca.saturation.overrideState = true;  ca.saturation.value = 8f;

        var vig = AddVc<Vignette>(profile);
        vig.intensity.overrideState = true;  vig.intensity.value = 0.32f;
        vig.smoothness.overrideState = true; vig.smoothness.value = 0.5f;

        var oldVol = GameObject.Find("PostFX");
        if (oldVol != null) Object.DestroyImmediate(oldVol);
        var volGo = new GameObject("PostFX");
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 1f;
        vol.sharedProfile = profile;

        // URP only applies the Volume if the camera opts into post-processing.
        if (Camera.main != null)
        {
            var camData = Camera.main.GetUniversalAdditionalCameraData();
            if (camData != null) camData.renderPostProcessing = true;
        }

        EditorUtility.SetDirty(profile);
    }

    static T AddVc<T>(VolumeProfile p) where T : VolumeComponent
    {
        var c = ScriptableObject.CreateInstance<T>();
        c.hideFlags = HideFlags.HideInHierarchy;
        c.name = typeof(T).Name;
        p.components.Add(c);
        AssetDatabase.AddObjectToAsset(c, p);
        return c;
    }

    static RectTransform NewRect(string name, Transform parent, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        return rt;
    }

    static Image NewImage(string name, Transform parent, Vector2 pos, Vector2 size, Sprite sprite, Material mat, Color color)
    {
        var rt = NewRect(name, parent, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.material = mat;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static CanvasGroup NewText(string name, Transform parent, Vector2 pos, Vector2 size,
        string text, TMP_FontAsset font, float fontSize, FontStyles style, Color color)
    {
        var rt = NewRect(name, parent, pos, size);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        if (font != null) tmp.font = font;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        var cg = rt.gameObject.GetComponent<CanvasGroup>();
        if (cg == null) cg = rt.gameObject.AddComponent<CanvasGroup>();
        return cg;
    }

    static void SetAlpha(Image img, float a)
    {
        var c = img.color; c.a = a; img.color = c;
    }
}
