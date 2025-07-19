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

	[Header("Crouch")]
	public bool enableCrouch = true;
	public bool useToggleFromObserver = false;
	public float standingHeight = 2.0f;
	public float crouchHeight = 1.2f;
	[Tooltip("Speed multiplier while crouched.")]
	public float crouchSpeedMultiplier = 0.55f;
	[Tooltip("Lerp speed for height & camera.")]
	public float crouchTransitionSpeed = 10f;
	[Tooltip("Local Y for camera when standing (eye).")]
	public float standingEyeHeight = 1.7f;
	[Tooltip("Local Y for camera when crouched.")]
	public float crouchedEyeHeight = 1.0f;
	[Tooltip("Layer mask for checking head clearance when uncrouching.")]
	public LayerMask ceilingMask = ~0;
	[Tooltip("Extra head clearance margin before allowing stand up.")]
	public float headClearanceBuffer = 0.05f;

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

	private CapsuleCollider capsule;
	public Transform cameraPivot;

	private bool isCrouching;
	private float currentTargetHeight;

	// Platform / moving ground
	private Rigidbody groundBody;
	private Transform groundTransform;
	private Vector3 groundVelocity;
	private Vector3 lastGroundPos;
	private bool hadGroundLastFrame;

	// For continuous relative mode
	public enum PlatformInheritMode { None, OnLandingImpulse, Continuous, ContinuousHybrid }
	public PlatformInheritMode inheritMode = PlatformInheritMode.Continuous;
	private Transform currentPlatform;
	private Vector3 platformLastPos;
	private bool platformJustSet;



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
		capsule = GetComponent<CapsuleCollider>();
		rb = GetComponent<Rigidbody>();

		currentTargetHeight = standingHeight;
		groundProbeRadius = Mathf.Min(groundProbeRadius, capsule.radius * 0.95f);

		// Prevent tipping
		rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

		if (cameraTransform == null && Camera.main != null)
			cameraTransform = Camera.main.transform;
	}

	void Update()
	{
		SetDesiredVelocity();

		if (enableCrouch)
		{
			HandleCrouchInput();
		}

		if (controlObserver.ConsumeJump())
		{
			lastJumpPressedTime = Time.time;
		}
	}

	void FixedUpdate()
	{
		ProbeGround();
		ApplyPlatformDelta();
		HandleJumpLogic();
		ApplyPlanarVelocity();

		if (!isGrounded && extraGravity > 0f)
			rb.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);
	}


	void LateUpdate()
	{
		AdjustTransformsForCrouching();
	}

	private void AdjustTransformsForCrouching()
	{
		if (!enableCrouch) return;

		float currentHeight = capsule.height;
		float targetHeight = currentTargetHeight;

		CalculateCapsuleHeightAndCenter(currentHeight, targetHeight);

		isCrouching = capsule.height < (standingHeight - 0.01f);

		MoveCamera(targetHeight);
	}

	private void MoveCamera(float targetHeight)
	{
		if (cameraPivot)
		{
			float targetEye = (targetHeight == crouchHeight) ? crouchedEyeHeight : standingEyeHeight;
			Vector3 lp = cameraPivot.localPosition;
			lp.y = Mathf.MoveTowards(lp.y, targetEye, crouchTransitionSpeed * Time.deltaTime);
			cameraPivot.localPosition = lp;
		}
	}

	private void CalculateCapsuleHeightAndCenter(float currentHeight, float targetHeight)
	{
		if (!Mathf.Approximately(currentHeight, targetHeight))
		{
			float newHeight = Mathf.MoveTowards(currentHeight, targetHeight, crouchTransitionSpeed * Time.deltaTime);
			capsule.height = newHeight;
			float heightDelta = standingHeight - capsule.height;
			capsule.center = new Vector3(0, -heightDelta * 0.5f, 0);

		}
	}


	/**
	 * PLANAR MOVEMENT
	 */

	private void ApplyPlanarVelocity()
	{
		// 1. Apply platform transform delta (if any).
		ApplyPlatformDelta();

		float dt = Time.fixedDeltaTime;

		// 2. Smooth player-controlled planar velocity (world space).
		ResetVelocityIfTurningThresholdIsReached();

		bool speedingUp = desiredVelocity.sqrMagnitude >
						  planarVelocity.sqrMagnitude + 0.0001f;
		float tau = speedingUp ? accelTau : decelTau;

		float alpha = (tau <= 0f) ? 1f :
					  1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, tau));

		planarVelocity += (desiredVelocity - planarVelocity) * alpha;

		// 3. Write to rigidbody (preserve vertical component).
		Vector3 v = rb.linearVelocity;
		v.x = planarVelocity.x;
		v.z = planarVelocity.z;
		rb.linearVelocity = v;
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
			sprinting = controlObserver.SprintHeld && movingForward && !isCrouching; // usually disallow sprint while crouched
		}

		float speed = maxSpeed;
		if (sprinting)
			speed *= sprintMultiplier;
		if (isCrouching)
			speed *= crouchSpeedMultiplier;

		return speed;
	}



	private void ResetVelocityIfTurningThresholdIsReached()
	{
		if (!zeroOnHardReverse) return;

		if (desiredVelocity != Vector3.zero && planarVelocity != Vector3.zero)
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
		hadGroundLastFrame = isGrounded;

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
			isGrounded = true;
			lastGroundedTime = Time.time;

			Transform plat = hit.collider.transform;
			if (plat != currentPlatform)
			{
				currentPlatform = plat;
				platformLastPos = currentPlatform.position; // initialize so first delta = 0
				platformJustSet = true;
			}

			if (!hadGroundLastFrame)
				airJumpsUsed = 0;
		}
		else
		{
			isGrounded = false;
			currentPlatform = null;
		}
	}


	private Vector3 GetPlatformVelocity(RaycastHit hit)
	{
		// If dynamic rigidbody with angular movement
		if (groundBody && !groundBody.isKinematic)
		{
			return groundBody.GetPointVelocity(hit.point);
		}

		// If kinematic body or no rigidbody, estimate from transform delta
		if (groundTransform)
		{
			Vector3 currentPos = groundTransform.position;
			Vector3 vel = Vector3.zero;
			if (hadGroundLastFrame && inheritMode != PlatformInheritMode.None)
				vel = (currentPos - lastGroundPos) / Time.fixedDeltaTime;
			lastGroundPos = currentPos;
			return vel;
		}

		return Vector3.zero;
	}

	private void ApplyPlatformDelta()
	{
		if (!isGrounded || currentPlatform == null) return;

		Vector3 currentPos = currentPlatform.position;

		// If we just landed / changed platform, we don't want a huge delta spike.
		if (platformJustSet)
		{
			platformLastPos = currentPos;
			platformJustSet = false;
			return;
		}

		Vector3 delta = currentPos - platformLastPos;

		if (delta.sqrMagnitude > 0f)
		{
			// Only horizontal (your platform only moves on X; keep general)
			// Remove this line if later you want vertical lifts:
			delta.y = 0f;

			// Move the rigidbody with the platform.
			rb.MovePosition(rb.position + delta);
		}

		platformLastPos = currentPos;
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
		if (isCrouching && HasHeadClearance())
		{
			currentTargetHeight = standingHeight;
		}

		float jumpVelocity = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * jumpHeight);

		Vector3 v = rb.linearVelocity;
		if (v.y < 0f) v.y = 0f;
		v.y = jumpVelocity;
		rb.linearVelocity = v;

		if (!groundOrCoyote)
		{
			airJumpsUsed++;
		}
	}

	public void SetSprinting(bool value) => sprinting = value;

	/**
	 * CROUCH
	 */
	private void HandleCrouchInput()
	{
		bool inputCrouch = controlObserver.CrouchHeld;

		if (inputCrouch)
		{
			currentTargetHeight = crouchHeight;
		}
		else
		{
			if (HasHeadClearance())
			{
				currentTargetHeight = standingHeight;
			}
			else
			{
				currentTargetHeight = crouchHeight;
			}
		}
	}

	private bool HasHeadClearance()
	{
		float standCenterY = standingHeight * 0.5f;
		Vector3 center = transform.position + Vector3.up * standCenterY;
		float radius = capsule.radius * 0.95f;
		float halfHeight = (standingHeight * 0.5f) - radius;

		Vector3 point1 = transform.position + Vector3.up * radius;
		Vector3 point2 = transform.position + Vector3.up * (standingHeight - radius);

		return !Physics.CheckCapsule(point1, point2, radius - 0.01f, ceilingMask, QueryTriggerInteraction.Ignore);
	}


#if UNITY_EDITOR
	void OnDrawGizmosSelected()
	{
		Gizmos.color = Color.cyan;
		if (capsule)
		{
			float h = capsule.height;
			float r = capsule.radius;
			Vector3 bottom = transform.position + Vector3.up * r;
			Vector3 top = transform.position + Vector3.up * (h - r);
			Gizmos.DrawWireSphere(bottom, r);
			Gizmos.DrawWireSphere(top, r);

			if (enableCrouch && currentTargetHeight == standingHeight && capsule.height < standingHeight)
			{
				Gizmos.color = HasHeadClearance() ? Color.green : Color.red;
				float standR = r;
				Vector3 sBottom = transform.position + Vector3.up * standR;
				Vector3 sTop = transform.position + Vector3.up * (standingHeight - standR);
				Gizmos.DrawWireSphere(sTop, standR * 0.9f);
				Gizmos.DrawLine(sBottom, sTop);
			}
		}

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
