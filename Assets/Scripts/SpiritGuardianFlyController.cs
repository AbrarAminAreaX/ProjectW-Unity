using UnityEngine;

// Minimal hover behaviour for the spirit guardian in the
// Western_World_spiritGuardian scene. Earlier wander / hide / peek logic was
// removed — the guardian now stays at its starting transform with a gentle
// procedural bob. The Summon / Dismiss / HandleAIText methods are kept as
// no-ops so existing Flutter triggers and GeminiAudioBridge hooks compile.
public class SpiritGuardianFlyController : MonoBehaviour
{
    public static SpiritGuardianFlyController Instance { get; private set; }

    [Header("Procedural hover")]
    public float bobAmplitude   = 0.08f;
    public float bobFrequency   = 1.0f;

    [Header("Conflict suppression")]
    [Tooltip("Disable Rob11Ctrl on Start so its grid/walk movement doesn't fight this hover.")]
    public bool disableRob11Ctrl  = true;
    [Tooltip("Turn off Animator root motion so animations don't override transform writes.")]
    public bool disableRootMotion = true;
    public Animator animator;

    private Vector3 _basePosition;
    private float   _phase;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        if (disableRob11Ctrl)
        {
            var ctrl = GetComponent<Rob11Ctrl>();
            if (ctrl) ctrl.enabled = false;
        }
        if (disableRootMotion && animator) animator.applyRootMotion = false;

        _basePosition = transform.position;
        _phase        = Random.value * Mathf.PI * 2f;
    }

    void LateUpdate()
    {
        float y = Mathf.Sin(Time.time * bobFrequency + _phase) * bobAmplitude;
        Vector3 p = _basePosition;
        p.y += y;
        transform.position = p;
    }

    // ── Kept as no-ops so existing call sites still compile / function ────

    public void Summon()           { /* no-op */ }
    public void SendBackToHiding() { /* no-op */ }
    public void HandleAIText(string textLower) { /* no-op */ }
}
