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
	public bool zeroOnHardReverse = false;

	[Header("Camera / Yaw")]
	public Transform cameraTransform;
	[Tooltip("Degrees per second for yaw alignment (set high for instant).")]
	public float yawLerpSpeed = 720f;

	[Header("Sprint")]
	private bool sprinting; 

	// -------- Jump Parameters --------
	[Header("Jump")]
	[Tooltip("Desired jump apex height in meters.")]
	public float jumpHeight = 1.4f;
	[Tooltip("Extra mid-air gravity for snappier arc (0 = none).")]
	public float extraGravity = 0f;
	[Tooltip("Time after leaving ground you can still press jump (coyote).")]
	public float coyoteTime = 0.12f;
	[Tooltip("Time a jump input is buffered before actual landing.")]
	public float jumpBufferTime = 0.12f;
	[Tooltip("Number of extra jumps allowed while airborne.")]
	public int maxAirJumps = 0;

	[Header("Ground Check")]
	[Tooltip("Layer mask for walkable ground.")]
	public LayerMask groundMask = ~0;
	[Tooltip("Radius of the sphere used for ground probing.")]
	public float groundProbeRadius = 0.35f;
	[Tooltip("Distance downward to cast from probe origin.")]
	public float groundProbeDistance = 0.3f;
	[Tooltip("Upward offset from transform.position to start the sphere cast (if your pivot is at the feet keep small).")]
	public float groundProbeOriginOffset = 0.05f;

	// Jump / Ground state
	private bool isGrounded;
	private float lastGroundedTime;
	private float lastJumpPressedTime = -999f;
	private int airJumpsUsed;
	private bool jumpConsumedThisFrame;

	// For gizmos
	private RaycastHit lastGroundHit;
	private bool jumpPossibleNow;

	// References
	private ControlObserver controlObserver;
	private Rigidbody rb;

	// State
	private Vector3 desiredVelocity;
	private Vector3 planarVelocity;

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
		SetDesiredVelocity();

		if (controlObserver.ConsumeJump())
		{
			lastJumpPressedTime = Time.time;
		}
	}

	void FixedUpdate()
	{
		ProbeGround();

		HandleJumpLogic();

		ApplyPlanarVelocity();

		if (!isGrounded && extraGravity > 0f)
		{
			rb.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);
		}
	}

	/**
	 * PLANAR MOVEMENT
	 */

	private void ApplyPlanarVelocity()
	{
		float dt = Time.fixedDeltaTime;

		ResetVelocityIfTurningThresholdIsReached();

		float tau = CalculateTau();

		if (tau <= 0f)
		{
			planarVelocity = desiredVelocity;
		}
		else
		{
			float alpha = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, tau));
			planarVelocity += (desiredVelocity - planarVelocity) * alpha;
		}

		rb.linearVelocity = new Vector3(planarVelocity.x, rb.linearVelocity.y, planarVelocity.z);
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

	/**
	 * GROUND CHECK
	 */

	private void ProbeGround()
	{
		Vector3 origin = transform.position + Vector3.up * (groundProbeOriginOffset + groundProbeRadius);
		float castDistance = groundProbeDistance;

		bool hitSomething = Physics.SphereCast(
			origin,
			groundProbeRadius,
			Vector3.down,
			out RaycastHit hit,
			castDistance,
			groundMask,
			QueryTriggerInteraction.Ignore
		);

		if (hitSomething)
		{
			lastGroundHit = hit;
			if (!isGrounded) // just landed
			{
				// Reset air jumps when newly grounded
				airJumpsUsed = 0;
			}
			isGrounded = true;
			lastGroundedTime = Time.time;
		}
		else
		{
			isGrounded = false;
		}
	}

	/**
	 * JUMP LOGIC
	 */

	private void HandleJumpLogic()
	{
		jumpConsumedThisFrame = false;

		bool hasBufferedJump = (Time.time - lastJumpPressedTime) <= jumpBufferTime;

		bool withinCoyote = (Time.time - lastGroundedTime) <= coyoteTime;

		bool canGroundJump = isGrounded || withinCoyote;
		bool canAirJump = !canGroundJump && airJumpsUsed < maxAirJumps;

		jumpPossibleNow = canGroundJump || canAirJump;

		if (!hasBufferedJump || !jumpPossibleNow)
			return;

		PerformJump(canGroundJump);

		lastJumpPressedTime = -999f;
		jumpConsumedThisFrame = true;
	}

	private void PerformJump(bool groundOrCoyote)
	{
		// Calculate vertical velocity needed for desired jump height (classic v = sqrt(2gh))
		float jumpVelocity = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * jumpHeight);

		Vector3 v = rb.linearVelocity;
		if (v.y < 0f) v.y = 0f;    // remove downward momentum
		v.y = jumpVelocity;
		rb.linearVelocity = v;

		if (!groundOrCoyote)
		{
			airJumpsUsed++;
		}
	}

	public void SetSprinting(bool value) => sprinting = value;

#if UNITY_EDITOR
	void OnDrawGizmosSelected()
	{
		Gizmos.color = Color.yellow;
		Gizmos.DrawLine(transform.position, transform.position + desiredVelocity);
		Gizmos.color = Color.green;
		Gizmos.DrawLine(transform.position, transform.position + planarVelocity);

		DrawGroundProbeGizmos();
	}

	private void OnDrawGizmos()
	{
		DrawGroundProbeGizmos();
	}

	private void DrawGroundProbeGizmos()
	{
		Gizmos.color = jumpPossibleNow ? Color.green : Color.red;

		Vector3 origin = transform.position + Vector3.up * (groundProbeOriginOffset + groundProbeRadius);
		Vector3 end = origin + Vector3.down * groundProbeDistance;

		Gizmos.DrawLine(origin, end);

		Gizmos.DrawWireSphere(origin, groundProbeRadius);

		if (lastGroundHit.collider != null)
		{
			Vector3 hitPoint = lastGroundHit.point + Vector3.up * groundProbeRadius;
			Gizmos.DrawWireSphere(hitPoint, groundProbeRadius * 0.9f);
		}
		else
		{
			Gizmos.DrawWireSphere(end, groundProbeRadius * 0.9f);
		}
	}
#endif
}
