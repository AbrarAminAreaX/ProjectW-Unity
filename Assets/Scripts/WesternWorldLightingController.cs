using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// Day / Night / Brightness controller for the Western_World_spiritGuardian scene.
// Driven from Flutter via Rob11SceneManager:
//   sendToUnity("SceneManager", "SetDayTime",   "")
//   sendToUnity("SceneManager", "SetNightTime", "")
//   sendToUnity("SceneManager", "BrightnessUp",   "")
//   sendToUnity("SceneManager", "BrightnessDown", "")
//
// Animates: directional light color/intensity, ambient color, fog color/density.
// A user brightness multiplier (0.3 .. 1.6) is applied on top of the preset
// intensity so + / − works in either day or night mode.
public class WesternWorldLightingController : MonoBehaviour
{
    public static WesternWorldLightingController Instance { get; private set; }

    [System.Serializable]
    public struct TimeOfDayPreset
    {
        public Color sunColor;
        public float sunIntensity;
        public Color ambient;
        public Color fog;
        public float fogDensity;
        // Tint applied to the Skybox/Cubemap material. Built-in shader doubles this value,
        // so (0.5,0.5,0.5,0.5) is the neutral mid-gray "no tint" baseline.
        public Color skyTint;
        public float skyExposure;
        // Point-light state. Day usually 0 (off); night turns lanterns on.
        public float pointLightIntensity;
        public float pointLightRange;
    }

    [Header("Scene references")]
    [Tooltip("Main sun. Auto-found via RenderSettings.sun or first directional light if left empty.")]
    public Light directionalLight;

    [Tooltip("Skybox material (Skybox/Cubemap shader). Assigned to RenderSettings.skybox on Start; _Tint and _Exposure are driven here.")]
    public Material skyboxMaterial;

    [Tooltip("Point lights that turn on at night (lanterns, windows, etc.). If left empty, all point lights in the scene are auto-collected on Start.")]
    public Light[] pointLights;

    [Header("Day preset (high noon western)")]
    public TimeOfDayPreset day = new TimeOfDayPreset {
        sunColor    = new Color(1f, 0.94f, 0.82f),
        sunIntensity = 3.6f,
        ambient     = new Color(0.55f, 0.50f, 0.45f),
        fog         = new Color(0.86f, 0.56f, 0.42f),
        fogDensity  = 0.008f,
        skyTint     = new Color(0.55f, 0.50f, 0.50f, 0.5f),
        skyExposure = 1.4f,
        pointLightIntensity = 0f,
        pointLightRange     = 0f,
    };

    [Header("Night preset (moonlit cool)")]
    public TimeOfDayPreset night = new TimeOfDayPreset {
        sunColor    = new Color(0.45f, 0.55f, 0.85f),
        sunIntensity = 0.6f,
        ambient     = new Color(0.08f, 0.10f, 0.18f),
        fog         = new Color(0.10f, 0.13f, 0.22f),
        fogDensity  = 0.020f,
        skyTint     = new Color(0.18f, 0.22f, 0.40f, 0.5f),
        skyExposure = 0.35f,
        pointLightIntensity = 2f,
        pointLightRange     = 3f,
    };

    [Header("Brightness step")]
    [Tooltip("Each + / − press multiplies the user brightness by this amount.")]
    public float brightnessStep    = 1.2f;
    public float brightnessMin     = 0.3f;
    public float brightnessMax     = 1.6f;

    [Header("Transition")]
    public float transitionDuration = 1.2f;

    private TimeOfDayPreset _activePreset;
    private float           _userBrightness = 1f;
    private Coroutine       _routine;
    private bool            _initialized;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        if (directionalLight == null) directionalLight = RenderSettings.sun;
        if (directionalLight == null)
        {
            foreach (var l in FindObjectsOfType<Light>())
                if (l.type == LightType.Directional) { directionalLight = l; break; }
        }
        if (skyboxMaterial != null) RenderSettings.skybox = skyboxMaterial;

        if (pointLights == null || pointLights.Length == 0)
        {
            var found = new System.Collections.Generic.List<Light>();
            foreach (var l in FindObjectsOfType<Light>())
                if (l.type == LightType.Point) found.Add(l);
            pointLights = found.ToArray();
        }

