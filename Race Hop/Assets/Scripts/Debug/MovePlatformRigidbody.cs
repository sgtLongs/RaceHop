using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class MovePlatformRigidbody : MonoBehaviour
{
	[Header("Movement Settings")]
	public Vector3 moveDirection = Vector3.forward;  // Default movement along Z
	public float speed = 5f;                         // Units per second

	private Rigidbody rb;

	void Awake()
	{
		rb = GetComponent<Rigidbody>();
		rb.interpolation = RigidbodyInterpolation.Interpolate; // Smooth movement
		rb.constraints = RigidbodyConstraints.FreezeRotation; // Optional: prevent spinning
	}

	void FixedUpdate()
	{
		// Calculate velocity for constant speed
		rb.linearVelocity = moveDirection.normalized * speed;
	}
}
