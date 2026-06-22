using UnityEngine;

/// <summary>
/// Lightweight follow camera for the flying spirit guardian.
///
/// Stays directly BEHIND the guardian's back while it flies — the camera swings
/// around so it always trails the guardian's heading — then FREEZES its orbit
/// angle the moment the guardian stops. That way, when the joystick is released
/// and the guardian turns to look back at the camera (its "attentive companion"
/// idle), the camera holds still instead of chasing the new facing forever.
///
/// While idle the guardian's heading is ignored, so the camera-relative joystick
/// controls don't drift. The orbit distance and height are captured from
/// whatever framing the camera already has in the scene at Start, so the shot
/// you set up in the Editor is preserved.
///
/// Usually you don't add this by hand — <see cref="SpiritGuardianJoystickController"/>
/// attaches and wires it automatically.
/// </summary>
[DisallowMultipleComponent]
public class GuardianFollowCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Offset")]
    [Tooltip("Use the camera's existing position relative to the target (captured at Start). Turn off to use the explicit offset below.")]
    public bool useInitialOffset = true;

    [Tooltip("World-space camera offset from the target. Ignored if Use Initial Offset is on.")]
    public Vector3 offset = new Vector3(0f, 1.5f, -4f);

    [Header("Stay behind")]
    [Tooltip("Swing the camera around to sit behind the guardian's back while it's moving.")]
    public bool stayBehind = true;

    [Tooltip("How fast (deg/sec) the camera swings around to get behind the guardian's heading.")]
    public float behindTurnSpeed = 220f;

    [Tooltip("Planar speed (units/sec) above which the guardian counts as 'moving' and the camera repositions behind it. Below this the orbit angle freezes.")]
    public float moveThreshold = 0.15f;

    [Header("Smoothing")]
    [Range(0.01f, 1f)] public float positionSmoothTime = 0.18f;
    [Range(0.01f, 1f)] public float lookSmoothTime = 0.12f;

    [Tooltip("Offset added to the target when computing the look-at point (e.g. aim at the chest/face).")]
    public Vector3 lookAtOffset = new Vector3(0f, 0.2f, 0f);

    [Header("Obstacle avoidance")]
    public bool avoidObstacles = true;
    [Tooltip("Layers the camera collides with. Exclude the guardian's own layer.")]
    public LayerMask collisionMask = ~0;
    public float collisionPadding = 0.2f;
    public float collisionProbeRadius = 0.2f;

    private Vector3 _positionVelocity;
    private Vector3 _currentLookAt;
    private Vector3 _lookAtVelocity;

    // Stay-behind orbit state
    private float _height;        // vertical part of the captured offset
    private float _distance;      // horizontal trailing distance
    private Vector3 _followDir;   // horizontal unit vector from target -> camera
    private Vector3 _lastTargetPos;

    void Start()
    {
        if (target == null) return;

        if (useInitialOffset)
            offset = transform.position - target.position;

        _height = offset.y;
        Vector3 flat = new Vector3(offset.x, 0f, offset.z);
        _distance = flat.magnitude;
        _followDir = flat.sqrMagnitude > 0.0001f
            ? flat.normalized
            : BehindDir();
        _lastTargetPos = target.position;

        _currentLookAt = target.position + lookAtOffset;
        transform.position = target.position + CurrentOffset();
        transform.LookAt(_currentLookAt);
    }

    // Horizontal direction from the guardian to where the camera should sit
    // when directly behind its back (opposite the guardian's forward).
    private Vector3 BehindDir()
    {
        Vector3 fwd = target.forward; fwd.y = 0f;
        return fwd.sqrMagnitude > 0.0001f ? -fwd.normalized : -Vector3.forward;
    }

    private Vector3 CurrentOffset()
    {
        return _followDir * _distance + Vector3.up * _height;
    }

    void LateUpdate()
    {
        if (target == null) return;

        if (stayBehind)
        {
            // Detect horizontal movement (ignore the procedural bob on Y).
            float dt = Mathf.Max(Time.deltaTime, 1e-5f);
            Vector3 deltaFlat = target.position - _lastTargetPos;
            deltaFlat.y = 0f;
            float planarSpeed = deltaFlat.magnitude / dt;
            _lastTargetPos = target.position;

            // While moving, swing the orbit angle behind the guardian's heading.
            // While idle, freeze it so the guardian can turn to face the camera.
            if (planarSpeed > moveThreshold)
            {
                _followDir = Vector3.RotateTowards(
                    _followDir, BehindDir(),
                    behindTurnSpeed * Mathf.Deg2Rad * dt, 0f).normalized;
            }
        }

        Vector3 desiredPosition = target.position + (stayBehind ? CurrentOffset() : offset);
        Vector3 lookAtPoint = target.position + lookAtOffset;

        if (avoidObstacles)
        {
            Vector3 toCamera = desiredPosition - lookAtPoint;
            float dist = toCamera.magnitude;
            if (dist > 0.001f)
            {
                Vector3 dir = toCamera / dist;
                RaycastHit hit;
                bool blocked = collisionProbeRadius > 0f
                    ? Physics.SphereCast(lookAtPoint, collisionProbeRadius, dir, out hit, dist, collisionMask, QueryTriggerInteraction.Ignore)
                    : Physics.Raycast(lookAtPoint, dir, out hit, dist, collisionMask, QueryTriggerInteraction.Ignore);
                if (blocked)
                    desiredPosition = lookAtPoint + dir * Mathf.Max(0f, hit.distance - collisionPadding);
            }
        }

        transform.position = Vector3.SmoothDamp(
            transform.position, desiredPosition, ref _positionVelocity, positionSmoothTime);

        _currentLookAt = Vector3.SmoothDamp(
            _currentLookAt, lookAtPoint, ref _lookAtVelocity, lookSmoothTime);
        transform.LookAt(_currentLookAt);
    }
}
