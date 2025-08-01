using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class ExitGameButton : MonoBehaviour
{
	private Button btn;

	private void Awake()
	{
		btn = GetComponent<Button>();
	}

	private void OnEnable()
	{
		btn.onClick.AddListener(QuitGame);
	}

	private void OnDisable()
	{
		btn.onClick.RemoveListener(QuitGame);
	}

	public void QuitGame()
	{
#if UNITY_EDITOR
		// Stop Play Mode in the Editor
		UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_WEBGL
        // WebGL cannot quit the browser tab
        Debug.Log("Quit not supported on WebGL.");
#else
        Application.Quit();
#endif
	}
}
