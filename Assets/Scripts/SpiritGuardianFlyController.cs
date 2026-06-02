using System.Collections;
using UnityEngine;

// Playful flying behaviour for the Western_World_spiritGuardian scene.
// The guardian wanders between [hideSpots], pauses to hide, occasionally
// peeks out, then moves on. When summoned ("come out", "show yourself")
// it flies to [summonSpot] in front of the camera and triggers a talk
// animation. All motion is procedural (no flying clips needed) — gentle
// sin-wave bob + smooth-damp travel + small banking tilt toward velocity.
public class SpiritGuardianFlyController : MonoBehaviour
{
    public static SpiritGuardianFlyController Instance { get; private set; }

    public enum State { Idle, FlyingToHide, Hiding, FlyingToSummon, TalkingToPlayer }

    [Header("Waypoints")]
    [Tooltip("Empty transforms placed behind props the guardian can hide behind.")]
    public Transform[] hideSpots;
    [Tooltip("Transform the guardian flies to when summoned (place in front of camera).")]
    public Transform   summonSpot;
    [Tooltip("Optional — if set, guardian rotates to face this transform when talking. Defaults to Main Camera.")]
    public Transform   faceTarget;

    [Header("Motion")]
    public float wanderSpeed     = 2.2f;
    public float summonSpeed     = 4.5f;
    public float arrivalDistance = 0.25f;
    public float bobAmplitude    = 0.18f;
    public float bobFrequency    = 1.4f;
    public float tiltMaxDegrees  = 18f;
    public float rotateSpeed     = 6f;

    [Header("Hide Timing")]
    public float minHideDuration = 4f;
    public float maxHideDuration = 9f;
    [Range(0f, 1f)] public float peekChance = 0.55f;
    public float peekRiseAmount   = 0.7f;
    public float peekDuration     = 1.0f;

    [Header("Animation Hooks (Rob11)")]
    [Tooltip("Animator on the guardian — used to fire idle / peek / talk gestures.")]
    public Animator  animator;
    [Tooltip("Animator bool for the peek gesture (e.g. LookingFor).")]
    public string    peekTrigger   = "LookingFor";
    [Tooltip("Animator bool for the summoned greeting (e.g. Hello).")]
    public string    summonTrigger = "Hello";

    [Header("Conflict suppression")]
    [Tooltip("Disable Rob11Ctrl on Start so its grid/walk movement doesn't fight this script.")]
    public bool disableRob11Ctrl   = true;
    [Tooltip("Turn off Animator root motion so animations don't override transform writes.")]
    public bool disableRootMotion  = true;
    [Tooltip("Yaw offset applied when facing a target. If the model faces away, set 180; sideways try ±90.")]
    public float modelYawOffset    = 0f;

    [Header("Voice Keywords")]
    [Tooltip("Substrings (lowercased) that summon the guardian. Matched in GeminiAudioBridge.SetAIResponse text — these are phrases Gemini itself says when revealing its location.")]
    public string[]  summonKeywords  = {
        "come out", "coming out", "show yourself",
        "i am here", "i'm here", "right here", "here i am",
        "i am behind", "i'm behind", "behind the", "behind a",
        "you found me", "you got me", "spotted me",
        "i see you", "found you",
    };
    [Tooltip("Substrings that dismiss the guardian back to wandering/hiding.")]
    public string[]  dismissKeywords = {
        "go hide", "hide again", "back to hiding", "go back",
        "see you later", "goodbye", "bye bye", "talk to you later",
    };

    private State    _state;
    private Transform _currentTarget;
    private Transform _lastHideSpot;
    private Vector3   _smoothVelocity;
    private float     _bobPhaseOffset;
    private Coroutine _routine;

    void Awake()
    {
        Instance = this;
        if (faceTarget == null && Camera.main != null) faceTarget = Camera.main.transform;
        _bobPhaseOffset = Random.value * Mathf.PI * 2f;
    }

