using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class FadeTransition : MonoBehaviour
{
    public CanvasGroup fadePanel;
    public float fadeDuration = 1f;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void Start()
    {
        fadePanel.alpha = 1f;
        StartCoroutine(Fade(0));
    }

    public void FadeToScene(string sceneName)
    {
        StartCoroutine(FadeOutAndLoad(sceneName));
    }

    IEnumerator FadeOutAndLoad(string sceneName)
    {
        yield return StartCoroutine(Fade(1));
        SceneManager.LoadScene(sceneName);
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StartCoroutine(Fade(0));
    }

    IEnumerator Fade(float targetAlpha)
    {
        float startAlpha = fadePanel.alpha;
        float timer = 0f;

        while (!Mathf.Approximately(fadePanel.alpha, targetAlpha))
        {
            fadePanel.alpha = Mathf.Lerp(startAlpha, targetAlpha, timer / fadeDuration);
            timer += Time.deltaTime;
            yield return null;
        }
        fadePanel.alpha = targetAlpha;
    }
}
