using System.Collections.Generic;
using UnityEditor.SearchService;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerDeathHandler : MonoBehaviour
{
	public GameObject deathCanvas;

	private void OnDestroy()
	{
		deathCanvas = FindInactiveObjectsWithTag("DeathCanvas")[0];
		deathCanvas.SetActive(true);
		Cursor.lockState = CursorLockMode.None;
		Cursor.visible = true;

		GameObject deathCamera = FindInactiveObjectsWithTag("DeathCamera")[0];
		deathCamera.SetActive(true);
		//SceneManager.LoadScene(SceneManager.GetActiveScene().name);
	}

	public static List<GameObject> FindInactiveObjectsWithTag(string tag)
	{
		GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
		List<GameObject> matchingObjects = new List<GameObject>();

		foreach (GameObject obj in allObjects)
		{
			if (obj.CompareTag(tag) && obj.hideFlags == HideFlags.None)
			{
				matchingObjects.Add(obj);
			}
		}

		return matchingObjects;
	}
}
