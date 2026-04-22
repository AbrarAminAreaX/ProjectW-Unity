using UnityEngine;

/// <summary>
/// Smooth third-person follow camera with slow yaw catch-up and obstacle
/// collision avoidance.
///
/// Behaviour:
///   - Follows <see cref="target"/>'s position with smoothing.
///   - The camera's yaw slowly lerps toward the target's yaw (slow enough
///     that joystick input axes stay stable mid-input — preventing the
///     "hold one direction → infinite spin" runaway loop you get when the
///     camera orbits in real time — but fast enough that after the player
///     stops moving the camera ends up behind them).
///   - Casts a ray from the look-at point to the desired camera position
///     and pulls the camera inward when an obstacle would block the view.
///     This keeps the player visible in congested environments.
///
/// Tuning:
///   - <see cref="yawSmoothTime"/> = how long it takes the camera to align
///     with the player's facing. Higher = more cinematic, less responsive.
///     Lower = snappier but reintroduces the spin loop if too low.
///   - <see cref="collisionMask"/> should include the layers of static
///     environment geometry (walls, props) and exclude the player itself.
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Transform the camera should follow (typically the player root).")]
    public Transform target;

    [Header("Follow")]
    [Tooltip("Camera offset in the target's yaw-only local space (x = right, y = up, z = -back / +forward). Pitch and roll are ignored so the camera stays level.")]
    public Vector3 offset = new Vector3(0f, 2.5f, -5f);

    [Tooltip("Higher = snappier follow. Lower = smoother / laggier.")]
    [Range(0.01f, 1f)]
    public float positionSmoothTime = 0.15f;

    [Tooltip("Higher = snappier look. Lower = smoother / laggier.")]
    [Range(0.01f, 1f)]
    public float lookSmoothTime = 0.1f;

    [Header("Yaw catch-up")]
    [Tooltip("Time (seconds) for the camera's yaw to catch up to the player's yaw WHILE the user is holding the joystick. Larger = more lag = no input-spin loop. Sweet spot ~1.0–2.0.")]
    [Range(0.1f, 5f)]
    public float yawSmoothTime = 1.5f;

    [Tooltip("Time (seconds) for the camera's yaw to catch up to the player's yaw WHEN the user has released the joystick. Should be much faster than yawSmoothTime so the camera quickly snaps behind the player and the next joystick press starts from a clean baseline.")]
    [Range(0.02f, 1f)]
    public float yawRealignSmoothTime = 0.15f;

    /// <summary>
    /// Set by <see cref="PlayerController"/> every frame to true while the
    /// joystick is being held, false when released. Drives whether the
    /// camera uses the slow lag (input active, prevents spin loop) or the
    /// fast realignment (input released, snaps behind the player).
    /// </summary>
    [HideInInspector]
    public bool inputActive;

    [Header("Look At")]
    [Tooltip("Offset added to the target's position when computing the look-at point.")]
    public Vector3 lookAtOffset = new Vector3(0f, 1.2f, 0f);

    [Header("Obstacle avoidance")]
    [Tooltip("Layers the camera will collide with (walls, scenery, etc). Should NOT include the player layer.")]
    public LayerMask collisionMask = ~0;

    [Tooltip("Distance to keep between the camera and the surface it would hit, so the camera doesn't end up clipping into geometry.")]
    public float collisionPadding = 0.2f;

    [Tooltip("Radius of the spherecast used for collision detection. 0 = use a thin ray.")]
    public float collisionProbeRadius = 0.2f;

    private Vector3 _positionVelocity = Vector3.zero;
    private Vector3 _currentLookAt;
    private Vector3 _lookAtVelocity = Vector3.zero;
    private float _currentYaw;
    private float _yawVelocity;

    void Start()
    {
        if (target == null) return;

        // Start the camera already aligned behind the player so we don't lerp
        // in from wherever it was sitting in the scene.
        _currentYaw = target.eulerAngles.y;
        Vector3 desired = ComputeDesiredPosition();
        transform.position = desired;
        _currentLookAt = target.position + lookAtOffset;
        transform.LookAt(_currentLookAt);
    }

    void LateUpdate()
    {
        if (target == null) return;

        // Catch up to the target's yaw. While the joystick is held we use
        // the slow lag (yawSmoothTime) so the input axes stay stable and
        // there's no runaway "input rotates → player rotates → input
        // rotates" feedback loop. As soon as the user releases the stick
        // we switch to a much faster realignment so the camera snaps
        // behind the player, leaving the next joystick press to start
        // from a clean, aligned baseline.
        float targetYaw = target.eulerAngles.y;
        float effectiveYawSmoothTime = inputActive ? yawSmoothTime : yawRealignSmoothTime;
        _currentYaw = Mathf.SmoothDampAngle(
            _currentYaw,
            targetYaw,
            ref _yawVelocity,
            effectiveYawSmoothTime
        );

        Vector3 desiredPosition = ComputeDesiredPosition();

        // Obstacle avoidance: cast from the look-at point toward the desired
        // camera position. If anything blocks the line of sight, pull the
        // camera inward to the hit point so the player stays visible.
        Vector3 lookAtPoint = target.position + lookAtOffset;
        Vector3 toCamera = desiredPosition - lookAtPoint;
        float castDistance = toCamera.magnitude;
        if (castDistance > 0.001f)
        {
            Vector3 castDir = toCamera / castDistance;
            bool blocked;
            RaycastHit hit;
            if (collisionProbeRadius > 0f)
            {
                blocked = Physics.SphereCast(
                    lookAtPoint,
                    collisionProbeRadius,
                    castDir,
                    out hit,
                    castDistance,
                    collisionMask,
                    QueryTriggerInteraction.Ignore
                );
            }
            else
            {
                blocked = Physics.Raycast(
                    lookAtPoint,
                    castDir,
                    out hit,
                    castDistance,
                    collisionMask,
                    QueryTriggerInteraction.Ignore
                );
            }

            if (blocked)
            {
                float allowedDistance = Mathf.Max(0f, hit.distance - collisionPadding);
                desiredPosition = lookAtPoint + castDir * allowedDistance;
            }
        }

        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref _positionVelocity,
            positionSmoothTime
        );

        Vector3 desiredLookAt = target.position + lookAtOffset;
        _currentLookAt = Vector3.SmoothDamp(
            _currentLookAt,
            desiredLookAt,
            ref _lookAtVelocity,
            lookSmoothTime
        );
        transform.LookAt(_currentLookAt);
    }

    /// <summary>
    /// Desired camera world position based on the target position and the
    /// camera's current (slowly catching up) yaw.
    /// </summary>
    private Vector3 ComputeDesiredPosition()
    {
        Quaternion yawRotation = Quaternion.Euler(0f, _currentYaw, 0f);
        return target.position + yawRotation * offset;
    }
}
