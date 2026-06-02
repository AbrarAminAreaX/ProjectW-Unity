using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

// Flutter ↔ Unity bridge for Addressables-hosted mini games.
//
// Attach to the same GameObject named "SceneManager" in Bootstrap.unity that
// already hosts BootstrapManager — Flutter calls reach this script via
// sendToUnity("SceneManager", "<method>", "<arg>"). Replies travel back via
// SendToFlutter.Send(...) using a "topic|payload" string convention so the
// Dart side can route messages without JSON parsing in the hot path.
//
//   download_progress|<key>|<0..1 float>
//   download_complete|<key>
//   download_error|<key>|<message>
//   scene_loaded|<key>
//   scene_load_error|<key>|<message>
//
// Flutter messages this script accepts:
//   DownloadAddressable    payload = key
//   LoadAddressableScene   payload = key
//   UnloadCurrentScene     payload = "" (returns to Bootstrap)
public class AddressableLoader : MonoBehaviour
{
    private SceneInstance _currentScene;
    private bool _hasLoadedScene;

    // Lives on its own "AddressableHost" GameObject (separate from
    // SceneManager) so it survives Bootstrap → Rob11 → MiniGame scene
    // switches without colliding with other "SceneManager" GameObjects.
    void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        // Lets Flutter know the bridge is alive — the MiniGames list waits
        // for this before enabling Download buttons.
        SendToFlutter.Send("addressable_host_ready");
    }

    public void DownloadAddressable(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            SendToFlutter.Send("download_error||empty key");
            return;
        }
        StartCoroutine(DownloadRoutine(key));
    }

    private IEnumerator DownloadRoutine(string key)
    {
        var sizeOp = Addressables.GetDownloadSizeAsync(key);
        yield return sizeOp;

        // sizeOp.Result is bytes still to download (0 if already cached).
        // We still progress through the dependencies handle so the UI gets
        // a deterministic "complete" event even on cache hit.
        var op = Addressables.DownloadDependenciesAsync(key, autoReleaseHandle: false);
        while (!op.IsDone)
        {
            float pct = op.PercentComplete; // 0..1
            SendToFlutter.Send($"download_progress|{key}|{pct.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}");
            yield return new WaitForSeconds(0.1f);
        }

        if (op.Status == AsyncOperationStatus.Succeeded)
        {
            SendToFlutter.Send($"download_progress|{key}|1.000");
            SendToFlutter.Send($"download_complete|{key}");
        }
        else
        {
            string msg = op.OperationException != null ? op.OperationException.Message : "unknown";
            SendToFlutter.Send($"download_error|{key}|{msg}");
        }
        Addressables.Release(op);
        Addressables.Release(sizeOp);
    }

    public void LoadAddressableScene(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            SendToFlutter.Send("scene_load_error||empty key");
            return;
        }
        StartCoroutine(LoadSceneRoutine(key));
    }

    private IEnumerator LoadSceneRoutine(string key)
    {
        // Single mode replaces the current scene — Bootstrap unloads, the
        // mini game scene becomes the active one. We retain the handle so
        // we can release it cleanly on UnloadCurrentScene.
        var op = Addressables.LoadSceneAsync(key, LoadSceneMode.Single);
        yield return op;
        if (op.Status == AsyncOperationStatus.Succeeded)
        {
            _currentScene = op.Result;
            _hasLoadedScene = true;
            SendToFlutter.Send($"scene_loaded|{key}");
        }
        else
        {
            string msg = op.OperationException != null ? op.OperationException.Message : "unknown";
            SendToFlutter.Send($"scene_load_error|{key}|{msg}");
        }
    }

    public void UnloadCurrentScene(string _ignored)
    {
        if (!_hasLoadedScene) return;
        StartCoroutine(UnloadRoutine());
    }

    private IEnumerator UnloadRoutine()
    {
        var op = Addressables.UnloadSceneAsync(_currentScene);
        yield return op;
        _hasLoadedScene = false;
    }
}
