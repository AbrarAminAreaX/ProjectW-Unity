using System.Collections.Generic;
using UnityEngine;

// Automatic sliding door for the Rob11Scene_ShipWorld scene.
//
// Drives one doorway that can be a single sliding panel or a left/right pair.
// Panels slide horizontally (apart) or vertically (up) when a tagged object
// enters any of the door's trigger zones, and close again once that object has
// cleared every zone. So walking in one side (the outside collider) opens it
// and walking out the far side (the inside collider) lets it auto-close.
//
// Pair this with a ShipDoorTrigger on each DoorOpener collider (the inside and
// outside zones) and point that trigger at this component.
public class ShipDoor : MonoBehaviour
{
    public enum DoorMovement { SlideHorizontal, SlideVertical }

    [Header("Door panels")]
    [Tooltip("Required. The (left) sliding panel. For a single-piece door, only assign this one.")]
    public Transform leftPanel;
    [Tooltip("Optional. The right panel of a double door. Leave empty for a single-piece door.")]
    public Transform rightPanel;

    [Header("Movement")]
    public DoorMovement movement = DoorMovement.SlideHorizontal;
    [Tooltip("How far (in local units) each panel travels when fully open.")]
    public float openDistance = 1.5f;
    [Tooltip("Slide speed in local units per second.")]
    public float openSpeed = 3f;
    [Tooltip("Flip the direction the panel(s) travel when opening.")]
    public bool invertDirection = false;

    [Header("Trigger")]
    [Tooltip("Only colliders with this tag open the door. Leave empty to allow anything.")]
    public string requiredTag = "Player";
    [Tooltip("Seconds to keep the door open after the last occupant clears every zone.")]
    public float autoCloseDelay = 0.25f;

    [Header("Debug (read-only)")]
    [SerializeField] private bool _isOpen;

    private Vector3 _leftClosed, _leftOpen;
    private Vector3 _rightClosed, _rightOpen;
    private readonly HashSet<Collider> _occupants = new HashSet<Collider>();
    private float _closeTimer;

    void Start()
    {
        // Capture the closed pose, then derive the open pose from the chosen mode.
        if (leftPanel != null)
        {
            _leftClosed = leftPanel.localPosition;
            _leftOpen   = _leftClosed + PanelOffset(isRight: false);
        }
        if (rightPanel != null)
        {
            _rightClosed = rightPanel.localPosition;
            _rightOpen   = _rightClosed + PanelOffset(isRight: true);
        }
    }

    Vector3 PanelOffset(bool isRight)
    {
        float dir = invertDirection ? -1f : 1f;

        if (movement == DoorMovement.SlideVertical)
            return Vector3.up * (openDistance * dir);

        // Horizontal: left panel slides -Z, right panel slides +Z. A single
        // piece (only leftPanel assigned) uses the -Z direction; invert flips it.
        float side = isRight ? 1f : -1f;
        return Vector3.forward * (openDistance * side * dir);
    }

    void Update()
    {
        // Drop any occupants that were destroyed while still inside a zone.
        if (_occupants.Count > 0)
            _occupants.RemoveWhere(c => c == null);

        if (_occupants.Count > 0)
        {
            _isOpen     = true;
            _closeTimer = autoCloseDelay;
        }
        else if (_isOpen)
        {
            _closeTimer -= Time.deltaTime;
            if (_closeTimer <= 0f) _isOpen = false;
        }

        float step = openSpeed * Time.deltaTime;
        if (leftPanel != null)
            leftPanel.localPosition = Vector3.MoveTowards(
                leftPanel.localPosition, _isOpen ? _leftOpen : _leftClosed, step);
        if (rightPanel != null)
            rightPanel.localPosition = Vector3.MoveTowards(
                rightPanel.localPosition, _isOpen ? _rightOpen : _rightClosed, step);
    }

    // Called by ShipDoorTrigger on each collider zone (inside / outside).
    public void NotifyEnter(Collider other)
    {
        if (!IsEligible(other)) return;
        _occupants.Add(other);
    }

    public void NotifyExit(Collider other)
    {
        _occupants.Remove(other);
    }

    bool IsEligible(Collider other)
    {
        if (string.IsNullOrEmpty(requiredTag)) return true;
        return other.CompareTag(requiredTag);
    }

    // Handy for sanity-checking the slide direction from the inspector.
    [ContextMenu("Toggle Open/Closed (Play mode)")]
    void ToggleForTest() { _isOpen = !_isOpen; _closeTimer = autoCloseDelay; }
}
