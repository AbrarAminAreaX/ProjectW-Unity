using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene controller for the ManLipSync scene (Ready Player Me male avatar).
/// Attach to the root GameObject named "SceneManager".
///
/// Flutter → Unity messages routed here:
///   sendToUnity("SceneManager", "LoadScene", "&lt;name&gt;")
///   sendToUnity("SceneManager", "SetAIResponse", text)      // drives emotion via keywords
///   sendToUnity("SceneManager", "OnSpeakingStart", "")
///   sendToUnity("SceneManager", "OnSpeakingEnd",   "")
///   sendToUnity("SceneManager", "OnAudioAmplitude", "0.x")  // drives jaw open
/// </summary>
public class ManLipSyncSceneManager : MonoBehaviour
{
    [Tooltip("EmotionController on the RPM character (applies face blend shape presets).")]
    public EmotionController emotionController;

    [Tooltip("AmplitudeLipSync on the RPM character (drives jawOpen from audio RMS). Used as fallback if uLipSync isn't wired.")]
    public AmplitudeLipSync lipSync;

    [Tooltip("UnityPcmStreamPlayer that buffers native audio chunks into a silent AudioSource for uLipSync to analyze.")]
    public UnityPcmStreamPlayer pcmStreamPlayer;

    [Tooltip("Animator on the RPM character with the Mixamo reaction states (Wave/Yes/No/Shrug/Thumb/Laugh/Cry/Think/Point/Clap/Dance triggers).")]
    public Animator characterAnimator;

    // Animations that have already fired during this AI turn — cleared on
    // OnSpeakingEnd. Prevents the same trigger from re-firing on every text
    // fragment the server streams during a single response.
    private readonly HashSet<string> _firedThisTurn = new HashSet<string>();

    // Keyword → Animator trigger name. First match wins; multi-word keys
    // appear before single-word so "i don't know" beats "know" etc.
    // Padded ones (" hi ", " no ") avoid substring false-matches.
    // Tuned for "Kai" wellness-coach backend: empathetic, reflective,
    // always asks a follow-up question.
    private static readonly KeyValuePair<string, string>[] AnimationKeywords = new[]
    {
        // ── Sympathy / Sad acknowledgment → Cry only on intense grief ──
        KV("heartbreak",     "Cry"), KV("in tears",  "Cry"),
        KV("so sad",         "Cry"), KV("crying",    "Cry"),
        KV("your pain",      "Cry"), KV("your sadness","Cry"),

        // ── Reflection / Thinking (Kai is constantly asking questions) ──
        KV("what is something", "Think"), KV("what are some",  "Think"),
        KV("what area",         "Think"), KV("perhaps",        "Think"),
        KV("let me think",      "Think"), KV("i wonder",       "Think"),
        KV("interesting",       "Think"), KV("hmm",            "Think"),
        KV("deeply value",      "Think"), KV("dreams you",     "Think"),
        KV("how do you",        "Think"), KV("how does",       "Think"),
        KV("where",             "Think"),

        // ── Acknowledgment / agreement → Yes (gentle nod) ──
        KV("i understand",       "Yes"), KV("i feel",          "Yes"),
        KV("i hear you",         "Yes"), KV("i am here",       "Yes"),
        KV("i'm here",           "Yes"), KV("yes,",            "Yes"),
        KV("absolutely",         "Yes"), KV("definitely",      "Yes"),
        KV("of course",          "Yes"), KV("certainly",       "Yes"),
        KV("agreed",             "Yes"),

        // ── Pointing / showing → Point (Kai often references "my world", "mirror to your") ──
        KV("you can see",   "Point"), KV("mirror to",     "Point"),
        KV("reflecting",    "Point"), KV("look at this",  "Point"),
        KV("check this",    "Point"), KV("over there",    "Point"),
        KV("right here",    "Point"), KV("my world",      "Point"),
        KV("my avatar",     "Point"), KV("my expressions","Point"),

        // ── Greetings → Wave ──
        KV("hello there", "Wave"), KV("hey there",   "Wave"),
        KV("hello, abrar","Wave"), KV("hi, abrar",   "Wave"),
        KV("hello",       "Wave"), KV(" hey ",       "Wave"),
        KV(" hey,",       "Wave"), KV(" hey!",       "Wave"),
        KV(" hi ",        "Wave"), KV(" hi,",        "Wave"), KV(" hi!", "Wave"),
        KV("welcome",     "Wave"),

        // ── Negation → No ──
        KV("absolutely not", "No"), KV("definitely not", "No"),
        KV("i don't think",  "No"),
        KV("nope",           "No"),
        KV(" no ",           "No"), KV(" no,", "No"), KV(" no.", "No"),

        // ── Uncertainty → Shrug ──
        KV("i don't know", "Shrug"), KV("not sure", "Shrug"),
        KV("no idea",      "Shrug"),

        // ── Praise → Thumb (Kai uses "valuable", "deeply value") ──
        KV("great job",   "Thumb"), KV("well done", "Thumb"),
        KV("good job",    "Thumb"), KV("thumbs up", "Thumb"),
        KV("valuable",    "Thumb"), KV("wonderful", "Thumb"),

        // ── Laughter → Laugh ──
        KV("haha",  "Laugh"), KV("ha ha", "Laugh"),
        KV(" lol ", "Laugh"), KV(" lol,", "Laugh"),
        KV(" lol.", "Laugh"), KV(" lol!", "Laugh"),
        KV("don't scientists", "Laugh"),  // "Why don't scientists trust atoms?" Kai dad-joke pattern
        KV("make up everything", "Laugh"),

        // ── Praise / celebration → Clap ──
        KV("congratulations", "Clap"), KV("we did it", "Clap"),
        KV("hooray",          "Clap"), KV("awesome",   "Clap"),

        // ── Dance ──
        KV("let's dance", "Dance"), KV("dance",     "Dance"),
        KV("celebrate",   "Dance"), KV("party time","Dance"),
    };