        _activePreset = day;
        _initialized  = true;
        ApplyImmediate(_activePreset, _userBrightness);
    }

    // ── Public API ───────────────────────────────────────────────────────

    [ContextMenu("TEST: Set Day")]
    public void SetDay()   { TransitionTo(day);   }

    [ContextMenu("TEST: Set Night")]
    public void SetNight() { TransitionTo(night); }

    [ContextMenu("TEST: Brightness Up")]
    public void BrightnessUp()
    {
        _userBrightness = Mathf.Min(brightnessMax, _userBrightness * brightnessStep);
        ApplyImmediate(_activePreset, _userBrightness);
    }

    [ContextMenu("TEST: Brightness Down")]
    public void BrightnessDown()
    {
        _userBrightness = Mathf.Max(brightnessMin, _userBrightness / brightnessStep);
        ApplyImmediate(_activePreset, _userBrightness);
    }

#if UNITY_EDITOR
    // Quick test keys (Game view must be focused):
    //   D = Day    N = Night    + / = → brighter    - → darker
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.D))           SetDay();
        if (Input.GetKeyDown(KeyCode.N))           SetNight();
        if (Input.GetKeyDown(KeyCode.Equals) ||
            Input.GetKeyDown(KeyCode.KeypadPlus))  BrightnessUp();
        if (Input.GetKeyDown(KeyCode.Minus) ||
            Input.GetKeyDown(KeyCode.KeypadMinus)) BrightnessDown();
    }
#endif

    // ── Implementation ──────────────────────────────────────────────────

    private void TransitionTo(TimeOfDayPreset target)
    {
        if (!_initialized) return;
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(LerpTo(target, transitionDuration));
    }

    private IEnumerator LerpTo(TimeOfDayPreset target, float duration)
    {
        TimeOfDayPreset from = _activePreset;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
            var mid = Lerp(from, target, k);
            Apply(mid, _userBrightness);
            yield return null;
        }
        _activePreset = target;
        Apply(target, _userBrightness);
    }

    private void ApplyImmediate(TimeOfDayPreset p, float brightness)
    {
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        Apply(p, brightness);
    }

    private void Apply(TimeOfDayPreset p, float brightness)
    {
        if (directionalLight != null)
        {
            directionalLight.color     = p.sunColor;
            directionalLight.intensity = p.sunIntensity * brightness;
        }
        RenderSettings.ambientLight = p.ambient * brightness;
        RenderSettings.fogColor     = p.fog;
        RenderSettings.fogDensity   = p.fogDensity;
        if (skyboxMaterial != null)
        {
            skyboxMaterial.SetColor("_Tint",     p.skyTint);
            skyboxMaterial.SetFloat("_Exposure", p.skyExposure * brightness);
        }
        if (pointLights != null)
        {
            float pli = p.pointLightIntensity * brightness;
            for (int i = 0; i < pointLights.Length; i++)
            {
                var l = pointLights[i];
                if (l == null) continue;
                l.intensity = pli;
                l.range     = p.pointLightRange;
            }
        }
        DynamicGI.UpdateEnvironment();
    }

    private static TimeOfDayPreset Lerp(TimeOfDayPreset a, TimeOfDayPreset b, float k)
    {
        return new TimeOfDayPreset {
            sunColor     = Color.Lerp(a.sunColor, b.sunColor, k),
            sunIntensity = Mathf.Lerp(a.sunIntensity, b.sunIntensity, k),
            ambient      = Color.Lerp(a.ambient, b.ambient, k),
            fog          = Color.Lerp(a.fog, b.fog, k),
            fogDensity   = Mathf.Lerp(a.fogDensity, b.fogDensity, k),
            skyTint      = Color.Lerp(a.skyTint, b.skyTint, k),
            skyExposure  = Mathf.Lerp(a.skyExposure, b.skyExposure, k),
            pointLightIntensity = Mathf.Lerp(a.pointLightIntensity, b.pointLightIntensity, k),
            pointLightRange     = Mathf.Lerp(a.pointLightRange,     b.pointLightRange,     k),
        };
    }
}
