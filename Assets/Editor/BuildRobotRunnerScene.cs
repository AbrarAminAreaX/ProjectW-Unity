using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public static class BuildRobotRunnerScene
{
    private const string ScenePath = "Assets/Scenes/MiniGame_RobotRunner.unity";
    private const string PrefabPath = "Assets/Prefabs/Obstacle.prefab";
    private const string AddressableGroup = "MiniGame_RobotRunner";
    private const string AddressableKey = "MiniGame_RobotRunner";
    private const string BootstrapPath = "Assets/Scenes/Bootstrap.unity";

    [MenuItem("Tools/ProjectW/1. Build Robot Runner Scene")]
    public static void Build()
    {
        EnsureFolder("Assets/Scenes");
        EnsureFolder("Assets/Prefabs");
        AddTagIfMissing("Obstacle");

        // 1. Obstacle prefab — built and saved first so the spawner can hold a real reference.
        var obstaclePrefab = BuildObstaclePrefab();

        // 2. New scene with default camera + light.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // 3. Camera placement.
        var cam = GameObject.Find("Main Camera");
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, 4f, -7f);
            cam.transform.eulerAngles = new Vector3(20f, 0f, 0f);
            var c = cam.GetComponent<Camera>();
            if (c != null) c.backgroundColor = new Color(0.05f, 0.07f, 0.12f, 1f);
        }

        // 4. Ground.
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = new Vector3(0f, 0f, 20f);
        ground.transform.localScale = new Vector3(1f, 1f, 50f);
        ApplyMaterial(ground, new Color(0.15f, 0.2f, 0.25f));

        // 5. Player capsule.
        var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = "Player";
        player.tag = "Player";
        player.transform.position = new Vector3(0f, 0.5f, 0f);
        var pCol = player.GetComponent<Collider>();
        pCol.isTrigger = true;
        ApplyMaterial(player, new Color(0.2f, 0.7f, 1f));
        var runner = player.AddComponent<RobotRunner>();

        // 6. Obstacle spawner.
        var spawnerGo = new GameObject("ObstacleSpawner");
        var spawner = spawnerGo.AddComponent<ObstacleSpawner>();
        spawner.obstaclePrefab = obstaclePrefab;

        // 7. Game manager.
        var gmGo = new GameObject("GameManager");
        var gm = gmGo.AddComponent<RunnerGameManager>();
        gm.player = runner;
        gm.spawner = spawner;
        runner.gameManager = gm;
        spawner.gameManager = gm;

        // 8. UI Canvas + EventSystem (required for buttons).
        var canvasGo = new GameObject("UICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);

        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        // Score (top-left).
        var scoreGo = new GameObject("ScoreText", typeof(RectTransform));
        scoreGo.transform.SetParent(canvasGo.transform, false);
        var scoreRT = (RectTransform)scoreGo.transform;
        scoreRT.anchorMin = new Vector2(0f, 1f);
        scoreRT.anchorMax = new Vector2(0f, 1f);
        scoreRT.pivot = new Vector2(0f, 1f);
        scoreRT.anchoredPosition = new Vector2(40f, -40f);
        scoreRT.sizeDelta = new Vector2(300f, 80f);
        var scoreText = scoreGo.AddComponent<TextMeshProUGUI>();
        scoreText.text = "0";
        scoreText.fontSize = 64f;
        scoreText.color = Color.white;

        // Exit (top-right).
        var exitGo = CreateButton(
            canvasGo.transform, "ExitButton", "Exit",
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-40f, -40f), new Vector2(160f, 80f)
        );
        var exitBtn = exitGo.GetComponent<Button>();

        // Game-over panel.
        var panelGo = new GameObject("GameOverPanel", typeof(RectTransform), typeof(Image));
        panelGo.transform.SetParent(canvasGo.transform, false);
        var panelRT = (RectTransform)panelGo.transform;
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = Vector2.zero;
        panelRT.sizeDelta = new Vector2(600f, 500f);
        panelGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.8f);

        var titleGo = new GameObject("Title", typeof(RectTransform));
        titleGo.transform.SetParent(panelGo.transform, false);
        var titleRT = (RectTransform)titleGo.transform;
        titleRT.anchorMin = new Vector2(0.5f, 1f);
        titleRT.anchorMax = new Vector2(0.5f, 1f);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0f, -40f);
        titleRT.sizeDelta = new Vector2(500f, 100f);
        var titleText = titleGo.AddComponent<TextMeshProUGUI>();
        titleText.text = "Game Over";
        titleText.fontSize = 64f;
        titleText.color = Color.white;
        titleText.alignment = TextAlignmentOptions.Center;

        var finalGo = new GameObject("FinalScore", typeof(RectTransform));
        finalGo.transform.SetParent(panelGo.transform, false);
        var finalRT = (RectTransform)finalGo.transform;
        finalRT.anchorMin = new Vector2(0.5f, 1f);
        finalRT.anchorMax = new Vector2(0.5f, 1f);
        finalRT.pivot = new Vector2(0.5f, 1f);
        finalRT.anchoredPosition = new Vector2(0f, -160f);
        finalRT.sizeDelta = new Vector2(500f, 120f);
        var finalText = finalGo.AddComponent<TextMeshProUGUI>();
        finalText.text = "0";
        finalText.fontSize = 96f;
        finalText.color = Color.yellow;
        finalText.alignment = TextAlignmentOptions.Center;

        var restartGo = CreateButton(
            panelGo.transform, "RestartButton", "Restart",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 140f), new Vector2(280f, 80f)
        );
        var restartBtn = restartGo.GetComponent<Button>();

        var goExitGo = CreateButton(
            panelGo.transform, "GameOverExit", "Exit",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 40f), new Vector2(280f, 80f)
        );
        var goExitBtn = goExitGo.GetComponent<Button>();

        panelGo.SetActive(false);

        // RunnerGameUI on the Canvas.
        var ui = canvasGo.AddComponent<RunnerGameUI>();
        ui.scoreText = scoreText;
        ui.exitButton = exitBtn;
        ui.gameOverPanel = panelGo;
        ui.finalScoreText = finalText;
        ui.restartButton = restartBtn;
        ui.gameOverExitButton = goExitBtn;
        ui.gameManager = gm;
        gm.ui = ui;

        // 9. Save scene.
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 10. Mark scene Addressable.
        MarkAddressable();

        Debug.Log($"[BuildRobotRunnerScene] Done. Scene: {ScenePath}, Prefab: {PrefabPath}");
        EditorUtility.DisplayDialog(
            "Robot Runner",
            "Scene built. Next:\n\n1. Tools → ProjectW → 2. Add AddressableLoader to Bootstrap\n2. Window → Asset Management → Addressables → Groups → Build → New Build → Default\n3. Upload ServerData/Android to CCD",
            "OK"
        );
    }

    [MenuItem("Tools/ProjectW/2. Add AddressableHost to Bootstrap")]
    public static void AddLoaderToBootstrap()
    {
        var scene = EditorSceneManager.OpenScene(BootstrapPath, OpenSceneMode.Single);

        // Clean up old setup: AddressableLoader used to live on SceneManager.
        // Move it off so we don't have ambiguous "SceneManager" routing.
        var sm = GameObject.Find("SceneManager");
        if (sm != null)
        {
            var stale = sm.GetComponent<AddressableLoader>();
            if (stale != null) Object.DestroyImmediate(stale, true);
        }

        var host = GameObject.Find("AddressableHost");
        if (host == null)
        {
            host = new GameObject("AddressableHost");
        }
        if (host.GetComponent<AddressableLoader>() == null)
        {
            host.AddComponent<AddressableLoader>();
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[BuildRobotRunnerScene] AddressableHost (with AddressableLoader) ready in Bootstrap.unity");
        EditorUtility.DisplayDialog("Bootstrap", "AddressableHost set up. Now re-export Unity to /Users/abraramin/Work/Builds/ProjectW_Export and replace android/unityLibrary in the Flutter app.", "OK");
    }

    private static GameObject BuildObstaclePrefab()
    {
        var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        temp.name = "Obstacle";
        temp.tag = "Obstacle";
        temp.transform.localScale = new Vector3(1.5f, 1f, 1f);
        var col = temp.GetComponent<BoxCollider>();
        col.isTrigger = true;
        ApplyMaterial(temp, new Color(0.85f, 0.25f, 0.25f));
        var prefab = PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath);
        Object.DestroyImmediate(temp);
        return prefab;
    }

    private static GameObject CreateButton(
        Transform parent, string name, string label,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var labelRT = (RectTransform)labelGo.transform;
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;
        var labelText = labelGo.AddComponent<TextMeshProUGUI>();
        labelText.text = label;
        labelText.fontSize = 36f;
        labelText.color = Color.white;
        labelText.alignment = TextAlignmentOptions.Center;
        return go;
    }

    private static void ApplyMaterial(GameObject go, Color color)
    {
        var renderer = go.GetComponent<Renderer>();
        if (renderer == null) return;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) return;
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        else mat.color = color;
        renderer.sharedMaterial = mat;
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

    private static void AddTagIfMissing(string tag)
    {
        var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (asset == null || asset.Length == 0) return;
        var so = new SerializedObject(asset[0]);
        var tags = so.FindProperty("tags");
        for (int i = 0; i < tags.arraySize; i++)
        {
            if (tags.GetArrayElementAtIndex(i).stringValue == tag) return;
        }
        tags.arraySize += 1;
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
        so.ApplyModifiedProperties();
    }

    private static void MarkAddressable()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogWarning("[BuildRobotRunnerScene] Addressables settings not initialised. Open Window → Asset Management → Addressables → Groups once, then re-run this menu item.");
            return;
        }

        var group = settings.FindGroup(AddressableGroup);
        if (group == null)
        {
            group = settings.CreateGroup(
                AddressableGroup,
                setAsDefaultGroup: false,
                readOnly: false,
                postEvent: true,
                schemasToCopy: null,
                types: new[] { typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema) }
            );
            var bundled = group.GetSchema<BundledAssetGroupSchema>();
            // Point at the Remote profile variables ("Remote.BuildPath" / "Remote.LoadPath").
            bundled.BuildPath.SetVariableByName(settings, "Remote.BuildPath");
            bundled.LoadPath.SetVariableByName(settings, "Remote.LoadPath");
            bundled.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
        }

        var sceneGuid = AssetDatabase.AssetPathToGUID(ScenePath);
        var entry = settings.CreateOrMoveEntry(sceneGuid, group);
        entry.address = AddressableKey;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[BuildRobotRunnerScene] Addressable: group '{AddressableGroup}', key '{AddressableKey}'.");
    }
}
