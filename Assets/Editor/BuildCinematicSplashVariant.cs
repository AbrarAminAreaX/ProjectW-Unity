using System.Collections.Generic;
using System.IO;
using Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

// Tools → ProjectW → 6. Create Cinematic Splash Variant
//
// Duplicates Splash.unity → Splash_Cinematic.unity (the original is left
// untouched). Sets up the new scene with Cinemachine + Timeline scaffold
// for cinematic multi-shot work:
//   * CinemachineBrain on the Main Camera
//   * 5 CinemachineVirtualCameras pre-positioned at hero angles around
//     the robot (wide establishing, medium 3/4, face close-up, hero
//     low-angle, final reveal)
//   * Empty Timeline + PlayableDirector on a SplashTimeline GameObject
//   * SplashSceneController's procedural camera animation disabled
//     (cameraEndRise=0, cameraDollyZ=0, targetCamera=null) so
//     Cinemachine fully controls the camera
//
// You then open Window → Sequencing → Timeline with the SplashTimeline
// GameObject selected, add a Cinemachine Track, and drag your VCams
// onto the track to author the shot sequence.
public static class BuildCinematicSplashVariant
{
    private const string SourcePath = "Assets/Scenes/Splash.unity";
    private const string DestPath = "Assets/Scenes/Splash_Cinematic.unity";
    private const string TimelineFolder = "Assets/Timelines/Splash";
    private const string TimelinePath = TimelineFolder + "/SplashCinematic.playable";

    [MenuItem("Tools/ProjectW/6. Create Cinematic Splash Variant")]
    public static void Build()
    {
        if (!File.Exists(SourcePath))
        {
            EditorUtility.DisplayDialog("Cinematic Splash", $"Source scene not found: {SourcePath}", "OK");
            return;
        }

        // Save any open changes first so we don't lose work
        if (EditorSceneManager.GetActiveScene().isDirty)
        {
            EditorSceneManager.SaveOpenScenes();
        }

        // Duplicate the scene file (skip if it already exists — caller
        // wanted to preserve any tweaks they've made to the variant)
        if (!File.Exists(DestPath))
        {
            AssetDatabase.CopyAsset(SourcePath, DestPath);
            AssetDatabase.Refresh();
        }

        var scene = EditorSceneManager.OpenScene(DestPath, OpenSceneMode.Single);

        var controllerGo = GameObject.Find("SplashController");
        if (controllerGo == null)
        {
            EditorUtility.DisplayDialog("Cinematic Splash", "SplashController GameObject not found in scene.", "OK");
            return;
        }
        var ssc = controllerGo.GetComponent<SplashSceneController>();
        if (ssc == null)
        {
            EditorUtility.DisplayDialog("Cinematic Splash", "SplashSceneController component missing.", "OK");
            return;
        }

        if (ssc.robotRoot == null)
        {
            EditorUtility.DisplayDialog("Cinematic Splash", "robotRoot not assigned on SplashSceneController.", "OK");
            return;
        }

        // Compute robot bounds for vcam placement
        var bounds = ComputeRendererBounds(ssc.robotRoot);
        Vector3 center = bounds.center;
        Vector3 size = bounds.size;
        float diag = Mathf.Max(0.5f, size.magnitude);

        // Add CinemachineBrain to Main Camera
        var mainCam = Camera.main;
        if (mainCam == null)
        {
            EditorUtility.DisplayDialog("Cinematic Splash", "No Main Camera tagged in scene.", "OK");
            return;
        }
        if (mainCam.GetComponent<CinemachineBrain>() == null)
        {
            mainCam.gameObject.AddComponent<CinemachineBrain>();
        }

        // Parent for all virtual cameras
        var cmRoot = FindOrCreateChild(controllerGo, "CinematicCameras");
        cmRoot.transform.localPosition = Vector3.zero;
        cmRoot.transform.localRotation = Quaternion.identity;

        // 5 shots — all cameras keep the robot fully framed and use
        // LookAt = robotRoot so they auto-track during blends. Distances
        // scaled by robot diagonal so any robot size works.
        var vcam1 = CreateVCam(cmRoot, "VCam1_WideEstablish",
            center + new Vector3(0f, size.y * 0.4f, -diag * 3.5f),
            ssc.robotRoot, fov: 50f);

        var vcam2 = CreateVCam(cmRoot, "VCam2_OrbitRight",
            center + new Vector3(diag * 1.4f, size.y * 0.5f, -diag * 1.8f),
            ssc.robotRoot, fov: 45f);

        var vcam3 = CreateVCam(cmRoot, "VCam3_FrontMedium",
            center + new Vector3(0f, size.y * 0.4f, -diag * 1.8f),
            ssc.robotRoot, fov: 42f);

        var vcam4 = CreateVCam(cmRoot, "VCam4_HeroLowAngle",
            center + new Vector3(diag * 0.5f, -size.y * 0.05f, -diag * 1.6f),
            ssc.robotRoot, fov: 45f);

        var vcam5 = CreateVCam(cmRoot, "VCam5_FinalReveal",
            center + new Vector3(-diag * 0.8f, size.y * 0.6f, -diag * 2.2f),
            ssc.robotRoot, fov: 45f);

        // Disable SplashSceneController's procedural camera handling so
        // Cinemachine fully owns the camera
        var so = new SerializedObject(ssc);
        var camProp = so.FindProperty("targetCamera");
        if (camProp != null) camProp.objectReferenceValue = null;
        SetFloat(so, "cameraEndRise", 0f);
        SetFloat(so, "cameraDollyZ", 0f);
        // Tighter / less dramatic crash zoom — tunable per shot via Timeline
        SetFloat(so, "cameraAwakeningFov", 45f);
        so.ApplyModifiedProperties();

        // Empty Timeline + Director — user authors shots in the Timeline window
        EnsureFolder(TimelineFolder);
        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
        if (timeline == null)
        {
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
        }

        var directorGo = FindOrCreateChild(controllerGo, "SplashTimeline", typeof(PlayableDirector));
        var director = directorGo.GetComponent<PlayableDirector>();
        director.playableAsset = timeline;
        director.playOnAwake = true;
        director.extrapolationMode = DirectorWrapMode.Hold;

        // Wire shots into the timeline. Timing roughly aligns with
        // SplashSceneController phases (assembly → rotate → awakening
        // → wave → hold).
        var brain = mainCam.GetComponent<CinemachineBrain>();
        SetupTimelineShots(timeline, director, brain, vcam1, vcam2, vcam3, vcam4, vcam5);

        EditorUtility.SetDirty(timeline);
        EditorUtility.SetDirty(director);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"[BuildCinematicSplashVariant] Created {DestPath} with 5 CinemachineVirtualCameras + Timeline scaffold.");
        EditorUtility.DisplayDialog(
            "Cinematic Splash",
            "Splash_Cinematic.unity created.\n\n" +
            "What's set up:\n" +
            "  • CinemachineBrain on Main Camera\n" +
            "  • 5 VCams: WideEstablish, Medium34, FaceCloseUp, HeroLowAngle, FinalReveal\n" +
            "  • Empty Timeline + PlayableDirector on SplashTimeline GO\n" +
            "  • SplashSceneController camera animation DISABLED\n\n" +
            "Next steps:\n" +
            "  1. Open Window → Sequencing → Timeline\n" +
            "  2. Select SplashTimeline GameObject — Timeline asset opens\n" +
            "  3. Right-click track area → Cinemachine Track\n" +
            "  4. Drag each VCam onto the track to make shots; resize/blend in the timeline\n" +
            "  5. Add Animation Track (driving the robot) + Volume Profile Track for per-shot post-FX\n\n" +
            "The original Splash.unity is unchanged.",
            "OK");
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static CinemachineVirtualCamera CreateVCam(GameObject parent, string name, Vector3 pos, Transform target, float fov)
    {
        var existing = parent.transform.Find(name);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(name);
            go.transform.SetParent(parent.transform, worldPositionStays: false);
        }
        go.transform.position = pos;
        if (target != null) go.transform.LookAt(target);

