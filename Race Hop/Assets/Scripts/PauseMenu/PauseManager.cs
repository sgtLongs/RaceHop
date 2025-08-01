using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PauseManager : MonoBehaviour
{
    public GameObject pauseMenuCanvas;
    public GameObject settingsCanvas;
    public GameController gameController;
    public CanvasGroup pauseMenuGroup;    // Assign in Inspector
    bool isPaused = false;
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (!isPaused) Pause();
            else Resume();
        }
    }

    public void Pause()
    {
        pauseMenuGroup.alpha = 1;
        pauseMenuGroup.interactable = true;
        pauseMenuGroup.blocksRaycasts = true;
        pauseMenuCanvas.SetActive(true);
        gameController.timeScale = 0f;
        isPaused = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Resume()
    {
        pauseMenuGroup.alpha = 0;
        pauseMenuGroup.interactable = false;
        pauseMenuGroup.blocksRaycasts = false;
        pauseMenuCanvas.SetActive(false);
        gameController.timeScale = 1f;
        isPaused = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
    public void QuitToMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("MainMeneScene"); // Update to your actual main menu scene name
    }
}