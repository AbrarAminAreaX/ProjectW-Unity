using UnityEngine;

/// <summary>
/// Cheap lip sync: opens the character's jaw proportional to incoming audio
/// RMS amplitude (fed from native via Flutter). Not viseme-aware — mouth
/// flaps but doesn't form distinct A/E/O shapes. Good enough for chatty
/// robots or mobile-budget characters; upgrade to uLipSync later if needed.
///
/// Drag the face SkinnedMeshRenderer into `faceMesh` in the Inspector. The
/// blend-shape name defaults to `jawOpen` (ARKit 52 / RPM) — change if your
/// mesh uses a different name.
/// </summary>
public class AmplitudeLipSync : MonoBehaviour
{
    [Tooltip("The RPM / ARKit face mesh containing the jaw-open blend shape.")]
    public SkinnedMeshRenderer faceMesh;

    [Tooltip("Blend shape to drive. RPM/ARKit default is 'jawOpen'.")]
    public string jawOpenBlendShape = "jawOpen";

    [Tooltip("Multiplier applied to incoming amplitude (0..1) before writing to the blend shape. >1 makes mouth open wider for the same loudness.")]
    public float gain = 3.5f;

    [Tooltip("How fast the mouth returns toward the current amplitude target. Higher = snappier.")]
    public float lerpSpeed = 25f;

    [Tooltip("Seconds without a new amplitude sample before the mouth auto-closes.")]
    public float freshnessWindow = 0.3f;

    private int _jawIdx = -1;
    private float _currentWeight; // 0..1 normalized
    private float _targetAmplitude; // 0..1
    private float _lastUpdate = -999f;

    void Awake()
    {
        ResolveIndex();
    }

    void OnEnable()
    {
        ResolveIndex();
    }

    private void ResolveIndex()
    {
        _jawIdx = -1;
        if (faceMesh == null || faceMesh.sharedMesh == null) return;
        var mesh = faceMesh.sharedMesh;
        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            if (mesh.GetBlendShapeName(i) == jawOpenBlendShape)
            {
                _jawIdx = i;
                return;
            }
        }
        Debug.LogWarning($"[AmplitudeLipSync] Blend shape '{jawOpenBlendShape}' not found on {faceMesh.name}.");
    }

    /// <summary>
    /// Push a new RMS amplitude (0..1). Called from the scene manager every
    /// audio chunk arriving from native.
    /// </summary>
    public void SetAmplitude(float amplitude)
    {
        _targetAmplitude = Mathf.Clamp01(amplitude * gain);
        _lastUpdate = Time.time;
    }

    /// <summary>
    /// Force the mouth closed right now (e.g. when speaking_end arrives).
    /// </summary>
    public void Close()
    {
        _targetAmplitude = 0f;
    }

    void LateUpdate()
    {
        if (_jawIdx < 0 || faceMesh == null) return;

        // Auto-close if no amplitude has arrived for a while.
        float stale = Time.time - _lastUpdate;
        float goal = stale > freshnessWindow ? 0f : _targetAmplitude;

        _currentWeight = Mathf.MoveTowards(_currentWeight, goal, lerpSpeed * Time.deltaTime);
        faceMesh.SetBlendShapeWeight(_jawIdx, _currentWeight * 100f);
    }
}
