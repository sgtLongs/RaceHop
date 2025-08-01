using UnityEngine;

public class PauseToSettings : MonoBehaviour
{
    public CanvasGroup pauseMenuGroup;    // Assign in Inspector
    public CanvasGroup settingsMenuGroup; // Assign in Inspector

    public void GoToSettings()
    {
        Debug.Log("gotosettings");
        // Hide Pause Menu
        pauseMenuGroup.alpha = 0;
        pauseMenuGroup.interactable = false;
        pauseMenuGroup.blocksRaycasts = false;

        // Show Settings Menu
        settingsMenuGroup.alpha = 1;
        settingsMenuGroup.interactable = true;
        settingsMenuGroup.blocksRaycasts = true;
    }
    public void GoBack()
    {
        Debug.Log("GoBack");
        // Hide Pause Menu
        pauseMenuGroup.alpha = 1;
        pauseMenuGroup.interactable = true;
        pauseMenuGroup.blocksRaycasts = true;

        // Show Settings Menu
        settingsMenuGroup.alpha = 0;
        settingsMenuGroup.interactable = false;
        settingsMenuGroup.blocksRaycasts = false;
    }
}