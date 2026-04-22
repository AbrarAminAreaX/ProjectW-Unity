using UnityEngine;

/// <summary>
/// Floating on-screen virtual joystick.
///
/// Drives a normalized 2D <see cref="Value"/> in [-1..1] for both axes.
/// Reads raw touches via legacy <see cref="Input"/> (and falls back to the
/// mouse in the Editor / on desktop), so it does not require an EventSystem.
///
/// Build the visuals as two stacked UI Images (background + handle) and
/// assign their RectTransforms in the inspector — or let
/// <see cref="OpenWorldSetup"/> create them at runtime.
/// </summary>
public class OnScreenJoystick : MonoBehaviour
{
    [Header("UI")]
    public RectTransform background;
    public RectTransform handle;

    [Tooltip("Maximum drag radius in local UI units.")]
    public float radius = 100f;

    [Tooltip("Inputs below this magnitude are zeroed (small dead-zone).")]
    [Range(0f, 0.5f)]
    public float deadZone = 0.1f;

    /// <summary>Normalized stick value, x = right, y = up, magnitude in [0..1].</summary>
    public Vector2 Value { get; private set; }

    private int _activeFingerId = -1;
    private bool _mouseHeld;
    private Camera _uiCamera;

    void Start()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera)
            _uiCamera = canvas.worldCamera;
    }

    void Update()
    {
        if (background == null || handle == null) return;

        if (Input.touchSupported && Input.touchCount > 0)
        {
            UpdateTouch();
        }
        else
        {
            UpdateMouse();
        }
    }

    private void UpdateTouch()
    {
        // If we already track a finger, follow it until it lifts.
        if (_activeFingerId != -1)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                if (t.fingerId != _activeFingerId) continue;

                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    ResetStick();
                    return;
                }
                MoveHandle(t.position);
                return;
            }
            // Finger disappeared.
            ResetStick();
            return;
        }

        // Otherwise look for a new finger that began inside the joystick area.
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);
            if (t.phase != TouchPhase.Began) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(background, t.position, _uiCamera))
            {
                _activeFingerId = t.fingerId;
                MoveHandle(t.position);
                return;
            }
        }
    }

    private void UpdateMouse()
    {
        if (Input.GetMouseButtonDown(0) &&
            RectTransformUtility.RectangleContainsScreenPoint(background, Input.mousePosition, _uiCamera))
        {
            _mouseHeld = true;
        }

        if (_mouseHeld)
        {
            if (Input.GetMouseButton(0))
            {
                MoveHandle(Input.mousePosition);
            }
            else
            {
                _mouseHeld = false;
                ResetStick();
            }
        }
    }

    private void MoveHandle(Vector2 screenPos)
    {
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(background, screenPos, _uiCamera, out localPoint);

        Vector2 clamped = Vector2.ClampMagnitude(localPoint, radius);
        handle.anchoredPosition = clamped;

        Vector2 normalized = clamped / radius;
        if (normalized.magnitude < deadZone) normalized = Vector2.zero;
        Value = normalized;
    }

    private void ResetStick()
    {
        _activeFingerId = -1;
        if (handle != null) handle.anchoredPosition = Vector2.zero;
        Value = Vector2.zero;
    }
}
