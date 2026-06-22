using UnityEngine;

/// Marks a 3D object in the Ship World as tappable. When tapped, the
/// ShipInteractionManager plays the portal warp and tells Flutter to open the
/// matching feature (the Flutter side maps featureKey -> route in
/// chat_home_screen.dart's _unityNavTargets).
///
/// Setup per object: add this component + a Collider (e.g. BoxCollider) to the
/// object you want tappable (e.g. the social-wall screen/panel), then set
/// featureKey to "socialWall". Adding more hotspots later = another object with
/// this component + a matching entry in _unityNavTargets on the Flutter side.
[RequireComponent(typeof(Collider))]
public class ShipInteractable : MonoBehaviour
{
    [Tooltip("Feature key sent to Flutter as 'navigate:<featureKey>'. " +
             "Must match a key in chat_home_screen.dart _unityNavTargets " +
             "(e.g. \"socialWall\").")]
    public string featureKey = "socialWall";

    [Header("Triggers")]
    [Tooltip("Tap/click the object to trigger the warp.")]
    public bool triggerOnTap = true;

    [Tooltip("Trigger the warp when the guardian flies into / touches the " +
             "object (proximity to its collider surface).")]
    public bool triggerOnCollision = true;

    [Tooltip("How close the guardian must get to the object's surface to count " +
             "as a collision (metres).")]
    public float collisionRadius = 1.0f;

    [Tooltip("Optional: briefly emphasise the object when tapped (scale punch).")]
    public bool tapFeedback = true;

    [HideInInspector] public Collider cachedCollider;
}
