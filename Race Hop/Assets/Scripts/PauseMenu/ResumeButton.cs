using UnityEngine;

public class ResumeButton : MonoBehaviour
{
    public CanvasGroup pauseMenuGroup; // Assign in Inspector

    public void ResumeGame()
    {
        pauseMenuGroup.alpha = 0;
        pauseMenuGroup.interactable = false;
        pauseMenuGroup.blocksRaycasts = false;
        Time.timeScale = 1f;
    }
}