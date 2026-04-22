using UnityEngine;

/// <summary>
/// Joystick-driven player controller with "lock to facing on press" input.
///
/// Reads a normalized 2D vector from <see cref="OnScreenJoystick"/> and
/// moves the player on the XZ plane. The input is interpreted as an offset
/// from a "base yaw" that is **captured at the moment the joystick is
/// touched and stays locked until the user releases it**:
///
///   • "Up on the stick"   → walk in the direction the player was facing
///                            when the touch started (= release baseline)
///   • "Right on the stick" → walk perpendicular right of that direction
///   • etc.
///
/// On release, nothing changes immediately — the next touch recaptures
/// the base yaw from the player's current facing. This gives the user a
/// predictable "forward is forward" feel: every fresh press treats the
/// player's current facing as the new baseline, and within a single press
/// the input axes never change so the controls never spin or invert.
///
/// The camera (<see cref="ThirdPersonCamera"/>) is only notified of the
/// joystick active state so it can switch between slow yaw lag (input
/// held) and fast yaw realignment (input released). It does NOT factor
/// into the input direction math.
///
/// If the player has an <see cref="Animator"/>, this script writes a float
/// parameter named <see cref="speedParameter"/> ("Speed" by default) which
/// you can blend Idle ↔ Walk against in the Animator Controller.
///
/// Movement uses <see cref="CharacterController"/> if one is present, so
/// the player respects ground/colliders. Otherwise it falls back to direct
/// transform translation.
/// </summary>
[DisallowMultipleComponent]
public class PlayerController : MonoBehaviour
{
    [Header("Input")]
    public OnScreenJoystick joystick;

    [Tooltip("Reference to the main camera Transform. Used only to find the ThirdPersonCamera component so we can notify it of input state. The camera does NOT factor into the input direction math — the input is locked to the player's facing at touch start. Defaults to Camera.main.")]
    public Transform cameraReference;

    [Header("Movement")]
    public float moveSpeed = 3f;
    public float rotationSpeed = 720f; // deg/sec
    public float gravity = -20f;

    [Header("Animation")]
    [Tooltip("Float parameter on the Animator that drives Idle ↔ Walk blend.")]
    public string speedParameter = "Speed";

    [Tooltip("How quickly the animator's Speed value lerps toward target. Higher = snappier.")]
    public float animationSmoothing = 10f;

    private Animator _animator;
    private CharacterController _characterController;
    private ThirdPersonCamera _thirdPersonCamera;
    private float _verticalVelocity;
    private float _animatorSpeed;
    private int _speedHash;

    // Base yaw locked at the moment the user pressed the joystick. While
    // they hold, all input is interpreted as an offset from this yaw, so
    // axes never shift mid-press. Recaptured on every fresh press.
    private float _baseYaw;
    private bool _hadInputLastFrame;

    void Start()
    {
        _animator = GetComponentInChildren<Animator>();
        _characterController = GetComponent<CharacterController>();
        _speedHash = Animator.StringToHash(speedParameter);

        if (cameraReference == null && Camera.main != null)
            cameraReference = Camera.main.transform;

        if (cameraReference != null)
            _thirdPersonCamera = cameraReference.GetComponent<ThirdPersonCamera>();
    }

    void Update()
    {
        Vector2 input = joystick != null ? joystick.Value : Vector2.zero;
        bool inputActive = input.sqrMagnitude > 0.0001f;

        // Detect a fresh press (transition idle → active) and recapture
        // the base yaw from the player's CURRENT facing. This locks the
        // input axes for as long as the user holds the stick, then on
        // release the next press will recapture from wherever the player
        // is facing then. This is the "release = new baseline" behaviour.
        if (inputActive && !_hadInputLastFrame)
        {
            _baseYaw = transform.eulerAngles.y;
        }
        _hadInputLastFrame = inputActive;

        // Tell the camera whether the joystick is being held. The camera
        // uses this to switch between slow yaw lag (input held = stable
        // visuals) and fast yaw realignment (input released = snap behind
        // player ready for the next press).
        if (_thirdPersonCamera != null)
            _thirdPersonCamera.inputActive = inputActive;

        // Build the world-space movement direction by rotating the local
        // (input.x, 0, input.y) vector by the locked base yaw. The camera
        // does NOT factor in here — every press uses the player-facing
        // baseline that was captured at touch start.
        Quaternion baseRotation = Quaternion.Euler(0f, _baseYaw, 0f);
        Vector3 worldDirection = baseRotation * new Vector3(input.x, 0f, input.y);

        float inputMagnitude = Mathf.Clamp01(input.magnitude);

        // Rotate the player to face the movement direction.
        if (worldDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(worldDirection.normalized);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRot,
                rotationSpeed * Time.deltaTime
            );
        }

        // Translate.
        Vector3 horizontal = worldDirection.normalized * (moveSpeed * inputMagnitude);

        if (_characterController != null)
        {
            if (_characterController.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;
            _verticalVelocity += gravity * Time.deltaTime;

            Vector3 motion = horizontal;
            motion.y = _verticalVelocity;
            _characterController.Move(motion * Time.deltaTime);
        }
        else
        {
            transform.position += horizontal * Time.deltaTime;
        }

        // Drive the animator (if present).
        if (_animator != null && !string.IsNullOrEmpty(speedParameter))
        {
            _animatorSpeed = Mathf.Lerp(
                _animatorSpeed,
                inputMagnitude,
                animationSmoothing * Time.deltaTime
            );
            _animator.SetFloat(_speedHash, _animatorSpeed);
        }
    }
}
