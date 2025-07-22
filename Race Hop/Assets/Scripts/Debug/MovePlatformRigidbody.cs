using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class MovePlatformRigidbody : MonoBehaviour
{
	[Header("Movement Settings")]
	public Vector3 moveDirection = Vector3.forward;
	public float speed = 5f;

	private Rigidbody rb;

	void Awake()
	{
		rb = GetComponent<Rigidbody>();
		rb.constraints = RigidbodyConstraints.FreezeRotation;
	}

	void FixedUpdate()
	{
		// Calculate velocity for constant speed
		rb.linearVelocity = moveDirection.normalized * speed;
	}
}
