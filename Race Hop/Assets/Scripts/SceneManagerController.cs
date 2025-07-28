using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;

public class SceneManagerController : MonoBehaviour
{
    public CanvasGroup fadeCanvasGroup;
    public float fadeDuration = 1f;
    public GameObject mainMenuGroup;
    public GameObject settingsPanel;

    public void FadeToScene(string sceneName)
    {
        StartCoroutine(FadeAndSwitchScenes(sceneName));
    }

    private IEnumerator FadeAndSwitchScenes(string sceneName)
    {
        yield return StartCoroutine(Fade(1));
        SceneManager.LoadScene(sceneName);
    }

    private IEnumerator Fade(float targetAlpha)
    {
        fadeCanvasGroup.blocksRaycasts = true;
        float startAlpha = fadeCanvasGroup.alpha;
        float timer = 0f;

        while (!Mathf.Approximately(fadeCanvasGroup.alpha, targetAlpha))
        {
            fadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, timer / fadeDuration);
            timer += Time.deltaTime;
            yield return null;
        }

        fadeCanvasGroup.alpha = targetAlpha;
    }

    public void OnStartButtonPressed()
    {
        FadeToScene("GameScene"); // Replace with your actual game scene name
    }

    public void OnExitButtonPressed()
    {
        Application.Quit();
        Debug.Log("Quit called");
    }

    public void OnSettingsButtonPressed()
    {
        mainMenuGroup.SetActive(false);
        settingsPanel.SetActive(true);
    }

    public void OnBackButtonPressed()
    {
        settingsPanel.SetActive(false);
        mainMenuGroup.SetActive(true);
    }
}