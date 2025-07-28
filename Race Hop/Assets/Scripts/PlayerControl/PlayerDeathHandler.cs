using UnityEditor.SearchService;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerDeathHandler : MonoBehaviour
{
	// Start is called once before the first execution of Update after the MonoBehaviour is created
	private void OnDestroy()
	{
		SceneManager.LoadScene(SceneManager.GetActiveScene().name);
	}
}
