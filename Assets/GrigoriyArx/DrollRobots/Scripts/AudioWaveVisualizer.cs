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
    public float lineWidth = 0.008f;
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
        if (_audioSource == null || _lineRenderer == null) return;

        // Get spectrum data
        _audioSource.GetSpectrumData(_spectrumData, 0, FFTWindow.BlackmanHarris);

        float amplitude = 0f;
        for (int i = 0; i < _spectrumData.Length; i++)
            amplitude += _spectrumData[i];
        amplitude = Mathf.Clamp01(amplitude * 10f);

        _currentAmplitude = Mathf.Lerp(_currentAmplitude, amplitude, Time.deltaTime / smoothSpeed);

        for (int i = 0; i < resolution; i++)
        {
            float target = _spectrumData[i] * waveHeight * 20f;
            _smoothedData[i] = Mathf.Lerp(_smoothedData[i], target, Time.deltaTime / smoothSpeed);
        }

        UpdateLinePositions();
        UpdateLineColor();
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

        Color currentColor = Color.Lerp(silentColor, activeColor, _currentAmplitude);
        _lineMaterial.color = currentColor;

        float width = Mathf.Lerp(lineWidth, lineWidth * 2f, _currentAmplitude);
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
