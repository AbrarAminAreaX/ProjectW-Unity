using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene controller for Rob11Scene when embedded in Flutter.
/// Forwards Gemini audio messages from Flutter to the GeminiAudioBridge.
///
/// The GameObject MUST be named "SceneManager" so that Flutter's
/// sendToUnity("SceneManager", ...) reaches it.
/// </summary>
public class Rob11SceneManager : MonoBehaviour
{
    [Header("Gemini Audio Bridge")]
    public GeminiAudioBridge audioBridge;

    void Start()
    {
        SendToFlutter.Send("scene_loaded");
        SendToFlutter.Send("ready_for_audio");
    }

    public void LoadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;
        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    // ── Gemini Audio Messages from Flutter ──

    public void PlayGeminiAudio(string base64Audio)
    {
        if (audioBridge != null)
            audioBridge.PlayAudio(base64Audio);
        else
            Debug.LogError("[SceneManager] GeminiAudioBridge not assigned!");
    }

    public void SetAIResponse(string responseText)
    {
        if (audioBridge != null)
            audioBridge.SetAIResponse(responseText);
    }

    public void StopAudio(string message)
    {
        if (audioBridge != null)
            audioBridge.StopAudio();
    }

    // ── Stubs ──
    public void EnableAR(string message) { }
    public void DisableAR(string message) { }
    public void ToggleBackground(string message) { }
    public void SetRobotAnimation(string message) { }
    public void ResetARPlacement(string message) { }

    public void GetAnimationList(string message)
    {
        SendToFlutter.Send("animation_list:");
    }
}
