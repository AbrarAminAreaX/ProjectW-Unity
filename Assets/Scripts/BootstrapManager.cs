using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Lightweight controller used by Bootstrap.unity (and OpenWorld.unity until
/// a real controller is added).
///
/// Bootstrap.unity is the very first scene Unity loads when embedded inside
/// Flutter. Its only job is to sit on screen as a "loading" placeholder and
/// wait for Flutter to tell it which real scene to load via:
///
///     sendToUnity("SceneManager", "LoadScene", "&lt;sceneName&gt;");
///
/// All other Flutter-callable methods are stubbed as no-ops so that messages
/// sent before the real per-scene controller is loaded don't generate
/// "method not found" warnings in Unity logs.
/// </summary>
public class BootstrapManager : MonoBehaviour
{
    void Start()
    {
        // Tell Flutter the bootstrap scene is up. Flutter already listens
        // for "scene_loaded" to drop its own loading state.
        SendToFlutter.Send("scene_loaded");
    }

    // sendToUnity("SceneManager", "LoadScene", "OpenWorld")
    public void LoadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;
        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    // ----- no-op stubs for messages the main/AR controllers handle -----
    // These exist only so Flutter messages destined for those controllers
    // don't error out if they happen to arrive while Bootstrap (or any
    // scene that hasn't replaced this controller yet) is active.

    public void EnableAR(string message) { }
    public void DisableAR(string message) { }
    public void ToggleBackground(string message) { }
    public void SetRobotAnimation(string message) { }
    public void ResetARPlacement(string message) { }

    public void GetAnimationList(string message)
    {
        // Empty list — no animations available in bootstrap/openworld yet.
        SendToFlutter.Send("animation_list:");
    }
}
