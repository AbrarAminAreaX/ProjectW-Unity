using UnityEngine;

public class GeminiAudioBridgeTester : MonoBehaviour
{
    public GeminiAudioBridge bridge;

    [TextArea(2, 4)] public string sampleResponseHappy = "Haha that's hilarious! Let me wave to you — hello there!";
    [TextArea(2, 4)] public string sampleResponseSad   = "I'm sorry to hear that. It must be difficult.";
    [TextArea(2, 4)] public string sampleResponseDance = "Let's dance to celebrate!";

    [Tooltip("Paste a base64-encoded MP3 string here to test PlayAudio. " +
             "Easiest source: call your Gemini endpoint from Flutter, log the response.audio, paste it here.")]
    [TextArea(3, 6)] public string sampleBase64Mp3 = "";

    void Reset()
    {
        if (bridge == null) bridge = GetComponent<GeminiAudioBridge>();
    }

    [ContextMenu("Test: SetAIResponse (Happy)")]
    void TestHappy()  => RequireBridge()?.SetAIResponse(sampleResponseHappy);

    [ContextMenu("Test: SetAIResponse (Sad)")]
    void TestSad()    => RequireBridge()?.SetAIResponse(sampleResponseSad);

    [ContextMenu("Test: SetAIResponse (Dance)")]
    void TestDance()  => RequireBridge()?.SetAIResponse(sampleResponseDance);

    [ContextMenu("Test: PlayAudio (base64 field)")]
    void TestPlay()
    {
        if (string.IsNullOrEmpty(sampleBase64Mp3))
        {
            Debug.LogWarning("sampleBase64Mp3 is empty. Paste a base64 MP3 to test playback.");
            return;
        }
        RequireBridge()?.PlayAudio(sampleBase64Mp3);
    }

    [ContextMenu("Test: StopAudio")]
    void TestStop()   => RequireBridge()?.StopAudio();

    GeminiAudioBridge RequireBridge()
    {
        if (bridge == null) bridge = GetComponent<GeminiAudioBridge>();
        if (bridge == null) Debug.LogError("GeminiAudioBridgeTester: no GeminiAudioBridge assigned.");
        return bridge;
    }
}