        var vcam = go.GetComponent<CinemachineVirtualCamera>();
        if (vcam == null) vcam = go.AddComponent<CinemachineVirtualCamera>();
        vcam.m_Lens.FieldOfView = fov;
        // LookAt = robot root → camera always aims at robot. Critical
        // during blends so the subject doesn't drift off-frame as the
        // camera position lerps between shots.
        vcam.LookAt = target;
        // Body = Do Nothing (static position), Aim = Composer (tracks LookAt)
        // Composer is the default Aim when LookAt is set, so no extra setup.
        return vcam;
    }

    // Wires 5 Cinemachine shots into the Timeline. Overlapping clips
    // produce auto-blends (the overlap region IS the blend duration).
    // Times are tuned to SplashSceneController phases:
    //   0.0–1.0  Wide establish        (preBuildup + buildup)
    //   0.7–3.3  Medium 3/4 (dolly)    (assembly)
    //   3.0–4.5  Face close-up         (rotate → awakening)
    //   4.2–5.8  Hero low-angle        (wave + smile)
    //   5.5–7.2  Final reveal pull-back (post-awakening hold)
    private static void SetupTimelineShots(
        TimelineAsset timeline,
        PlayableDirector director,
        CinemachineBrain brain,
        CinemachineVirtualCamera vc1,
        CinemachineVirtualCamera vc2,
        CinemachineVirtualCamera vc3,
        CinemachineVirtualCamera vc4,
        CinemachineVirtualCamera vc5)
    {
        // Idempotent: clear existing tracks so re-runs rebuild cleanly
        var existing = new List<TrackAsset>();
        foreach (var t in timeline.GetOutputTracks()) existing.Add(t);
        foreach (var t in existing) timeline.DeleteTrack(t);

        var cmTrack = timeline.CreateTrack<CinemachineTrack>(null, "Cameras");
        director.SetGenericBinding(cmTrack, brain);

        AddShot(cmTrack, director, vc1, start: 0.0, duration: 1.0, name: "Wide");
        AddShot(cmTrack, director, vc2, start: 0.7, duration: 2.6, name: "Medium 3/4");
        AddShot(cmTrack, director, vc3, start: 3.0, duration: 1.5, name: "Face Close-up");
        AddShot(cmTrack, director, vc4, start: 4.2, duration: 1.6, name: "Hero Low-angle");
        AddShot(cmTrack, director, vc5, start: 5.5, duration: 1.7, name: "Final Reveal");
    }

    private static void AddShot(
        CinemachineTrack track,
        PlayableDirector director,
        CinemachineVirtualCamera vcam,
        double start,
        double duration,
        string name)
    {
        var clip = track.CreateDefaultClip();
        clip.start = start;
        clip.duration = duration;
        clip.displayName = name;
        var shot = clip.asset as CinemachineShot;
        if (shot == null) return;
        // Each CinemachineShot stores its vcam as an ExposedReference,
        // so the actual binding lives on the PlayableDirector.
        var exposedName = System.Guid.NewGuid().ToString();
        shot.VirtualCamera.exposedName = exposedName;
        director.SetReferenceValue(exposedName, vcam);
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

    private static void SetFloat(SerializedObject so, string propName, float value)
    {
        var p = so.FindProperty(propName);
        if (p != null) p.floatValue = value;
    }
}
