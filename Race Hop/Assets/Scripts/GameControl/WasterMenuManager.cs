using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;      // Button lives here, even for TMP-styled buttons
using TMPro;


#if UNITY_EDITOR
using UnityEditor;         // Needed only to exit Play-mode in the editor
#endif

/// <summary>
/// Handles “Quit” and “Reload” UI buttons.
/// Attach to any GameObject in the scene,
/// then assign the buttons in the Inspector.
/// </summary>
public class WasterMenuManager : MonoBehaviour
{

	public PlayerTracker playerTracker;

	[Header("Assign your TMP-styled UI Buttons")]
	[Tooltip("Button that closes the application (or stops Play-mode in the editor).")]
	public Button QuitButton;

	[Tooltip("Button that reloads the current scene.")]
	public Button ReloadButton;

	public TMP_Text text;

	void Awake()
	{
		if (QuitButton != null) QuitButton.onClick.AddListener(QuitGame);
		if (ReloadButton != null) ReloadButton.onClick.AddListener(ReloadScene);

		text.text = "Total Distance : " + playerTracker.maxDistanceTraveled/10 + " meters";
	}

	/// <summary>Closes the app (or stops Play-mode while in the editor).</summary>
	void QuitGame()
	{
#if UNITY_EDITOR
		EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
	}

	/// <summary>Reloads whatever scene is currently active.</summary>
	void ReloadScene()
	{
		Scene active = SceneManager.GetActiveScene();
		SceneManager.LoadScene(active.buildIndex);
	}
}
