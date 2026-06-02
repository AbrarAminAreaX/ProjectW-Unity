using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Receives Base64 audio + AI response text from Flutter (Gemini Voice API),
/// plays audio through AudioSource, and drives robot reactions.
///
/// Flutter sends:
///   sendToUnity("SceneManager", "PlayGeminiAudio", base64Mp3String)
///   sendToUnity("SceneManager", "SetAIResponse", aiResponseText)
///
/// Unity sends back:
///   SendToFlutter("audio_started")
///   SendToFlutter("audio_done")
///
/// Attach to the robot GameObject that has AudioSource.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class GeminiAudioBridge : MonoBehaviour
{
    [Header("Robot References")]
    public Rob11Ctrl robotController;
    public Animator robotAnimator;

    [Header("Mouth / Wave Toggle")]
    public GameObject mouthEmoObject;
    public AudioWaveVisualizer waveVisualizer;

    [Header("UI")]
    public GameObject silenceButton;
    public GameObject listeningIndicator;

    [Header("Emotion Settings")]
    public float emotionLingerDuration = 2f;
    [Tooltip("Fallback hold duration (seconds) for an animation's bool when we can't query the current state length. The normal path waits for the actual clip length + a small buffer.")]
    public float animationHoldFallback = 1.8f;
    [Tooltip("Extra seconds to hold the bool after the clip ends so the state's Exit Time transition has room to complete.")]
    public float animationHoldTail = 0.15f;

    private AudioSource _audioSource;
    private bool _isPlaying;
    private Coroutine _emotionResetCoroutine;
    private Coroutine _animResetCoroutine;
    private string _activeAnimBool;
    private int _lastEmotionIndex = 0;

    // Tracks which animations have already fired during the current speaking
    // turn. The server streams AI text in many fragments — without this lock
    // each fragment that still contains "haha" (or any other matched keyword)
    // would refire the same clip. Cleared on OnSpeakingEnd().
    private readonly HashSet<string> _firedThisTurn = new HashSet<string>();

    // ── Emotion Map (same as ConvaiRobotEmotionBridge) ──
    private static readonly Dictionary<string, int> TextEmotionKeywords = new Dictionary<string, int>
    {
        // Happy (1)
        {"haha", 1}, {"ha ha", 1}, {"lol", 1}, {"funny", 1}, {"laugh", 1},
        {"great", 1}, {"awesome", 1}, {"wonderful", 1}, {"fantastic", 1},
        {"amazing", 1}, {"excellent", 1}, {"happy", 1}, {"glad", 1}, {"joy", 1},
        {"exciting", 1}, {"hooray", 1}, {"yay", 1}, {"love it", 1},

        // Sad (2)
        {"sorry", 2}, {"unfortunately", 2}, {"sad", 2}, {"miss", 2},
        {"regret", 2}, {"apologize", 2}, {"heartbreak", 2},

        // Angry/Distrust (3)
        {"angry", 3}, {"furious", 3}, {"annoyed", 3}, {"frustrated", 3},
        {"terrible", 3}, {"awful", 3}, {"hate", 3},

        // Wonder/Surprise (4)
        {"wow", 4}, {"whoa", 4}, {"really", 4}, {"incredible", 4},
        {"surprising", 4}, {"unexpected", 4}, {"curious", 4}, {"interesting", 4},
        {"hmm", 4}, {"wonder", 4},

        // Fear (5)
        {"scary", 5}, {"afraid", 5}, {"terrifying", 5}, {"horror", 5},
        {"nervous", 5}, {"worried", 5},

        // Disgust (6)
        {"gross", 6}, {"disgusting", 6}, {"yuck", 6}, {"eww", 6},

        // Love (9)
        {"love", 9}, {"adore", 9}, {"heart", 9}, {"sweetheart", 9},
        {"darling", 9}, {"beautiful", 9},
    };

    // ── Animation keywords (from bot response) ──
    private struct AnimAction
    {
        public string animBool;
        public int vary;
        public int emotionIndex;

        public AnimAction(string animBool, int emotionIndex = -1, int vary = -1)
        {
            this.animBool = animBool;
            this.vary = vary;
            this.emotionIndex = emotionIndex;
        }
    }

    // Order matters: first match wins. Put multi-word / more specific keys
    // BEFORE single-word ones so "i don't know" beats "know", "great job"
    // beats "great", etc. Keywords that risk substring collisions (e.g.
    // "hit", "no") use padding ("hit me", " no ") so they don't false-fire
    // inside unrelated words.
    private static readonly KeyValuePair<string, AnimAction>[] ResponseKeywordAnims = new[]
    {
        // ── Greetings → Hello ─────────────────────────────────────────────
        // All tokens are padded so they don't false-match inside "they",
        // "hello there", "whisper", etc. Match happens against a response
        // wrapped in a leading space (see TriggerKeywordAnimations).
        KV("hello there", new AnimAction("Hello", emotionIndex: 1)),
        KV("hey there",   new AnimAction("Hello", emotionIndex: 1)),
        KV("hello",       new AnimAction("Hello", emotionIndex: 1)),
        KV(" hey ",       new AnimAction("Hello", emotionIndex: 1)),
        KV(" hey,",       new AnimAction("Hello", emotionIndex: 1)),
        KV(" hey!",       new AnimAction("Hello", emotionIndex: 1)),
        KV(" hi ",        new AnimAction("Hello", emotionIndex: 1)),
        KV(" hi,",        new AnimAction("Hello", emotionIndex: 1)),
        KV(" hi!",        new AnimAction("Hello", emotionIndex: 1)),

        // ── Rhetorical setup → Pointing (specific phrases) ────────────────
        KV("knock knock", new AnimAction("Pointing", emotionIndex: 1, vary: 0)),
        KV("look at this", new AnimAction("Pointing", emotionIndex: 4, vary: 0)),
        KV("check this",   new AnimAction("Pointing", emotionIndex: 4, vary: 0)),
        KV("over there",   new AnimAction("Pointing", emotionIndex: 4, vary: 1)),
        KV("right here",   new AnimAction("Pointing", emotionIndex: 4, vary: 2)),

        // ── Laughter → Laught ─────────────────────────────────────────────
        // Padded versions so "lol" doesn't fire inside "follower" / "pollen".
        KV("haha",  new AnimAction("Laught", emotionIndex: 1)),
        KV("ha ha", new AnimAction("Laught", emotionIndex: 1)),
        KV(" lol ", new AnimAction("Laught", emotionIndex: 1)),
        KV(" lol,", new AnimAction("Laught", emotionIndex: 1)),
        KV(" lol.", new AnimAction("Laught", emotionIndex: 1)),
        KV(" lol!", new AnimAction("Laught", emotionIndex: 1)),

        // ── Uncertainty → DontKnow ────────────────────────────────────────
        KV("i don't know", new AnimAction("DontKnow", emotionIndex: 3)),
        KV("not sure",     new AnimAction("DontKnow", emotionIndex: 3)),
        KV("no idea",      new AnimAction("DontKnow", emotionIndex: 3)),

        // ── Searching / questioning → LookingFor ──────────────────────────
        KV("looking for", new AnimAction("LookingFor", emotionIndex: 4)),
        KV("can't find",  new AnimAction("LookingFor", emotionIndex: 4)),
        KV("cant find",   new AnimAction("LookingFor", emotionIndex: 4)),
        KV("searching",   new AnimAction("LookingFor", emotionIndex: 4)),
        KV("where is",    new AnimAction("LookingFor", emotionIndex: 4)),
        KV("have you seen", new AnimAction("LookingFor", emotionIndex: 4)),

        // ── Negation → No (all padded to avoid substring collisions) ──────
        KV("absolutely not", new AnimAction("No", emotionIndex: 3)),
        KV("definitely not", new AnimAction("No", emotionIndex: 3)),
        KV("i don't think",  new AnimAction("No", emotionIndex: 3)),
        KV("nope",           new AnimAction("No", emotionIndex: 3)),
        KV(" no ",           new AnimAction("No", emotionIndex: 3)),
        KV(" no,",           new AnimAction("No", emotionIndex: 3)),
        KV(" no.",           new AnimAction("No", emotionIndex: 3)),

        // ── Victory → Win ─────────────────────────────────────────────────
        KV("congratulations", new AnimAction("Win", emotionIndex: 1)),
        KV("we did it",       new AnimAction("Win", emotionIndex: 1)),
        KV("hooray",          new AnimAction("Win", emotionIndex: 1)),
        KV("awesome",         new AnimAction("Win", emotionIndex: 1)),

        // ── Praise → Thumb ────────────────────────────────────────────────
        KV("great job", new AnimAction("Thumb", emotionIndex: 1)),
        KV("well done", new AnimAction("Thumb", emotionIndex: 1)),
        KV("good job",  new AnimAction("Thumb", emotionIndex: 1)),
        KV("thumbs up", new AnimAction("Thumb", emotionIndex: 9)),

        // ── Anger → Angry ─────────────────────────────────────────────────
        KV("furious",    new AnimAction("Angry", emotionIndex: 3)),
        KV("so angry",   new AnimAction("Angry", emotionIndex: 3)),
        KV("frustrated", new AnimAction("Angry", emotionIndex: 3)),
        KV("annoyed",    new AnimAction("Angry", emotionIndex: 3)),

        // ── Grief → Cry (only intense/explicit grief, not just "sad") ─────
        KV("heartbreak", new AnimAction("Cry", emotionIndex: 8)),
        KV("crying",     new AnimAction("Cry", emotionIndex: 8)),
        KV("so sad",     new AnimAction("Cry", emotionIndex: 8)),
        KV("in tears",   new AnimAction("Cry", emotionIndex: 8)),

        // ── Physical actions → Hit (padded to avoid matches inside words) ─
        KV("hit me",  new AnimAction("Hit", emotionIndex: 3, vary: 0)),
        KV("hit you", new AnimAction("Hit", emotionIndex: 3, vary: 1)),
        KV("punch",   new AnimAction("Hit", emotionIndex: 3, vary: 0)),
        KV("strike",  new AnimAction("Hit", emotionIndex: 3, vary: 1)),

        // ── Dance: Dance0 = general, Dance1 = party ───────────────────────
        KV("let's dance", new AnimAction("Dance0", emotionIndex: 1)),
        KV("dance",       new AnimAction("Dance0", emotionIndex: 1)),
        KV("celebrate",   new AnimAction("Dance1", emotionIndex: 1)),
        KV("party time",  new AnimAction("Dance1", emotionIndex: 1)),
        KV("let's party", new AnimAction("Dance1", emotionIndex: 1)),
    };

    private static KeyValuePair<string, AnimAction> KV(string key, AnimAction val)
    {
        return new KeyValuePair<string, AnimAction>(key, val);
    }

    void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        _audioSource.playOnAwake = false;

        // Force the legacy Convai-era "speech lines" mouth graphic off at all
        // times — we use the wave visualizer for speaking feedback instead.
        ForceHideMouthSpeech();
    }

    void LateUpdate()
    {
        // Animation events on the base robot controller (e.g. the Talk clip
        // calling ToggleObjectActiveState, Speech3End, etc.) can re-enable
        // MouthSpeech at any frame. Enforce off every frame.
        ForceHideMouthSpeech();
    }

    private void ForceHideMouthSpeech()
    {
        if (robotController != null && robotController.MouthSpeech != null
            && robotController.MouthSpeech.activeSelf)
        {
            robotController.MouthSpeech.SetActive(false);
        }
    }

    // ════════════════════════════════════════════
    //  Called from Flutter via sendToUnity
    // ════════════════════════════════════════════

    /// <summary>
    /// Receives Base64-encoded MP3 audio from Flutter and plays it.
    /// Called via: sendToUnity("SceneManager", "PlayGeminiAudio", base64String)
    /// The SceneManager forwards to this method.
    /// </summary>
    public void PlayAudio(string base64Audio)
    {
        if (string.IsNullOrEmpty(base64Audio))
        {
            Debug.LogWarning("[GeminiAudio] Empty audio data received");
            return;
        }

        StartCoroutine(DecodeAndPlayAudio(base64Audio));
    }

    /// <summary>
    /// Receives the AI response text for emotion/animation analysis.
    /// Called via: sendToUnity("SceneManager", "SetAIResponse", text)
    /// </summary>
    public void SetAIResponse(string responseText)
    {
        if (string.IsNullOrEmpty(responseText)) return;

        Debug.Log($"[GeminiAudio] AI response: {responseText}");

        string lower = responseText.ToLowerInvariant();

        // Detect emotion from text
        DetectEmotionFromText(lower);

        // Trigger keyword animations
        TriggerKeywordAnimations(lower);

        // Spirit guardian summon / dismiss keywords (no-op if controller absent)
        if (SpiritGuardianFlyController.Instance != null)
            SpiritGuardianFlyController.Instance.HandleAIText(lower);

        // In the LiveChat (native-audio) flow, OnAudioFinished never fires here,
        // so schedule the emotion reset ourselves. Next SetAIResponse cancels this.
        if (_emotionResetCoroutine != null) StopCoroutine(_emotionResetCoroutine);
        _emotionResetCoroutine = StartCoroutine(ResetEmotionAfterDelay(emotionLingerDuration));
    }

    // ════════════════════════════════════════════
    //  Speaking-state signals (called from Flutter via Rob11SceneManager)
    //  Used to show/hide the mouth wave visualizer when audio plays through
    //  the native LiveChat pipeline (so Unity's AudioSource is silent).
    // ════════════════════════════════════════════
    public void OnSpeakingStart()
    {
        // Only the visualizer during speech: hide the emotion-mouth and keep
        // the speech-lines mouth force-hidden so we never get a triple-stack
        // (visualizer + speech lines + mouth).
        if (mouthEmoObject != null) mouthEmoObject.SetActive(false);
        ForceHideMouthSpeech();
        if (waveVisualizer != null) waveVisualizer.Show();
    }

    public void OnSpeakingEnd()
    {
        // Restore the emotion-mouth, keep the speech-lines mouth off.
        if (waveVisualizer != null) waveVisualizer.Hide();
        ForceHideMouthSpeech();
        if (mouthEmoObject != null) mouthEmoObject.SetActive(true);
        // Reset the per-turn firing lock so the next AI response can
        // re-trigger the same animations.
        _firedThisTurn.Clear();
    }

    /// <summary>
    /// Forwarded from Flutter/native (RMS of the current audio chunk). Lets
    /// the wave visualizer react to real loudness even though audio plays
    /// through Android AudioTrack, not Unity's AudioSource.
    /// </summary>
    public void SetAmplitude(float amplitude)
    {
        if (waveVisualizer != null) waveVisualizer.SetExternalAmplitude(amplitude);
    }

    /// <summary>
    /// Stop audio playback (silence button).
    /// </summary>
    public void StopAudio()
    {
        if (_audioSource.isPlaying)
        {
            _audioSource.Stop();
            OnAudioFinished();
        }
    }

    // ════════════════════════════════════════════
    //  Audio Decode & Playback
    // ════════════════════════════════════════════

    private IEnumerator DecodeAndPlayAudio(string base64Audio)
    {
        byte[] audioBytes;
        try
        {
            audioBytes = Convert.FromBase64String(base64Audio);
        }
        catch (Exception e)
        {
            Debug.LogError($"[GeminiAudio] Failed to decode Base64: {e.Message}");
            yield break;
        }

        Debug.Log($"[GeminiAudio] Decoded {audioBytes.Length} bytes of audio");

        // Convert MP3 bytes to AudioClip using WAV conversion
        // Unity can't natively load MP3 from bytes, so we use a raw PCM approach
        AudioClip clip = CreateAudioClipFromMp3Bytes(audioBytes);

        if (clip == null)
        {
            // Fallback: write to temp file and load
            yield return StartCoroutine(LoadAudioFromTempFile(audioBytes));
            yield break;
        }

        PlayClip(clip);
    }

    private AudioClip CreateAudioClipFromMp3Bytes(byte[] mp3Bytes)
    {
        // Unity doesn't have a built-in MP3 decoder for raw bytes.
        // Return null to trigger the temp file fallback.
        return null;
    }

    private IEnumerator LoadAudioFromTempFile(byte[] audioBytes)
    {
        string tempPath = System.IO.Path.Combine(Application.temporaryCachePath, "gemini_response.mp3");
        System.IO.File.WriteAllBytes(tempPath, audioBytes);

        string fileUrl = "file://" + tempPath;

        using (var www = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(fileUrl, AudioType.MPEG))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                AudioClip clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(www);
                if (clip != null && clip.loadState == AudioDataLoadState.Loaded)
                {
                    PlayClip(clip);
                }
                else
                {
                    Debug.LogError("[GeminiAudio] AudioClip failed to load");
                    SendToFlutter.Send("audio_error");
                }
            }
            else
            {
                Debug.LogError($"[GeminiAudio] Failed to load audio: {www.error}");
                SendToFlutter.Send("audio_error");
            }
        }
    }

    private void PlayClip(AudioClip clip)
    {
        _audioSource.clip = clip;
        _audioSource.Play();
        _isPlaying = true;

        // Show wave, hide mouth
        if (mouthEmoObject != null)
            mouthEmoObject.SetActive(false);
        if (waveVisualizer != null)
            waveVisualizer.Show();

        if (silenceButton != null)
            silenceButton.SetActive(true);
        if (listeningIndicator != null)
            listeningIndicator.SetActive(false);

        SendToFlutter.Send("audio_started");
        Debug.Log($"[GeminiAudio] Playing audio clip ({clip.length:F1}s)");

        // Cancel any pending emotion reset
        if (_emotionResetCoroutine != null)
        {
            StopCoroutine(_emotionResetCoroutine);
            _emotionResetCoroutine = null;
        }

        StartCoroutine(WaitForAudioEnd());
    }

    private IEnumerator WaitForAudioEnd()
    {
        while (_audioSource.isPlaying)
            yield return null;

        OnAudioFinished();
    }

    private void OnAudioFinished()
    {
        _isPlaying = false;

        // Hide wave, show mouth
        if (waveVisualizer != null)
            waveVisualizer.Hide();
        if (mouthEmoObject != null)
            mouthEmoObject.SetActive(true);

        if (silenceButton != null)
            silenceButton.SetActive(false);
        if (listeningIndicator != null)
            listeningIndicator.SetActive(true);

        // Linger emotion then reset
        _emotionResetCoroutine = StartCoroutine(ResetEmotionAfterDelay(emotionLingerDuration));

        SendToFlutter.Send("audio_done");
        Debug.Log("[GeminiAudio] Audio playback finished");
    }

    // ════════════════════════════════════════════
    //  Emotion Detection from Text
    // ════════════════════════════════════════════

    private void DetectEmotionFromText(string lowerText)
    {
        if (robotController == null) return;

        int bestEmotion = 0; // neutral
        int bestPriority = -1;

        foreach (var kvp in TextEmotionKeywords)
        {
            if (lowerText.Contains(kvp.Key))
            {
                // Longer keywords have higher priority
                if (kvp.Key.Length > bestPriority)
                {
                    bestPriority = kvp.Key.Length;
                    bestEmotion = kvp.Value;
                }
            }
        }

        if (bestEmotion != _lastEmotionIndex)
        {
            _lastEmotionIndex = bestEmotion;
            robotController.setEmotion(bestEmotion);
            Debug.Log($"[GeminiAudio] Emotion set to {bestEmotion}");
        }
    }

    // ════════════════════════════════════════════
    //  Keyword Animations
    // ════════════════════════════════════════════

    private void TriggerKeywordAnimations(string lowerText)
    {
        if (robotAnimator == null) return;

        // Wrap with spaces so padded keys like " hi " and " lol " can match
        // at the very start/end of the response.
        string padded = " " + lowerText + " ";

        for (int i = 0; i < ResponseKeywordAnims.Length; i++)
        {
            if (padded.Contains(ResponseKeywordAnims[i].Key))
            {
                AnimAction action = ResponseKeywordAnims[i].Value;
                // Only let each animation fire once per speaking turn.
                // Cleared when OnSpeakingEnd arrives.
                if (_firedThisTurn.Contains(action.animBool))
                {
                    return;
                }
                Debug.Log($"[GeminiAudio] Keyword \"{ResponseKeywordAnims[i].Key}\" → {action.animBool}");
                _firedThisTurn.Add(action.animBool);
                PlayAnimation(action);
                return;
            }
        }
    }

    private void PlayAnimation(AnimAction action)
    {
        if (robotAnimator == null) return;

        ClearTriggeredAnimation();

        if (action.emotionIndex >= 0 && robotController != null)
        {
            if (_emotionResetCoroutine != null)
            {
                StopCoroutine(_emotionResetCoroutine);
                _emotionResetCoroutine = null;
            }
            _lastEmotionIndex = action.emotionIndex;
            robotController.setEmotion(action.emotionIndex);
        }

        if (action.vary >= 0)
            robotAnimator.SetInteger("vary", action.vary);

        robotAnimator.SetBool(action.animBool, true);
        _activeAnimBool = action.animBool;

        _animResetCoroutine = StartCoroutine(ResetAnimationAfterClip(action.animBool));
    }

    private void ClearTriggeredAnimation()
    {
        if (_activeAnimBool != null && robotAnimator != null)
        {
            robotAnimator.SetBool(_activeAnimBool, false);
            _activeAnimBool = null;
        }

        if (_animResetCoroutine != null)
        {
            StopCoroutine(_animResetCoroutine);
            _animResetCoroutine = null;
        }
    }

    /// <summary>
    /// Waits for the Animator to actually enter the triggered state, then
    /// holds the bool true for the state's clip length (+ a tiny tail so the
    /// Exit Time transition can complete) before resetting. This prevents
    /// both "clip gets cut off mid-play" AND "clip loops while bool stays
    /// true" — the two failure modes we'd otherwise flip between.
    /// </summary>
    private IEnumerator ResetAnimationAfterClip(string animBool)
    {
        if (robotAnimator == null)
        {
            _activeAnimBool = null;
            _animResetCoroutine = null;
            yield break;
        }

        // Give the Animator a couple of frames to transition into the state.
        yield return null;
        yield return null;

        // How long is the state we just entered? Fall back if the state is
        // still idle (transition in flight on slow frames / wrong layer).
        float clipLength = animationHoldFallback;
        AnimatorStateInfo info = robotAnimator.GetCurrentAnimatorStateInfo(0);
        if (info.length > 0.05f && info.length < 10f)
        {
            clipLength = info.length;
        }

        yield return new WaitForSeconds(clipLength + animationHoldTail);

        if (robotAnimator != null)
        {
            robotAnimator.SetBool(animBool, false);
            robotAnimator.SetBool("reset", true);
        }

        _activeAnimBool = null;
        _animResetCoroutine = null;
    }

    private IEnumerator ResetEmotionAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (robotController != null)
            robotController.setEmotion(0);
        _lastEmotionIndex = 0;
        _emotionResetCoroutine = null;
    }
}
