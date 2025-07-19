using UnityEngine;

public class MovePlatform : MonoBehaviour
{
	[Header("Movement Settings")]
	[Tooltip("Units per second to move in +X (negative to move left).")]
	public float speed = 2f;

	[Tooltip("Move in world space (global X) or the object's local X axis.")]
	public Space moveSpace = Space.World;

	void Update()
	{
		float dx = speed * Time.deltaTime;

		if (moveSpace == Space.World)
		{
			// World X
			transform.position += new Vector3(dx, 0f, 0f);
		}
		else
		{
			// Local X (object's right direction)
			transform.Translate(Vector3.right * dx, Space.Self);
		}
	}
}
