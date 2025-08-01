using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;

public class SceneManagerController : MonoBehaviour
{
    [Header("Fade References")]
    public CanvasGroup fadeCanvasGroup;

    [Header("Menu Panels")]
    public CanvasGroup mainMenuGroup;
    public CanvasGroup settingsPanel;

    public float fadeDuration = 1f;

    void Start()
    {
        // Hide settings at start
        settingsPanel.alpha = 0;
        settingsPanel.interactable = false;
        settingsPanel.blocksRaycasts = false;

        // Show main menu at start
        mainMenuGroup.alpha = 1;
        mainMenuGroup.interactable = true;
        mainMenuGroup.blocksRaycasts = true;
    }

    public void OnStartButtonPressed()
    {
        FadeToScene("TopicCarSpawning"); // replace with your actual scene name
    }

    public void OnExitButtonPressed()
    {
        Application.Quit();
        Debug.Log("Quit");
    }

    public void OnSettingsButtonPressed()
    {
        StartCoroutine(SwitchMenus(mainMenuGroup, settingsPanel));
    }

    public void OnBackButtonPressed()
    {
        StartCoroutine(SwitchMenus(settingsPanel, mainMenuGroup));
    }

    public void FadeToScene(string sceneName)
    {
        StartCoroutine(FadeAndSwitchScenes(sceneName));
    }

    private IEnumerator SwitchMenus(CanvasGroup fromMenu, CanvasGroup toMenu)
    {
        yield return StartCoroutine(FadeGroup(fromMenu, 0));
        fromMenu.interactable = false;
        fromMenu.blocksRaycasts = false;

        toMenu.interactable = true;
        toMenu.blocksRaycasts = true;
        yield return StartCoroutine(FadeGroup(toMenu, 1));
    }

    private IEnumerator FadeAndSwitchScenes(string sceneName)
    {
        yield return StartCoroutine(FadeGroup(fadeCanvasGroup, 1));
        SceneManager.LoadScene("TopicCarSpawning");
    }

    private IEnumerator FadeGroup(CanvasGroup group, float targetAlpha)
    {
        float startAlpha = group.alpha;
        float timer = 0f;

        while (!Mathf.Approximately(group.alpha, targetAlpha))
        {
            group.alpha = Mathf.Lerp(startAlpha, targetAlpha, timer / fadeDuration);
            timer += Time.deltaTime;
            yield return null;
        }

        group.alpha = targetAlpha;
    }
}