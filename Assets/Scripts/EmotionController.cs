using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives a Ready Player Me / ARKit 52 character's face through emotion
/// presets (Happy, Sad, Angry, Surprised, Disgust, Love, Neutral). Each
/// preset is a combination of blend shape weights that smoothly lerp in
/// and back out to neutral.
///
/// Usage:
///   emotionController.SetEmotion("happy");
///   emotionController.SetEmotion("neutral");   // reset
///
/// Drag the character's face SkinnedMeshRenderer into `faceMesh` in the
/// Inspector.
/// </summary>
public class EmotionController : MonoBehaviour
{
    [Tooltip("The SkinnedMeshRenderer on the RPM character's head/face mesh (the one with the ARKit blend shapes).")]
    public SkinnedMeshRenderer faceMesh;

    [Tooltip("How fast blend shape weights lerp toward their target (weight-units per second). Lower = more natural.")]
    public float lerpSpeed = 2f;

    [Header("Idle Liveliness")]
    [Tooltip("Auto-blink the eyes at random intervals to make the face feel alive.")]
    public bool autoBlink = true;
    [Tooltip("Average seconds between blinks.")]
    public float blinkIntervalAverage = 4.5f;
    [Tooltip("± random jitter on the blink interval.")]
    public float blinkIntervalJitter = 1.5f;
    [Tooltip("Single-blink duration (close → open).")]
    public float blinkDuration = 0.16f;

    // Blend-shape weight range in Unity is 0..100.
    private const float MaxWeight = 100f;

    // Name → current/target weight (0..1 normalized; we scale to 100 when applying).
    private readonly Dictionary<string, float> _current = new Dictionary<string, float>();
    private readonly Dictionary<string, float> _target  = new Dictionary<string, float>();

    // Name → blend-shape index on the mesh (cached to avoid string lookups per frame).
    private Dictionary<string, int> _indexByName;

    // Predefined emotion presets using the RPM / ARKit 52 naming convention
    // (note: "Left"/"Right" suffix, NOT "_L"/"_R").
    private static readonly Dictionary<string, Dictionary<string, float>> Presets =
        new Dictionary<string, Dictionary<string, float>>()
    {
        ["neutral"] = new Dictionary<string, float>(),
        // Weights toned down from 1.0 → 0.5–0.65 — at full weight RPM blend
        // shapes look mask-like. ~60% is much more natural for facial expressions.
        ["happy"] = new Dictionary<string, float>()
        {
            { "mouthSmileLeft",  0.55f },
            { "mouthSmileRight", 0.55f },
            { "cheekSquintLeft", 0.30f },
            { "cheekSquintRight",0.30f },
            { "eyeSquintLeft",   0.15f },
            { "eyeSquintRight",  0.15f },
        },
        ["sad"] = new Dictionary<string, float>()
        {
            { "mouthFrownLeft",  0.65f },
            { "mouthFrownRight", 0.65f },
            { "browInnerUp",     0.6f },
            { "eyeSquintLeft",   0.15f },
            { "eyeSquintRight",  0.15f },
        },
        ["angry"] = new Dictionary<string, float>()
        {
            { "browDownLeft",    0.7f },
            { "browDownRight",   0.7f },
            { "noseSneerLeft",   0.35f },
            { "noseSneerRight",  0.35f },
            { "mouthPressLeft",  0.3f },
            { "mouthPressRight", 0.3f },
        },
        ["surprised"] = new Dictionary<string, float>()
        {
            { "jawOpen",         0.35f },
            { "browInnerUp",     0.75f },
            { "browOuterUpLeft", 0.55f },
            { "browOuterUpRight",0.55f },
            { "eyeWideLeft",     0.75f },
            { "eyeWideRight",    0.75f },
        },
        ["disgust"] = new Dictionary<string, float>()
        {
            { "noseSneerLeft",   0.7f },
            { "noseSneerRight",  0.7f },
            { "mouthLeft",       0.25f },
            { "cheekSquintLeft", 0.25f },
            { "cheekSquintRight",0.25f },
        },
        ["love"] = new Dictionary<string, float>()
        {
            { "mouthSmileLeft",  0.5f },
            { "mouthSmileRight", 0.5f },
            { "cheekSquintLeft", 0.4f },
            { "cheekSquintRight",0.4f },
            { "eyeBlinkLeft",    0.10f },
            { "eyeBlinkRight",   0.10f },
        },
        ["fear"] = new Dictionary<string, float>()
        {
            { "browInnerUp",     0.7f },
            { "browOuterUpLeft", 0.4f },
            { "browOuterUpRight",0.4f },
            { "eyeWideLeft",     0.7f },
            { "eyeWideRight",    0.7f },
            { "mouthStretchLeft",  0.35f },
            { "mouthStretchRight", 0.35f },
        },
        ["wonder"] = new Dictionary<string, float>()
        {
            { "browInnerUp",      0.45f },
            { "browOuterUpLeft",  0.3f },
            { "browOuterUpRight", 0.3f },
            { "mouthFunnel",      0.3f },
            { "eyeWideLeft",      0.4f },
            { "eyeWideRight",     0.4f },
        },
    };

