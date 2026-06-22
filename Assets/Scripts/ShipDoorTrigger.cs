using UnityEngine;

// Attach to each DoorOpener collider — the inside and outside trigger zones of a
// doorway — and point it at the ShipDoor it controls. Forwards trigger
// enter/exit events so the door stays open while something is inside any zone
// and closes once that object has cleared every zone.
//
// Note: for OnTrigger events to fire, the moving object (the player) must have a
// Rigidbody (a kinematic one is fine). The collider on this object is forced to
// isTrigger automatically.
[RequireComponent(typeof(Collider))]
public class ShipDoorTrigger : MonoBehaviour
{
    [Tooltip("The door this collider opens. Auto-filled from a parent ShipDoor if left empty.")]
    public ShipDoor door;

    void Reset()
    {
        // Editor convenience: make the collider a trigger and auto-wire the door.
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
        if (door == null) door = GetComponentInParent<ShipDoor>();
    }

    void Awake()
    {
        if (door == null) door = GetComponentInParent<ShipDoor>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (door != null) door.NotifyEnter(other);
    }

    void OnTriggerExit(Collider other)
    {
        if (door != null) door.NotifyExit(other);
    }
}