    // Keywords (lowercase) → emotion name (matches EmotionController.Presets keys).
    // First match wins; multi-word keys should come before short ones.
    // Tuned for the wellness-coach style backend ("Kai") observed in
    // production — empathetic, reflective, asks questions a lot.
    private static readonly KeyValuePair<string, string>[] EmotionKeywords = new[]
    {
        // ── Sad / pain (Kai often acknowledges feelings explicitly) ──
        KV("your sadness", "sad"), KV("your pain", "sad"),
        KV("hold space",   "sad"), KV("heartbreak", "sad"),
        KV("so sad",       "sad"), KV("sorry",       "sad"),
        KV("regret",       "sad"), KV("apologize",   "sad"),
        KV("difficult",    "sad"), KV("struggling",  "sad"),
        KV("sadness",      "sad"), KV("grief",       "sad"),
        KV("hurts",        "sad"),

        // ── Love / care (Kai's empathetic style — gentle warmth) ──
        KV("deeply attentive", "love"), KV("deeply caring", "love"),
        KV("here for you",     "love"), KV("caring",        "love"),
        KV("attentive",        "love"), KV("cherish",       "love"),
        KV("love it",          "love"), KV("adore",         "love"),
        KV("sweetheart",       "love"), KV("beautiful",     "love"),

        // ── Happy / joy / value ──
        KV("moments of joy", "happy"), KV("deeply value", "happy"),
        KV("valuable",       "happy"), KV("joy",          "happy"),
        KV("wonderful",      "happy"), KV("amazing",      "happy"),
        KV("fantastic",      "happy"), KV("excellent",    "happy"),
        KV("awesome",        "happy"), KV("great",        "happy"),
        KV("happy",          "happy"), KV("glad",         "happy"),
        KV("hooray",         "happy"), KV("yay",          "happy"),
        KV("haha",           "happy"), KV("ha ha",        "happy"),
        KV(" lol ",          "happy"),

        // ── Wonder / reflection (Kai's calm mindful tone) ──
        KV("steady awareness", "wonder"), KV("deep awareness", "wonder"),
        KV("calm presence",    "wonder"), KV("centered",       "wonder"),
        KV("constant presence","wonder"), KV("listening",      "wonder"),
        KV("incredible",       "surprised"), KV("surprising",  "surprised"),
        KV("unexpected",       "surprised"),
        KV("wow",              "surprised"), KV("whoa",        "surprised"),
        KV("really?",          "surprised"),
        KV("interesting",      "wonder"),
        KV("curious",          "wonder"), KV("i wonder",       "wonder"),
        KV("hmm",              "wonder"),

        // ── Fear ──
        KV("terrifying", "fear"), KV("scary",     "fear"),
        KV("afraid",     "fear"), KV("horror",    "fear"),
        KV("nervous",    "fear"), KV("worried",   "fear"),
        KV("fearful",    "fear"),

        // ── Anger ──
        KV("furious",    "angry"), KV("so angry",   "angry"),
        KV("annoyed",    "angry"), KV("frustrated", "angry"),
        KV("terrible",   "angry"), KV("awful",      "angry"),
        KV("hate",       "angry"), KV("angry",      "angry"),

        // ── Disgust ──
        KV("disgusting", "disgust"), KV("gross",  "disgust"),
        KV("yuck",       "disgust"), KV("eww",    "disgust"),
    };

