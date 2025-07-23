using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;

public class SceneManagerController : MonoBehaviour
{
    [Header("Fade Settings")]
    public CanvasGroup fadeCanvasGroup;
    public float fadeDuration = 1f;

    [Header("Debug")]
    public bool debugFadeToNextScene;

    private void OnValidate()
    {
        if (debugFadeToNextScene)
        {
            debugFadeToNextScene = false; // auto uncheck
            FadeToScene("NextScene"); // replace with your target scene
        }
    }

    public void FadeToScene(string sceneName)
    {
        StartCoroutine(FadeAndSwitchScenes(sceneName));
    }

    private IEnumerator FadeAndSwitchScenes(string sceneName)
    {
        // Fade to black
        yield return StartCoroutine(Fade(1));

        // Load scene
        SceneManager.LoadScene(sceneName);

        // Optional: Wait a frame for scene load
        yield return null;

        // Fade from black
        yield return StartCoroutine(Fade(0));
    }

    private IEnumerator Fade(float finalAlpha)
    {
        fadeCanvasGroup.blocksRaycasts = true;

        float fadeSpeed = Mathf.Abs(fadeCanvasGroup.alpha - finalAlpha) / fadeDuration;

        while (!Mathf.Approximately(fadeCanvasGroup.alpha, finalAlpha))
        {
            fadeCanvasGroup.alpha = Mathf.MoveTowards(fadeCanvasGroup.alpha, finalAlpha, fadeSpeed * Time.deltaTime);
            yield return null;
        }

        fadeCanvasGroup.blocksRaycasts = finalAlpha == 1;
    }
}