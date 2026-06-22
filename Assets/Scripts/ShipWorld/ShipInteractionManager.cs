using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// Detects interactions with ShipInteractable objects in the Ship World, plays
/// the portal warp, then tells Flutter to open the matching feature via
/// SendToFlutter.Send("navigate:<featureKey>") at the warp peak.
///
/// Two trigger modes (per ShipInteractable):
///   • Tap   — press+release without much movement, not on the joystick UI.
///   • Collision — the guardian flies within collisionRadius of the object's
///     collider surface. Fires once on *entry* (won't re-fire while you stay
///     inside, so popping back from the Flutter feature standing in the object
///     doesn't immediately re-trigger).
///
/// Scene setup: add this component to one GameObject in Rob11Scene_ShipWorld
/// (e.g. an empty "ShipInteractions"). It auto-creates the PortalWarpController
/// and auto-finds the guardian (the SpiritGuardianJoystickController).
public class ShipInteractionManager : MonoBehaviour
{
    [Tooltip("Camera taps are cast from. Defaults to Camera.main.")]
    public Camera cam;

    [Tooltip("The guardian/player transform used for collision proximity. " +
             "Left empty = the SpiritGuardianJoystickController in the scene.")]
    public Transform player;

    [Tooltip("Max pointer travel (pixels) between press and release to still " +
             "count as a tap (vs a joystick drag).")]
    public float tapMoveThreshold = 20f;

    [Tooltip("Layers tappable objects live on. Default = Everything.")]
    public LayerMask raycastMask = ~0;

    private PortalWarpController _warp;
    private readonly List<ShipInteractable> _interactables = new();
    private bool[] _wasInside;

    private bool _pressed;
    private Vector2 _downPos;
    private bool _downOverUI;

    private void Awake()
    {
        if (cam == null) cam = Camera.main;

        _warp = PortalWarpController.Create();
        _warp.transform.SetParent(transform, false);

        if (player == null)
        {
            var guardian = FindFirstObjectByType<SpiritGuardianJoystickController>();
            if (guardian != null) player = guardian.transform;
        }

        RefreshInteractables();
    }

    /// Caches every ShipInteractable in the scene (+ its collider). Call again
    /// if you spawn interactables at runtime.
    public void RefreshInteractables()
    {
        _interactables.Clear();
        foreach (var it in FindObjectsByType<ShipInteractable>(FindObjectsSortMode.None))
        {
            if (it.cachedCollider == null) it.cachedCollider = it.GetComponent<Collider>();
            _interactables.Add(it);
        }
        _wasInside = new bool[_interactables.Count];
    }

    private void Update()
    {
        if (cam == null) cam = Camera.main;
        if (_warp == null || _warp.IsPlaying) return;

        HandleCollisions();
        HandleTap();
    }

    // --- Collision (proximity to the collider surface) ---
    private void HandleCollisions()
    {
        if (player == null) return;

        for (int i = 0; i < _interactables.Count; i++)
        {
            var it = _interactables[i];
            if (it == null || !it.triggerOnCollision || it.cachedCollider == null)
            {
                _wasInside[i] = false;
                continue;
            }

            Vector3 closest = it.cachedCollider.ClosestPoint(player.position);
            bool inside = (closest - player.position).sqrMagnitude
                          <= it.collisionRadius * it.collisionRadius;

            // Fire only on the outside -> inside edge.
            if (inside && !_wasInside[i])
            {
                _wasInside[i] = true;
                TriggerInteraction(it, feedback: false);
                return; // one warp at a time
            }
            _wasInside[i] = inside;
        }
    }

    // --- Tap / click ---
    private void HandleTap()
    {
        if (cam == null) return;

        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
                BeginPress(touch.position, touch.fingerId);
            else if (touch.phase == TouchPhase.Ended)
                EndPress(touch.position);
            else if (touch.phase == TouchPhase.Canceled)
                _pressed = false;
            return;
        }

        if (Input.GetMouseButtonDown(0))
            BeginPress(Input.mousePosition, -1);
        else if (Input.GetMouseButtonUp(0))
            EndPress(Input.mousePosition);
    }

    private void BeginPress(Vector2 pos, int fingerId)
    {
        _pressed = true;
        _downPos = pos;
        _downOverUI = IsOverUI(fingerId);
    }

    private void EndPress(Vector2 pos)
    {
        if (!_pressed) return;
        _pressed = false;

        if (_downOverUI) return;
        if (Vector2.Distance(pos, _downPos) > tapMoveThreshold) return;

        Ray ray = cam.ScreenPointToRay(pos);
        if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, raycastMask)) return;

        var interactable = hit.collider.GetComponentInParent<ShipInteractable>();
        if (interactable == null || !interactable.triggerOnTap) return;

        TriggerInteraction(interactable, feedback: true);
    }

    private void TriggerInteraction(ShipInteractable interactable, bool feedback)
    {
        if (feedback && interactable.tapFeedback)
            StartCoroutine(TapPunch(interactable.transform));

        string key = interactable.featureKey;
        _warp.Play(() => SendToFlutter.Send("navigate:" + key));
    }

    private static bool IsOverUI(int fingerId)
    {
        if (EventSystem.current == null) return false;
        return fingerId >= 0
            ? EventSystem.current.IsPointerOverGameObject(fingerId)
            : EventSystem.current.IsPointerOverGameObject();
    }

    // Small scale punch so the tapped object reacts before the warp.
    private static IEnumerator TapPunch(Transform t)
    {
        Vector3 baseScale = t.localScale;
        float dur = 0.18f;
        for (float e = 0f; e < dur; e += Time.unscaledDeltaTime)
        {
            float k = 1f + 0.12f * Mathf.Sin((e / dur) * Mathf.PI);
            t.localScale = baseScale * k;
            yield return null;
        }
        t.localScale = baseScale;
    }
}
