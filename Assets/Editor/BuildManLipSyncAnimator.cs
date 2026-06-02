using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// One-shot Editor utility: builds an AnimatorController for the ManLipSync
/// scene character with all 11 Mixamo reaction animations + an idle default.
/// Each reaction is a separate state triggered by a Trigger parameter,
/// with an automatic return-to-idle on Exit Time.
///
/// Run via menu: Tools → Build ManLipSync Animator Controller
///
/// Output: Assets/Animations/ManLipSyncController.controller
/// </summary>
public static class BuildManLipSyncAnimator
{
    private const string OutputPath = "Assets/Animations/ManLipSyncController.controller";
    private const string AnimRoot   = "Assets/ready-player-me-female-avatar-vrchatgame";

    // (FBX file (no extension), Animator parameter name, transition out exit-time-normalized)
    // Exit time of 0.85 = transition back to idle when 85% of clip has played.
    private static readonly (string fbx, string trigger, float exitTime)[] Reactions = new[]
    {
        ("Idle (1)",                "",       0f),     // default state
        ("Waving",                  "Wave",   0.85f),
        ("Head Nod Yes",            "Yes",    0.85f),
        ("Shaking Head No",         "No",     0.85f),
        ("Shrugging",               "Shrug",  0.85f),
        ("Standing Thumbs Up",      "Thumb",  0.85f),
        ("Laughing",                "Laugh",  0.85f),
        ("Crying",                  "Cry",    0.85f),
        ("Thinking",                "Think",  0.85f),
        ("Pointing",                "Point",  0.85f),
        ("Clapping",                "Clap",   0.85f),
        ("Locking Hip Hop Dance",   "Dance",  0.95f),
    };

    [MenuItem("Tools/Build ManLipSync Animator Controller")]
    public static void Build()
    {
        // 1. Make sure target folder exists.
        var dir = Path.GetDirectoryName(OutputPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        // 2. (Re)create the controller from scratch so re-runs are clean.
        if (File.Exists(OutputPath)) AssetDatabase.DeleteAsset(OutputPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(OutputPath);

        var sm = ctrl.layers[0].stateMachine;

        // 3. Add a Trigger parameter for each non-idle reaction.
        foreach (var r in Reactions)
        {
            if (string.IsNullOrEmpty(r.trigger)) continue;
            ctrl.AddParameter(r.trigger, AnimatorControllerParameterType.Trigger);
        }

        // 4. Find each clip and create its state. Force Loop Time = false on
        //    every reaction clip so the state plays once and exits via Exit
        //    Time. The Idle clip keeps its existing loop setting (we want
        //    Idle to loop indefinitely between reactions).
        AnimatorState idleState = null;
        var stateByTrigger = new System.Collections.Generic.Dictionary<string, AnimatorState>();

        foreach (var r in Reactions)
        {
            string fbxPath = $"{AnimRoot}/{r.fbx}.fbx";
            var clip = LoadClip(fbxPath);
            if (clip == null)
            {
                Debug.LogError($"[BuildManLipSyncAnimator] No clip found at {fbxPath}");
                continue;
            }

            // Disable loop on reaction clips via the FBX importer (Idle stays
            // looping). Setting the import setting + reimporting updates the
            // baked AnimationClip.
            if (!string.IsNullOrEmpty(r.trigger))
            {
                var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                if (importer != null && importer.clipAnimations.Length > 0)
                {
                    var clips = importer.clipAnimations;
                    bool changed = false;
                    for (int i = 0; i < clips.Length; i++)
                    {
                        if (clips[i].loopTime || clips[i].loop)
                        {
                            clips[i].loopTime = false;
                            clips[i].loop = false;
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        importer.clipAnimations = clips;
                        importer.SaveAndReimport();
                        // Re-load the clip after reimport — old reference is stale.
                        clip = LoadClip(fbxPath);
                    }
                }
            }

            var stateName = string.IsNullOrEmpty(r.trigger) ? "Idle" : r.trigger;
            var state = sm.AddState(stateName);
            state.motion = clip;

            if (string.IsNullOrEmpty(r.trigger))
            {
                idleState = state;
                sm.defaultState = state;
            }
            else
            {
                stateByTrigger[r.trigger] = state;
            }
        }

        if (idleState == null)
        {
            Debug.LogError("[BuildManLipSyncAnimator] No idle state could be created. Aborting.");
            return;
        }

        // 5. Wire transitions: Idle → reaction (on trigger), reaction → Idle (on Exit Time).
        foreach (var r in Reactions)
        {
            if (string.IsNullOrEmpty(r.trigger)) continue;
            if (!stateByTrigger.TryGetValue(r.trigger, out var rxn)) continue;

            // Idle → reaction (instant on trigger)
            var inT = idleState.AddTransition(rxn);
            inT.hasExitTime = false;
            inT.exitTime = 0f;
            inT.duration = 0.1f;
            inT.AddCondition(AnimatorConditionMode.If, 0f, r.trigger);

            // Reaction → Idle (auto on exit time)
            var outT = rxn.AddTransition(idleState);
            outT.hasExitTime = true;
            outT.exitTime = r.exitTime;
            outT.duration = 0.15f;
        }

        // 6. Save & report.
        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[BuildManLipSyncAnimator] Built {OutputPath} with {Reactions.Length - 1} reactions + idle.");
    }

    private static AnimationClip LoadClip(string fbxPath)
    {
        // Mixamo FBX bundles a single AnimationClip sub-asset. Find it.
        var assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        foreach (var a in assets)
        {
            if (a is AnimationClip clip && !clip.name.StartsWith("__preview__"))
            {
                return clip;
            }
        }
        return null;
    }
}
