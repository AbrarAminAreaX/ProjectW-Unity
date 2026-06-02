using UnityEngine;

/// <summary>
/// Draws a waveform line on the robot's mouth that resonates with speech audio.
/// Attach to the Rob11 robot GameObject (same one with AudioSource).
/// Follows the mouth position every frame without inheriting bone rotation.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class AudioWaveVisualizer : MonoBehaviour
{
    [Header("Line Settings")]
    [Tooltip("Line thickness in world units. Smaller = thinner.")]
    public float lineWidth = 0.0035f;
    public float waveWidth = 0.3f;
    public float waveHeight = 0.08f;
    public int resolution = 64;

    [Header("Mouth Position")]
    [Tooltip("Drag the MouthEmo or MouthSpeech child object here")]
    public Transform mouthTransform;
    [Tooltip("Offset from the mouth position (in world-oriented local space)")]
    public Vector3 offset = new Vector3(0f, 0f, 0.05f);

    [Header("Colors")]
    public Color silentColor = new Color(0.2f, 0.6f, 1f, 0.5f);
    public Color activeColor = new Color(0.2f, 0.8f, 1f, 1f);
    [Tooltip("Optional: drag a renderer whose material color represents the current eye color. The wave will tint to match (silentColor = dimmer version of it, activeColor = the color itself).")]
    public Renderer eyeColorSource;
    [Tooltip("Alpha to apply to the silent (base) color when sampling from eyeColorSource.")]
    [Range(0f, 1f)] public float silentAlpha = 0.5f;
    [Tooltip("Brightness multiplier for silentColor relative to eyeColorSource color. <1 = dimmer.")]
    [Range(0.1f, 1f)] public float silentDim = 0.55f;

    [Header("Smoothing")]
    [Range(0.01f, 0.5f)]
    public float smoothSpeed = 0.08f;

    private GameObject _lineObj;
    private LineRenderer _lineRenderer;
    private AudioSource _audioSource;
    private float[] _spectrumData;
    private float[] _smoothedData;
    private float _currentAmplitude;
    private Material _lineMaterial;
    private bool _visible;

    // External amplitude path (used when audio plays outside Unity's AudioSource —
    // e.g. via Android AudioTrack in the LiveChat pipeline). Kept as "recent"
    // for a short window so that once chunks stop arriving the visualizer
    // gracefully falls back to the idle animation.
    private float _externalAmplitude;
    private float _externalAmpLastUpdate = -999f;
    private const float ExternalAmplitudeFreshness = 0.3f; // seconds
    private System.Random _noiseRng = new System.Random();

    void Start()
    {
        _audioSource = GetComponent<AudioSource>();
        _spectrumData = new float[resolution];
        _smoothedData = new float[resolution];

        // Create as a root object (not parented to bone) to avoid bone rotation issues
        _lineObj = new GameObject("AudioWaveLine");
        _lineRenderer = _lineObj.AddComponent<LineRenderer>();
        _lineRenderer.positionCount = resolution;
        _lineRenderer.startWidth = lineWidth;
        _lineRenderer.endWidth = lineWidth;
        _lineRenderer.useWorldSpace = true;
        _lineRenderer.numCornerVertices = 4;
        _lineRenderer.numCapVertices = 4;

        _lineMaterial = new Material(Shader.Find("Sprites/Default"));
        _lineMaterial.color = silentColor;
        _lineRenderer.material = _lineMaterial;

        // Start hidden — shown when speech begins
        _lineObj.SetActive(false);
        _visible = false;

        var gradient = new Gradient();
        gradient.SetKeys(
            new[] {
                new GradientColorKey(silentColor, 0f),
                new GradientColorKey(activeColor, 0.5f),
                new GradientColorKey(silentColor, 1f)
            },
            new[] {
                new GradientAlphaKey(0.3f, 0f),
                new GradientAlphaKey(1f, 0.5f),
                new GradientAlphaKey(0.3f, 1f)
            }
        );
        _lineRenderer.colorGradient = gradient;
    }

    void LateUpdate()
    {
        if (_lineRenderer == null) return;

        float amplitude;
        bool useExternal = Time.time - _externalAmpLastUpdate < ExternalAmplitudeFreshness;

        if (useExternal)
        {
            // Amplitude comes from native audio pipeline (LiveChat). Synthesize
            // spectrum-like data by shaping noise by the RMS so the wave shimmers.
            amplitude = Mathf.Clamp01(_externalAmplitude * 3f);
            for (int i = 0; i < resolution; i++)
            {
                float noise = (float)_noiseRng.NextDouble() - 0.5f; // -0.5..0.5
                float target = noise * amplitude * waveHeight * 1.2f;
                _smoothedData[i] = Mathf.Lerp(_smoothedData[i], target, Time.deltaTime / smoothSpeed);
            }
        }
        else if (_audioSource != null)
        {
            // Fallback: sample Unity's AudioSource (legacy / Convai-style path).
            _audioSource.GetSpectrumData(_spectrumData, 0, FFTWindow.BlackmanHarris);

            amplitude = 0f;
            for (int i = 0; i < _spectrumData.Length; i++)
                amplitude += _spectrumData[i];
            amplitude = Mathf.Clamp01(amplitude * 10f);

            for (int i = 0; i < resolution; i++)
            {
                float target = _spectrumData[i] * waveHeight * 20f;
                _smoothedData[i] = Mathf.Lerp(_smoothedData[i], target, Time.deltaTime / smoothSpeed);
            }
        }
        else
        {
            amplitude = 0f;
        }

        _currentAmplitude = Mathf.Lerp(_currentAmplitude, amplitude, Time.deltaTime / smoothSpeed);

        UpdateLinePositions();
        UpdateLineColor();
    }

    /// <summary>
    /// Feed in an externally-measured amplitude (0..1). When called recently,
    /// takes precedence over AudioSource spectrum sampling.
    /// </summary>
    public void SetExternalAmplitude(float amplitude)
    {
        _externalAmplitude = Mathf.Clamp01(amplitude);
        _externalAmpLastUpdate = Time.time;
    }

    void UpdateLinePositions()
    {
        // Get mouth world position
        Vector3 center;
        Vector3 right;
        Vector3 up;

        if (mouthTransform != null)
        {
            // Use the robot's forward direction, not the bone's
            center = mouthTransform.position + transform.forward * offset.z
                                              + transform.up * offset.y
                                              + transform.right * offset.x;
            right = transform.right;
            up = transform.up;
        }
        else
        {
            center = transform.position + offset;
            right = transform.right;
            up = transform.up;
        }

        float halfWidth = waveWidth / 2f;

        for (int i = 0; i < resolution; i++)
        {
            float t = (float)i / (resolution - 1);
            float x = Mathf.Lerp(-halfWidth, halfWidth, t);

            float y = _smoothedData[i];

            if (_currentAmplitude < 0.01f)
            {
                float idleWave = Mathf.Sin(t * Mathf.PI * 4f + Time.time * 2f) * 0.005f;
                y = idleWave;
            }

            float edgeFade = Mathf.Sin(t * Mathf.PI);
            y *= edgeFade;

            Vector3 worldPos = center + right * x + up * y;
            _lineRenderer.SetPosition(i, worldPos);
        }
    }

    void UpdateLineColor()
    {
        if (_lineMaterial == null) return;

        // If an eye-color renderer is wired, derive colors from it so the
        // wave matches the robot's current eye tint (which changes per
        // emotion). Otherwise use the manually-set Inspector colors.
        Color baseActive = activeColor;
        Color baseSilent = silentColor;
        if (eyeColorSource != null && eyeColorSource.sharedMaterial != null)
        {
            // Rob11 tints emotion via _EmissionColor on body/eyes/mouth
            // renderers (see Rob11ColorManager.ApplyEmissionColor). Try
            // _EmissionColor first, fall back to _BaseColor / _Color.
            var mat = eyeColorSource.material; // instance copy so we read current runtime values
            Color eye = activeColor;
            if (mat.HasProperty("_EmissionColor"))
            {
                eye = mat.GetColor("_EmissionColor");
            }
            else if (mat.HasProperty("_BaseColor"))
            {
                eye = mat.GetColor("_BaseColor");
            }
            else if (mat.HasProperty("_Color"))
            {
                eye = mat.color;
            }
            // Normalize brightness so super-bright HDR emission doesn't
            // produce a pure-white wave. Clamp each channel to [0,1].
            float maxC = Mathf.Max(eye.r, eye.g, eye.b, 1f);
            eye = new Color(eye.r / maxC, eye.g / maxC, eye.b / maxC, 1f);

            baseActive = new Color(eye.r, eye.g, eye.b, 1f);
            baseSilent = new Color(eye.r * silentDim, eye.g * silentDim, eye.b * silentDim, silentAlpha);
        }

        Color currentColor = Color.Lerp(baseSilent, baseActive, _currentAmplitude);
        _lineMaterial.color = currentColor;

        // Also update the line gradient (it's set once in Start() with the
        // Inspector colors) so the stripe-fade look follows the eye color.
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] {
                new GradientColorKey(baseSilent, 0f),
                new GradientColorKey(baseActive, 0.5f),
                new GradientColorKey(baseSilent, 1f),
            },
            new[] {
                new GradientAlphaKey(0.3f, 0f),
                new GradientAlphaKey(1f, 0.5f),
                new GradientAlphaKey(0.3f, 1f),
            }
        );
        _lineRenderer.colorGradient = gradient;

        float width = Mathf.Lerp(lineWidth, lineWidth * 1.5f, _currentAmplitude);
        _lineRenderer.startWidth = width;
        _lineRenderer.endWidth = width;
    }

    public void Show()
    {
        if (_lineObj != null && !_visible)
        {
            _lineObj.SetActive(true);
            _visible = true;
        }
    }

    public void Hide()
    {
        if (_lineObj != null && _visible)
        {
            _lineObj.SetActive(false);
            _visible = false;
        }
    }

    public bool IsVisible => _visible;

    void OnDestroy()
    {
        if (_lineObj != null)
            Destroy(_lineObj);
        if (_lineMaterial != null)
            Destroy(_lineMaterial);
    }
}