    void Start()
    {
        if (disableRob11Ctrl)
        {
            var ctrl = GetComponent<Rob11Ctrl>();
            if (ctrl) ctrl.enabled = false;
        }
        if (disableRootMotion && animator) animator.applyRootMotion = false;

        if (hideSpots == null || hideSpots.Length == 0)
            Debug.LogWarning("[Guardian] No hideSpots assigned — won't move until you fill the array.");

        EnterWander();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ───────────────────────────────────────────────────────

    [ContextMenu("TEST: Summon")]
    public void Summon()
    {
        if (summonSpot == null) { Debug.LogWarning("[Guardian] summonSpot not set."); return; }
        StopRoutine();
        _state = State.FlyingToSummon;
        _currentTarget = summonSpot;
    }

    [ContextMenu("TEST: Send Back To Hiding")]
    public void SendBackToHiding()
    {
        StopRoutine();
        EnterWander();
    }

    [ContextMenu("TEST: Peek Now")]
    private void TestPeek()
    {
        if (Application.isPlaying) StartCoroutine(PeekRoutine());
    }

    public void HandleAIText(string textLower)
    {
        if (string.IsNullOrEmpty(textLower)) return;

        foreach (var kw in summonKeywords)
            if (!string.IsNullOrEmpty(kw) && textLower.Contains(kw)) { Summon(); return; }

        foreach (var kw in dismissKeywords)
            if (!string.IsNullOrEmpty(kw) && textLower.Contains(kw)) { SendBackToHiding(); return; }
    }

    // ── State entry ──────────────────────────────────────────────────────

    private void EnterWander()
    {
        _state = State.FlyingToHide;
        _currentTarget = PickHideSpot();
    }

    private Transform PickHideSpot()
    {
        if (hideSpots == null || hideSpots.Length == 0) return null;
        if (hideSpots.Length == 1) return hideSpots[0];

        Transform pick;
        int safety = 8;
        do { pick = hideSpots[Random.Range(0, hideSpots.Length)]; } while (pick == _lastHideSpot && --safety > 0);
        _lastHideSpot = pick;
        return pick;
    }

    // ── Update loop ──────────────────────────────────────────────────────

    void Update()
    {
#if UNITY_EDITOR
        // Quick test keys (Game view must be focused):
        //   C = Summon   |   H = Send back to hiding   |   P = Peek
        if (Input.GetKeyDown(KeyCode.C)) Summon();
        if (Input.GetKeyDown(KeyCode.H)) SendBackToHiding();
        if (Input.GetKeyDown(KeyCode.P)) TestPeek();
#endif

        switch (_state)
        {
            case State.FlyingToHide:
                if (_currentTarget == null) { EnterWander(); return; }
                MoveToward(_currentTarget.position, wanderSpeed);
                FacePosition(_currentTarget.position);
                if (Reached(_currentTarget.position))
                {
                    _state = State.Hiding;
                    _routine = StartCoroutine(HideRoutine());
                }
                break;

            case State.Hiding:
                // hold position with gentle bob (added below)
                FacePosition(_currentTarget != null ? _currentTarget.position + _currentTarget.forward : transform.position + transform.forward);
                break;

            case State.FlyingToSummon:
                if (_currentTarget == null) { EnterWander(); return; }
                MoveToward(_currentTarget.position, summonSpeed);
                FacePosition(faceTarget ? faceTarget.position : _currentTarget.position);
                if (Reached(_currentTarget.position))
                {
                    _state = State.TalkingToPlayer;
                    if (animator && !string.IsNullOrEmpty(summonTrigger))
                        animator.SetBool(summonTrigger, true);
                    _routine = StartCoroutine(ClearAnimBoolAfter(summonTrigger, 1.6f));
                }
                break;

            case State.TalkingToPlayer:
                if (faceTarget) FacePosition(faceTarget.position);
                break;
        }
    }

    // Position writes happen in LateUpdate to beat animator root motion.
    void LateUpdate()
    {
        if (_pendingMove)
        {
            transform.position = _pendingPosition;
            _pendingMove = false;
        }
        ApplyBob();
    }

    private bool    _pendingMove;
    private Vector3 _pendingPosition;

    // ── Movement helpers ────────────────────────────────────────────────

    private void MoveToward(Vector3 target, float speed)
    {
        float smoothTime = Mathf.Max(0.15f, 1f / Mathf.Max(0.01f, speed));
        Vector3 next = Vector3.SmoothDamp(transform.position, target, ref _smoothVelocity, smoothTime, speed * 1.6f);
        _pendingPosition = next;
        _pendingMove = true;
    }

    private void FacePosition(Vector3 worldPos)
    {
        Vector3 dir = worldPos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion look = Quaternion.LookRotation(dir);

        // Apply model-forward correction (e.g. 180° if the rig faces -Z).
        look *= Quaternion.Euler(0f, modelYawOffset, 0f);

        // Bank into the turn based on horizontal velocity for a playful tilt.
        float lateralSpeed = Vector3.Dot(_smoothVelocity, transform.right);
        float bank = Mathf.Clamp(-lateralSpeed * 4f, -tiltMaxDegrees, tiltMaxDegrees);
        look *= Quaternion.Euler(0f, 0f, bank);

        transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * rotateSpeed);
    }

    private bool Reached(Vector3 target)
    {
        Vector3 d = target - transform.position; d.y *= 0.5f; // forgive vertical
        return d.sqrMagnitude < arrivalDistance * arrivalDistance;
    }

    private void ApplyBob()
    {
        float bob = Mathf.Sin(Time.time * bobFrequency + _bobPhaseOffset) * bobAmplitude * Time.deltaTime;
        transform.position = transform.position + Vector3.up * bob;
    }

    // ── Hide / peek routine ─────────────────────────────────────────────

    private IEnumerator HideRoutine()
    {
        float hideTime = Random.Range(minHideDuration, maxHideDuration);
        float elapsed  = 0f;

        while (elapsed < hideTime)
        {
            float chunk = Random.Range(1.2f, 2.5f);
            yield return new WaitForSeconds(chunk);
            elapsed += chunk;

            if (Random.value < peekChance) yield return StartCoroutine(PeekRoutine());
        }

        EnterWander();
    }

    private IEnumerator PeekRoutine()
    {
        if (animator && !string.IsNullOrEmpty(peekTrigger)) animator.SetBool(peekTrigger, true);

        Vector3 origin = transform.position;
        Vector3 peak   = origin + Vector3.up * peekRiseAmount;

        float t = 0f;
        while (t < peekDuration * 0.5f)
        {
            t += Time.deltaTime;
            transform.position = Vector3.Lerp(origin, peak, t / (peekDuration * 0.5f));
            yield return null;
        }
        t = 0f;
        while (t < peekDuration * 0.5f)
        {
            t += Time.deltaTime;
            transform.position = Vector3.Lerp(peak, origin, t / (peekDuration * 0.5f));
            yield return null;
        }

        if (animator && !string.IsNullOrEmpty(peekTrigger)) animator.SetBool(peekTrigger, false);
    }

    private IEnumerator ClearAnimBoolAfter(string boolName, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (animator && !string.IsNullOrEmpty(boolName)) animator.SetBool(boolName, false);
    }

    private void StopRoutine()
    {
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
    }
}
