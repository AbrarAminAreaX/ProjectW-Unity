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

      // Flutter forwards the on-screen joystick as "x,y" (normalized, [-1..1]).
      // The embedded Unity view doesn't reliably deliver drag touches, so the
      // joystick is driven from a Flutter widget instead.
      //   sendToUnity("SceneManager", "SetJoystick", "0.5,-0.3")  // "0,0" on release
      public void SetJoystick(string xy)
      {
          if (SpiritGuardianJoystickController.Instance == null || string.IsNullOrEmpty(xy)) return;
          var parts = xy.Split(',');
          if (parts.Length != 2) return;
          float x, y;
          if (float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                  System.Globalization.CultureInfo.InvariantCulture, out x) &&
              float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                  System.Globalization.CultureInfo.InvariantCulture, out y))
          {
              SpiritGuardianJoystickController.Instance.SetExternalInput(x, y);
          }
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

      // ── Western World day/night/brightness ─────────────────────────────
      //   sendToUnity("SceneManager", "SetDayTime",     "")
      //   sendToUnity("SceneManager", "SetNightTime",   "")
      //   sendToUnity("SceneManager", "BrightnessUp",   "")
      //   sendToUnity("SceneManager", "BrightnessDown", "")
      public void SetDayTime(string _ignored)
      {
          if (WesternWorldLightingController.Instance != null)
              WesternWorldLightingController.Instance.SetDay();
      }
      public void SetNightTime(string _ignored)
      {
          if (WesternWorldLightingController.Instance != null)
              WesternWorldLightingController.Instance.SetNight();
      }
      public void BrightnessUp(string _ignored)
      {
          if (WesternWorldLightingController.Instance != null)
              WesternWorldLightingController.Instance.BrightnessUp();
      }
      public void BrightnessDown(string _ignored)
      {
          if (WesternWorldLightingController.Instance != null)
              WesternWorldLightingController.Instance.BrightnessDown();
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
