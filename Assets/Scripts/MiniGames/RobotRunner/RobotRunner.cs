using UnityEngine;

// Player avatar for the endless runner. World scrolls toward the player
// (constant z = 0); this script handles touch-swipe / arrow-key lane
// switching and reports collisions to RunnerGameManager.
[RequireComponent(typeof(Collider))]
public class RobotRunner : MonoBehaviour
{
    [Header("Lanes")]
    [Tooltip("X positions of left / center / right lanes.")]
    public float[] laneXs = new float[] { -2f, 0f, 2f };
    public int startingLane = 1;
    public float laneSwitchSpeed = 12f;

    [Header("Input")]
    [Tooltip("Horizontal swipe distance (px) needed to switch a lane.")]
    public float swipeThresholdPx = 60f;

    [Header("Refs")]
    public RunnerGameManager gameManager;

    private int _currentLane;
    private Vector2 _touchStart;
    private bool _tracking;

    void Start()
    {
        _currentLane = Mathf.Clamp(startingLane, 0, laneXs.Length - 1);
        var p = transform.position;
        transform.position = new Vector3(laneXs[_currentLane], p.y, p.z);
    }

    void Update()
    {
        if (gameManager != null && gameManager.IsGameOver) return;

        HandleKeyboard();
        HandleTouch();

        // Smoothly slide toward the active lane's X.
        var pos = transform.position;
        float targetX = laneXs[_currentLane];
        pos.x = Mathf.MoveTowards(pos.x, targetX, laneSwitchSpeed * Time.deltaTime);
        transform.position = pos;
    }

    void HandleKeyboard()
    {
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
            _currentLane = Mathf.Max(0, _currentLane - 1);
        else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
            _currentLane = Mathf.Min(laneXs.Length - 1, _currentLane + 1);
    }

    void HandleTouch()
    {
        if (Input.touchCount == 0) return;
        var t = Input.GetTouch(0);
        if (t.phase == TouchPhase.Began)
        {
            _touchStart = t.position;
            _tracking = true;
        }
        else if (_tracking && (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Ended))
        {
            float dx = t.position.x - _touchStart.x;
            if (Mathf.Abs(dx) >= swipeThresholdPx)
            {
                _currentLane = Mathf.Clamp(
                    _currentLane + (dx > 0 ? 1 : -1),
                    0,
                    laneXs.Length - 1
                );
                _tracking = false;
            }
            else if (t.phase == TouchPhase.Ended)
            {
                _tracking = false;
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Obstacle") && gameManager != null)
        {
            gameManager.GameOver();
        }
    }
}