    private static KeyValuePair<string, string> KV(string k, string v)
    {
        return new KeyValuePair<string, string>(k, v);
    }

    void Start()
    {
        SendToFlutter.Send("scene_loaded");
    }

    public void LoadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;
        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    /// <summary>Fires keyword-based emotion change + animation trigger on every AI text chunk.</summary>
    public void SetAIResponse(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        string lower = text.ToLowerInvariant();
        // Wrap so padded keys (" hi ", " no ") match at sentence boundaries.
        string padded = " " + lower + " ";

        // 1) Emotion (face blend shape preset)
        if (emotionController != null)
        {
            for (int i = 0; i < EmotionKeywords.Length; i++)
            {
                if (lower.Contains(EmotionKeywords[i].Key))
                {
                    emotionController.SetEmotion(EmotionKeywords[i].Value);
                    break;
                }
            }
        }

        // 2) Animation trigger (Mixamo body reaction, fires once per turn)
        if (characterAnimator != null)
        {
            for (int i = 0; i < AnimationKeywords.Length; i++)
            {
                if (padded.Contains(AnimationKeywords[i].Key))
                {
                    string trigger = AnimationKeywords[i].Value;
                    if (!_firedThisTurn.Contains(trigger))
                    {
                        _firedThisTurn.Add(trigger);
                        characterAnimator.SetTrigger(trigger);
                        Debug.Log($"[ManLipSync] Anim trigger \"{AnimationKeywords[i].Key}\" → {trigger}");
                    }
                    break;
                }
            }
        }
    }

    public void OnSpeakingStart(string _ignored)
    {
        // No-op for now. Lip sync starts automatically when amplitudes flow.
    }

    public void OnSpeakingEnd(string _ignored)
    {
        // Close mouth immediately and fade back to neutral emotion.
        if (lipSync != null) lipSync.Close();
        if (emotionController != null) emotionController.SetEmotion("neutral");
        // Cut any in-progress reaction (e.g. long Dance loop) and snap back
        // to Idle when the AI finishes talking.
        if (characterAnimator != null)
        {
            characterAnimator.CrossFade("Idle", 0.2f);
        }
        // Reset per-turn animation lock so the next response can re-trigger
        // the same body reactions.
        _firedThisTurn.Clear();
    }

    public void OnAudioAmplitude(string amplitudeStr)
    {
        if (lipSync == null) return;
        float amp;
        if (float.TryParse(amplitudeStr,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out amp))
        {
            lipSync.SetAmplitude(amp);
        }
    }

    /// <summary>
    /// Each base64 PCM16 audio chunk forwarded from native (24000 Hz mono).
    /// Pumped into UnityPcmStreamPlayer's silent AudioSource so uLipSync
    /// (or any AudioSource-based analyzer) gets real audio to FFT.
    /// </summary>
    public void OnAudioPcm(string base64Pcm)
    {
        if (pcmStreamPlayer != null) pcmStreamPlayer.PushChunk(base64Pcm);
    }

    // ── No-op stubs matching existing scene manager contracts ─────────────
    public void PlayGeminiAudio(string _b64) { }
    public void StopAudio() { }
    public void EnableAR(string _) { }
    public void DisableAR(string _) { }
    public void ToggleBackground(string _) { }
    public void SetRobotAnimation(string _) { }
    public void ResetARPlacement(string _) { }
    public void GetAnimationList(string _) { SendToFlutter.Send("animation_list:"); }
}
