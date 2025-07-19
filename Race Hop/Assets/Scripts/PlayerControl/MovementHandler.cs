using UnityEngine;

[RequireComponent(typeof(ControlObserver))]
[RequireComponent(typeof(Rigidbody))]
public class MovementHandler : MonoBehaviour
{
	[Header("Speeds")]
	public float maxSpeed = 5f;
	public float sprintMultiplier = 1.6f;

	[Header("Response (Time Constants)")]
	[Tooltip("Seconds to reach ?63% of target speed when accelerating.")]
	public float accelTau = 0.15f;
	[Tooltip("Seconds to decay ?63% toward zero when decelerating.")]
	public float decelTau = 0.30f;

	[Header("Reverse Handling (usually off for FPS)")]
	[Range(-1f, 1f)]
	public float directionFlipThreshold = -0.2f;
	public bool zeroOnHardReverse = false;   // Off by default for strafe movement

	[Header("Camera / Yaw")]
	public Transform cameraTransform;
	[Tooltip("Degrees per second for yaw alignment (set high for instant).")]
	public float yawLerpSpeed = 720f;  // big number ~= snap

	[Header("Sprint")]
	private bool sprinting;            // Set via SetSprinting or future input

	// References
	private ControlObserver controlObserver;
	private Rigidbody rb;

	// State
	private Vector3 desiredVelocity;    // Target horizontal velocity
	private Vector3 planarVelocity;     // Smoothed horizontal velocity

	void Awake()
	{
		controlObserver = GetComponent<ControlObserver>();
		rb = GetComponent<Rigidbody>();

		// Prevent tipping
		rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

		if (cameraTransform == null && Camera.main != null)
			cameraTransform = Camera.main.transform;
	}

	//Get Desired Velocity
	void Update()
	{
		SetDesiredVelocity();
	}

	private void SetDesiredVelocity()
	{
		Vector2 input = controlObserver.MoveDirection;
		float targetSpeed = CalculateTargetSpeed();

		Vector3 camFwd = Vector3.forward;
		Vector3 camRight = Vector3.right;

		(camFwd, camRight) = SetCameraFacingNormals(camFwd, camRight);

		if (input.sqrMagnitude > 0.0001f)
		{
			desiredVelocity = CalculateDesiredVelocity(input, targetSpeed, camFwd, camRight);
		}
		else
		{
			desiredVelocity = Vector3.zero;
		}
	}

	private Vector3 CalculateDesiredVelocity(Vector2 input, float targetSpeed, Vector3 camFwd, Vector3 camRight)
	{
		Vector3 moveDir = (camRight * input.x + camFwd * input.y);
		if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();
		return moveDir * targetSpeed;
	}

	private (Vector3, Vector3) SetCameraFacingNormals(Vector3 camFwd, Vector3 camRight)
	{
		if (cameraTransform)
		{
			camFwd = cameraTransform.forward; camFwd.y = 0f; camFwd.Normalize();
			camRight = cameraTransform.right; camRight.y = 0f; camRight.Normalize();
		}

		return (camFwd, camRight);
	}

	private float CalculateTargetSpeed()
	{
		if (controlObserver != null)
		{
			bool movingForward = controlObserver.MoveDirection.y > 0.01f;
			sprinting = controlObserver.SprintHeld && movingForward;
		}

		float targetSpeed = maxSpeed * (sprinting ? sprintMultiplier : 1f);

		return targetSpeed;
	}


	void FixedUpdate()
	{
		float deltaTime = Time.fixedDeltaTime;

		ResetVelocityIfTurningThresholdIsReached();

		float tau = CalculateTau();

		if (tau <= 0f)
		{
			planarVelocity = desiredVelocity;
		}
		else
		{
			float alpha = 1f - Mathf.Exp(-deltaTime / Mathf.Max(0.0001f, tau));
			planarVelocity += (desiredVelocity - planarVelocity) * alpha;
		}

		rb.linearVelocity = new Vector3(planarVelocity.x, rb.linearVelocity.y, planarVelocity.z);
	}

	private void ResetVelocityIfTurningThresholdIsReached()
	{
		if (zeroOnHardReverse &&
					desiredVelocity != Vector3.zero &&
					planarVelocity != Vector3.zero)
		{
			float dot = Vector3.Dot(planarVelocity.normalized, desiredVelocity.normalized);

			if (dot < directionFlipThreshold)
			{
				planarVelocity = Vector3.zero;
			}
		}
	}

	private float CalculateTau()
	{
		bool speedingUp = desiredVelocity.sqrMagnitude > planarVelocity.sqrMagnitude + 0.0001f;

		float tau = speedingUp ? accelTau : decelTau;
		return tau;
	}

	// Public API to set sprint state (wire up later)
	public void SetSprinting(bool value) => sprinting = value;

#if UNITY_EDITOR
	void OnDrawGizmosSelected()
	{
		Gizmos.color = Color.yellow;
		Gizmos.DrawLine(transform.position, transform.position + desiredVelocity);
		Gizmos.color = Color.green;
		Gizmos.DrawLine(transform.position, transform.position + planarVelocity);
	}
#endif
}
