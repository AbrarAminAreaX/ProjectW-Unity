using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;

/// <summary>
/// Applies the home background chosen in Flutter to a Unity Canvas.
///
/// Flutter sends the selected/loaded background URL with:
///     sendToUnity("SceneManager", "SetBackground", "&lt;file_url&gt;");
/// (the same string held by currentHomeBackgroundProvider). This component
/// downloads that image and shows it on a UI Image behind the robot.
///
/// Put it on the GameObject named "SceneManager" in each scene that has a
/// backdrop (alongside the scene controller) so the Flutter message reaches it.
/// Assign 'targetImage' to your existing background Image (e.g. "Background_Image").
/// If left empty and 'autoCreateBackground' is on, it builds a full-screen
/// background Canvas + Image.
///
/// Mirrors the SendToFlutter / SceneManager message pattern used by
/// BootstrapManager and GeminiAudioBridge.
/// </summary>
public class UnityCanvasBackground : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The UI Image that displays the background (e.g. your 'Background_Image'). " +
             "Leave empty to auto-build one if 'autoCreateBackground' is on.")]
    public Image targetImage;

    [Tooltip("If no targetImage is assigned, build a full-screen background Canvas at runtime.")]
    public bool autoCreateBackground = true;

    [Tooltip("Sorting order for the auto-built background Canvas. Keep it low so it sits behind everything else.")]
    public int backgroundSortingOrder = -100;

    [Header("Transition")]
    [Tooltip("Seconds to crossfade from the old background to the new one (0 = instant).")]
    public float fadeDuration = 0.35f;

    private string _currentUrl;         // last applied URL — skips redundant downloads
    private Coroutine _loadRoutine;
    private Texture2D _currentTexture;  // owned texture, destroyed on swap
    private Sprite _currentSprite;      // owned sprite, destroyed on swap

    // ------------------------------------------------------------------
    //  Flutter entry point — sendToUnity("SceneManager", "SetBackground", url)
    // ------------------------------------------------------------------
    public void SetBackground(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        if (url == _currentUrl) return;            // already showing it
        _currentUrl = url;

        if (targetImage == null && autoCreateBackground)
            BuildBackgroundCanvas();

        if (targetImage == null)
        {
            Debug.LogWarning("[UnityCanvasBackground] No targetImage assigned and autoCreateBackground is off.");
            return;
        }

        if (_loadRoutine != null) StopCoroutine(_loadRoutine);
        _loadRoutine = StartCoroutine(DownloadAndApply(url));
    }

    private IEnumerator DownloadAndApply(string url)
    {
        using (var req = UnityWebRequestTexture.GetTexture(url))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[UnityCanvasBackground] Failed to load '{url}': {req.error}");
                yield break;
            }

            var tex = DownloadHandlerTexture.GetContent(req);
            yield return ApplyTexture(tex);
        }
    }

    private IEnumerator ApplyTexture(Texture2D tex)
    {
        // Wrap the downloaded texture in a sprite for the UI Image.
        var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));

        // Hold the outgoing texture + sprite so we can free them after the swap
        // (so backgrounds don't leak memory).
        var oldTex = _currentTexture;
        var oldSprite = _currentSprite;
        _currentTexture = tex;
        _currentSprite = sprite;

        if (fadeDuration > 0f)
        {
            Color c = targetImage.color;
            // Fade out, swap, fade in.
            yield return Fade(c.a, 0f);
            targetImage.sprite = sprite;
            yield return Fade(0f, 1f);
        }
        else
        {
            targetImage.sprite = sprite;
            var c = targetImage.color; c.a = 1f; targetImage.color = c;
        }

        if (oldSprite != null) Destroy(oldSprite);
        if (oldTex != null) Destroy(oldTex);
    }

    private IEnumerator Fade(float from, float to)
    {
        float t = 0f;
        Color c = targetImage.color;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            c.a = Mathf.Lerp(from, to, t / fadeDuration);
            targetImage.color = c;
            yield return null;
        }
        c.a = to;
        targetImage.color = c;
    }

    private void BuildBackgroundCanvas()
    {
        var canvasGo = new GameObject("BackgroundCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = backgroundSortingOrder;   // behind the robot / other UI
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);

        var imgGo = new GameObject("BackgroundImage", typeof(Image));
        imgGo.transform.SetParent(canvasGo.transform, false);
        var rect = imgGo.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;          // stretch full screen
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        targetImage = imgGo.GetComponent<Image>();
        targetImage.raycastTarget = false;
        var c = targetImage.color; c.a = (fadeDuration > 0f) ? 0f : 1f; targetImage.color = c;
    }

    void OnDestroy()
    {
        if (_currentSprite != null) Destroy(_currentSprite);
        if (_currentTexture != null) Destroy(_currentTexture);
    }
}
