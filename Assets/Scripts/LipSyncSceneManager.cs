using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene controller for LipSync Sample when embedded in Flutter.
/// Attach to a root GameObject named "SceneManager".
/// </summary>
public class LipSyncSceneManager : MonoBehaviour
{
    void Start()
    {
        SendToFlutter.Send("scene_loaded");
    }

    public void LoadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;
        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

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
