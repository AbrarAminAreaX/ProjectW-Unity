using UnityEngine;                                                                                                                                                                                              
  using UnityEngine.SceneManagement;                                                                                                                                                                              
  //using FlutterEmbedUnity;  // for SendToFlutter                                                                                                                                                                  
                                                                                                                                                                                                                  
  public class Rob11SceneManager : MonoBehaviour                                                                                                                                                                  
  {                                                      
      // Field name matches the serialized reference already in Rob11Scene.unity
      // (audioBridge), so the Inspector-wired link survives without re-dragging.
      [Tooltip("GeminiAudioBridge on the Robot GameObject")]
      public GeminiAudioBridge audioBridge;

      void Start()
      {
          SendToFlutter.Send("scene_loaded");
      }

      public void LoadScene(string sceneName)
      {
          SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
      }

      public void SetAIResponse(string text)
      {
          if (audioBridge != null) audioBridge.SetAIResponse(text);
      }

      public void PlayGeminiAudio(string base64Mp3)
      {
          if (audioBridge != null) audioBridge.PlayAudio(base64Mp3);
      }

      public void StopAudio()
      {
          if (audioBridge != null) audioBridge.StopAudio();
      }

      // Flutter invokes these when the native LiveChat pipeline starts/stops
      // playing AI audio, so the bridge can show/hide the mouth wave visualizer.
      public void OnSpeakingStart(string _ignored)
      {
          if (audioBridge != null) audioBridge.OnSpeakingStart();
      }

      public void OnSpeakingEnd(string _ignored)
      {
          if (audioBridge != null) audioBridge.OnSpeakingEnd();
      }

      // ── Spirit guardian (Western_World_spiritGuardian scene) ────────────
      // Direct Flutter triggers for the guardian's playful fly/hide loop.
      //   sendToUnity("SceneManager", "SummonGuardian", "")
      //   sendToUnity("SceneManager", "DismissGuardian", "")
      //   sendToUnity("SceneManager", "UserTranscript", "where are you...")
      public void SummonGuardian(string _ignored)
      {
          if (SpiritGuardianFlyController.Instance != null)
              SpiritGuardianFlyController.Instance.Summon();
      }

      public void DismissGuardian(string _ignored)
      {
          if (SpiritGuardianFlyController.Instance != null)
              SpiritGuardianFlyController.Instance.SendBackToHiding();
      }

      // Optional: pass the USER's transcript so the guardian reacts to what
      // the player says (more reliable than scanning Kai's reply).
      public void UserTranscript(string text)
      {
          if (SpiritGuardianFlyController.Instance != null && !string.IsNullOrEmpty(text))
              SpiritGuardianFlyController.Instance.HandleAIText(text.ToLowerInvariant());
      }

      // Flutter streams a float (as string) with each audio chunk; we use it
      // to drive the wave visualizer amplitude.
      public void OnAudioAmplitude(string amplitudeStr)
      {
          if (audioBridge == null) return;
          float amp;
          if (float.TryParse(amplitudeStr,
              System.Globalization.NumberStyles.Float,
              System.Globalization.CultureInfo.InvariantCulture,
              out amp))
          {
              audioBridge.SetAmplitude(amp);
          }
      }
  }