    void Awake()
    {
        BuildIndex();
    }

    void Start()
    {
        if (autoBlink) StartCoroutine(AutoBlinkLoop());
    }

    // Two indices cached separately from the lerp dict so blinks don't fight
    // the emotion presets — blinks write directly to the mesh.
    private int _blinkIdxLeft = -1;
    private int _blinkIdxRight = -1;
    private bool _blinkActive;

    private System.Collections.IEnumerator AutoBlinkLoop()
    {
        // Wait a beat for BuildIndex to populate.
        yield return null;
        if (_indexByName != null)
        {
            _indexByName.TryGetValue("eyeBlinkLeft", out _blinkIdxLeft);
            _indexByName.TryGetValue("eyeBlinkRight", out _blinkIdxRight);
        }
        if (_blinkIdxLeft < 0 || _blinkIdxRight < 0) yield break;

        while (autoBlink)
        {
            float wait = blinkIntervalAverage + Random.Range(-blinkIntervalJitter, blinkIntervalJitter);
            yield return new WaitForSeconds(Mathf.Max(0.5f, wait));
            yield return DoBlink();
        }
    }

    private System.Collections.IEnumerator DoBlink()
    {
        if (faceMesh == null || _blinkIdxLeft < 0 || _blinkIdxRight < 0) yield break;

        _blinkActive = true;
        float half = blinkDuration * 0.5f;
        float t = 0f;
        // Close eyes
        while (t < half)
        {
            t += Time.deltaTime;
            float w = Mathf.Lerp(0f, 100f, t / half);
            faceMesh.SetBlendShapeWeight(_blinkIdxLeft, w);
            faceMesh.SetBlendShapeWeight(_blinkIdxRight, w);
            yield return null;
        }
        // Open eyes
        t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            float w = Mathf.Lerp(100f, 0f, t / half);
            faceMesh.SetBlendShapeWeight(_blinkIdxLeft, w);
            faceMesh.SetBlendShapeWeight(_blinkIdxRight, w);
            yield return null;
        }
        // Land on 0 then release control back to the main lerp loop.
        faceMesh.SetBlendShapeWeight(_blinkIdxLeft, 0f);
        faceMesh.SetBlendShapeWeight(_blinkIdxRight, 0f);
        _blinkActive = false;
    }

    private void BuildIndex()
    {
        _indexByName = new Dictionary<string, int>();
        if (faceMesh == null || faceMesh.sharedMesh == null) return;
        var mesh = faceMesh.sharedMesh;
        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            _indexByName[mesh.GetBlendShapeName(i)] = i;
        }
    }

    /// <summary>
    /// Switch to the named emotion. Unknown names fall back to "neutral".
    /// </summary>
    public void SetEmotion(string emotion)
    {
        if (string.IsNullOrEmpty(emotion)) emotion = "neutral";
        emotion = emotion.ToLowerInvariant();

        Dictionary<string, float> preset;
        if (!Presets.TryGetValue(emotion, out preset))
        {
            preset = Presets["neutral"];
        }
        // Start from "every known shape back to 0", then apply preset.
        _target.Clear();
        foreach (var kv in preset) _target[kv.Key] = kv.Value;
    }

    void Update()
    {
        if (faceMesh == null || _indexByName == null) return;

        // Collect the union of currently-active and target shape names so we
        // lerp already-active shapes back toward 0 when they're not in the
        // new target preset.
        var allNames = new HashSet<string>(_current.Keys);
        foreach (var k in _target.Keys) allNames.Add(k);

        float step = lerpSpeed * Time.deltaTime;

        foreach (var name in allNames)
        {
            float cur = _current.TryGetValue(name, out float c) ? c : 0f;
            float tgt = _target.TryGetValue(name, out float t) ? t : 0f;
            cur = Mathf.MoveTowards(cur, tgt, step);
            _current[name] = cur;

            int idx;
            if (_indexByName.TryGetValue(name, out idx))
            {
                // Don't overwrite the blink driver while a blink is in progress.
                if (_blinkActive && (idx == _blinkIdxLeft || idx == _blinkIdxRight)) continue;
                faceMesh.SetBlendShapeWeight(idx, cur * MaxWeight);
            }
        }
    }

    /// <summary>
    /// Directly set a named blend shape (bypasses presets). Useful for
    /// lip sync (jawOpen / visemes) layered on top of emotions.
    /// </summary>
    public void SetBlendShape(string name, float weightNormalized)
    {
        if (faceMesh == null || _indexByName == null) return;
        int idx;
        if (_indexByName.TryGetValue(name, out idx))
        {
            faceMesh.SetBlendShapeWeight(idx, Mathf.Clamp01(weightNormalized) * MaxWeight);
        }
    }
}
