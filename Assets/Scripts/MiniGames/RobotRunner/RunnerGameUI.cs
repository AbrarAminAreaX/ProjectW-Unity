using UnityEngine;
using UnityEngine.UI;
using TMPro;

// In-game UI: top-left score, top-right exit, center game-over panel with
// Restart + Exit buttons. All references wired via Inspector.
public class RunnerGameUI : MonoBehaviour
{
    [Header("HUD")]
    public TMP_Text scoreText;
    public Button exitButton;

    [Header("Game Over panel")]
    public GameObject gameOverPanel;
    public TMP_Text finalScoreText;
    public Button restartButton;
    public Button gameOverExitButton;

    [Header("Refs")]
    public RunnerGameManager gameManager;

    void Start()
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (exitButton != null) exitButton.onClick.AddListener(OnExit);
        if (restartButton != null) restartButton.onClick.AddListener(OnRestart);
        if (gameOverExitButton != null) gameOverExitButton.onClick.AddListener(OnExit);
    }

    public void SetScore(int score)
    {
        if (scoreText != null) scoreText.text = score.ToString();
    }

    public void ShowGameOver(int finalScore)
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (finalScoreText != null) finalScoreText.text = finalScore.ToString();
    }

    public void HideGameOver()
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    void OnExit()
    {
        if (gameManager != null) gameManager.Exit();
    }

    void OnRestart()
    {
        if (gameManager != null) gameManager.Restart();
    }
}
