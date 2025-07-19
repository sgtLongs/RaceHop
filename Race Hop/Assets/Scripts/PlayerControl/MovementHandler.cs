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
	[Tooltip("If true, player yaw follows camera yaw every frame.")]
	public bool syncYawToCamera = true;
	[Tooltip("Degrees per second for yaw alignment (set high for instant).")]
	public float yawLerpSpeed = 720f;  // big number ~= snap

	[Header("Sprint")]
	public bool enableSprint = false;
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

	void Update()
	{
		// 1. Read raw input (already updated before Update by Input System)
		Vector2 input = controlObserver.MoveDirection;   // (x = right, y = forward)

		float targetSpeed = maxSpeed * (enableSprint && sprinting ? sprintMultiplier : 1f);

		// 2. Prepare a stable camera basis BEFORE possibly adjusting player yaw
		Vector3 camFwd = Vector3.forward;
		Vector3 camRight = Vector3.right;

		if (cameraTransform)
		{
			camFwd = cameraTransform.forward; camFwd.y = 0f; camFwd.Normalize();
			camRight = cameraTransform.right; camRight.y = 0f; camRight.Normalize();
		}

		// 3. Build desired horizontal velocity
		if (input.sqrMagnitude > 0.0001f)
		{
			Vector3 moveDir = (camRight * input.x + camFwd * input.y);
			if (moveDir.sqrMagnitude > 1f) moveDir.Normalize(); // diagonals
			desiredVelocity = moveDir * targetSpeed;
		}
		else
		{
			desiredVelocity = Vector3.zero;
		}

		// 4. Yaw alignment (FPS pattern). Player yaw matches camera yaw; movement DOES NOT rotate player.
		if (syncYawToCamera && cameraTransform)
		{
			float camYaw = cameraTransform.eulerAngles.y;
			Quaternion targetYaw = Quaternion.Euler(0f, camYaw, 0f);
			// Lerp by angular speed (deg/sec)
			if (yawLerpSpeed <= 0f)
			{
				transform.rotation = targetYaw;
			}
			else
			{
				transform.rotation = Quaternion.RotateTowards(
					transform.rotation,
					targetYaw,
					yawLerpSpeed * Time.deltaTime
				);
			}
		}
	}

	void FixedUpdate()
	{
		float dt = Time.fixedDeltaTime;

		// 5. Optional hard reverse snap (usually disabled for strafing)
		if (zeroOnHardReverse &&
			desiredVelocity != Vector3.zero &&
			planarVelocity != Vector3.zero)
		{
			float dot = Vector3.Dot(planarVelocity.normalized, desiredVelocity.normalized);
			if (dot < directionFlipThreshold)
				planarVelocity = Vector3.zero;
		}

		// 6. Choose tau based on speeding up vs slowing down
		bool speedingUp = desiredVelocity.sqrMagnitude >
						  planarVelocity.sqrMagnitude + 0.0001f;

		float tau = speedingUp ? accelTau : decelTau;

		if (tau <= 0f)
		{
			planarVelocity = desiredVelocity;
		}
		else
		{
			float alpha = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, tau));
			planarVelocity += (desiredVelocity - planarVelocity) * alpha;
		}

		// 7. Apply horizontal velocity; keep vertical component (gravity, future jump)
		Vector3 current = rb.linearVelocity;
		rb.linearVelocity = new Vector3(planarVelocity.x, current.y, planarVelocity.z);
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
