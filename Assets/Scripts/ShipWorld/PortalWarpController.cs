using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// Builds a full-screen Screen-Space-Overlay portal at runtime (no scene wiring)
/// and animates the "UI/PortalWarp" shader's _Progress 0->1. The onPeak callback
/// fires once near full coverage — that's where the ShipInteractionManager tells
/// Flutter to navigate, so the Flutter screen appears under the portal peak.
///
/// After a short hold it resets to hidden, so when the user pops back from the
/// Flutter feature the Ship World is visible again (not a frozen portal).
public class PortalWarpController : MonoBehaviour
{
    [Tooltip("Seconds for the portal to fully open.")]
    public float openDuration = 0.85f;

    [Tooltip("Fraction of openDuration at which navigation fires (near full " +
             "coverage). 0.8 = fire at 80% open.")]
    [Range(0.5f, 1f)] public float peakFraction = 0.82f;

    [Tooltip("Seconds to hold full coverage after peak before resetting hidden " +
             "(happens behind the Flutter screen).")]
    public float holdAfterPeak = 0.4f;

    private Canvas _canvas;
    private RawImage _image;
    private Material _mat;
    private bool _playing;

    public bool IsPlaying => _playing;

    /// Creates a PortalWarpController on its own GameObject. Call once.
    public static PortalWarpController Create()
    {
        var go = new GameObject("PortalWarpController");
        return go.AddComponent<PortalWarpController>();
    }

    private void EnsureUI()
    {
        if (_canvas != null) return;

        // Overlay canvas on top of everything (incl. the joystick UI).
        var canvasGo = new GameObject("PortalWarpCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 32760; // above gameplay UI

        var shader = Shader.Find("UI/PortalWarp");
        if (shader == null)
            Debug.LogError("[PortalWarp] Shader 'UI/PortalWarp' not found. " +
                           "Ensure Assets/Shaders/PortalWarp.shader is in the project.");
        _mat = new Material(shader);

        var imgGo = new GameObject("PortalImage", typeof(RawImage));
        imgGo.transform.SetParent(canvasGo.transform, false);
        _image = imgGo.GetComponent<RawImage>();
        _image.material = _mat;
        _image.raycastTarget = false;

        // Stretch full screen.
        var rt = _image.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        SetProgress(0f);
        canvasGo.SetActive(false);
    }

    private void SetProgress(float p)
    {
        if (_mat != null) _mat.SetFloat("_Progress", p);
    }

    /// Plays the warp. onPeak is invoked once at peakFraction.
    public void Play(Action onPeak)
    {
        if (_playing) return;
        EnsureUI();
        StartCoroutine(PlayRoutine(onPeak));
    }

    private IEnumerator PlayRoutine(Action onPeak)
    {
        _playing = true;
        _canvas.gameObject.SetActive(true);

        float t = 0f;
        bool peakFired = false;
        while (t < openDuration)
        {
            t += Time.unscaledDeltaTime; // works even if gameplay pauses time
            float p = Mathf.Clamp01(t / openDuration);
            SetProgress(p);

            if (!peakFired && p >= peakFraction)
            {
                peakFired = true;
                onPeak?.Invoke();
            }
            yield return null;
        }
        SetProgress(1f);
        if (!peakFired) onPeak?.Invoke();

        yield return new WaitForSecondsRealtime(holdAfterPeak);

        // Reset hidden (behind the now-presented Flutter screen).
        _canvas.gameObject.SetActive(false);
        SetProgress(0f);
        _playing = false;
    }
}
