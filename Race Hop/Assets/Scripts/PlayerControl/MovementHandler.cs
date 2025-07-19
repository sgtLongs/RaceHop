using UnityEngine;

[RequireComponent(typeof(ControlObserver))]
[RequireComponent(typeof(Rigidbody))]
public class MovementHandler : MonoBehaviour
{
	[Header("Movement")]
	public float speed = 10f;
	public float accelerationTime = 0.1f;
	public float decelerationTime = 0.4f;

	[Header("Options")]
	public Transform cameraTransform;

	private ControlObserver controlObserver;
	private Rigidbody rb;

	private Vector3 desiredVelocity;
	private Vector3 planarVelocity;

	void Awake()
	{
		controlObserver = GetComponent<ControlObserver>();
		rb = GetComponent<Rigidbody>();
		rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

		if (cameraTransform == null && Camera.main != null)
		{
			cameraTransform = Camera.main.transform;
		}
	}

	void Update()
	{
		Vector2 input = controlObserver.MoveDirection;

		if (input.sqrMagnitude > 0.0001f)
		{
			Vector3 dir;
			if (cameraTransform)
			{
				Vector3 fwd = cameraTransform.forward; fwd.y = 0; fwd.Normalize();
				Vector3 right = cameraTransform.right; right.y = 0; right.Normalize();
				dir = (right * input.x + fwd * input.y).normalized;
			}
			else
			{
				dir = new Vector3(input.x, 0f, input.y).normalized;
			}

			desiredVelocity = dir * speed;
		}
		else
		{
			desiredVelocity = Vector3.zero;
		}
	}

	void FixedUpdate()
	{
		float accelRate;
		if (desiredVelocity.sqrMagnitude > planarVelocity.sqrMagnitude + 0.0001f)
		{
			accelRate = (accelerationTime <= 0f) ? float.PositiveInfinity : speed / accelerationTime;
		}
		else
		{
			accelRate = (decelerationTime <= 0f) ? float.PositiveInfinity : speed / decelerationTime;
		}

		float maxDelta = accelRate * Time.fixedDeltaTime;

		Vector3 diff = desiredVelocity - planarVelocity;
		if (diff.sqrMagnitude <= maxDelta * maxDelta)
		{
			planarVelocity = desiredVelocity;
		}
		else
		{
			planarVelocity += diff.normalized * maxDelta;
		}

		Vector3 vel = rb.linearVelocity;
		rb.linearVelocity = new Vector3(planarVelocity.x, vel.y, planarVelocity.z);
	}
}
