using UnityEngine;

// Game state for the endless runner. Score = seconds survived. Owns the
// "exit to Flutter" handshake — when the user presses the Exit button,
// SendToFlutter publishes "scene_exit" so the Flutter side pops the screen.
public class RunnerGameManager : MonoBehaviour
{
    [Header("Refs")]
    public RobotRunner player;
    public ObstacleSpawner spawner;
    public RunnerGameUI ui;

    public bool IsGameOver { get; private set; }
    public float Score { get; private set; }

    void Update()
    {
        if (!IsGameOver)
        {
            Score += Time.deltaTime;
            if (ui != null) ui.SetScore(Mathf.FloorToInt(Score));
        }
    }

    public void GameOver()
    {
        if (IsGameOver) return;
        IsGameOver = true;
        if (ui != null) ui.ShowGameOver(Mathf.FloorToInt(Score));
    }

    public void Restart()
    {
        IsGameOver = false;
        Score = 0f;
        if (spawner != null) spawner.ClearAll();
        if (ui != null) ui.HideGameOver();
        if (player != null) player.transform.position = new Vector3(0f, player.transform.position.y, 0f);
    }

    public void Exit()
    {
        // Tell Flutter to pop the MiniGameScreen. SendToFlutter is the
        // existing thin wrapper around the flutter_embed_unity bridge.
        SendToFlutter.Send("scene_exit");
    }
}
