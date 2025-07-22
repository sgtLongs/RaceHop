using UnityEngine;

/// <summary>
/// Rigidbody-based character movement:
/// - Smoothed camera-relative acceleration (platform friction respected)
/// - Sprinting
/// - Crouching (with head clearance)
/// - Buffered + coyote + optional mid-air jumps
/// </summary>
[RequireComponent(typeof(ControlObserver))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class RigidBodyMovementHandler : MonoBehaviour
{
	[Header("Movement Speeds")]
	public float MaxWalkSpeed = 5f;
	public float SprintMultiplier = 1.5f;

	[Header("Acceleration (Time Constants)")]
	[Tooltip("Seconds to reach ~63% of a new target speed while accelerating.")]
	public float AccelTau = 0.15f;
	[Tooltip("Seconds to reach ~63% toward zero / lower magnitude when decelerating.")]
	public float DecelTau = 0.20f;

	[Header("Hard Reverse Cutoff")]
	[Range(-1f, 1f)] public float ReverseDotThreshold = -0.2f;
	public bool ZeroVelocityOnHardReverse = true;

	[Header("Heading / Camera")]
	public Transform CameraTransform;

	// Jump / Ground ---------------------------------------------------------
	[Header("Jump")]
	[Tooltip("Desired apex height in meters.")]
	public float JumpHeight = 1.5f;
	[Tooltip("Extra downward accel while airborne for snappier arc.")]
	public float ExtraAirGravity = 20f;
	[Tooltip("Time after leaving ground jump is still allowed.")]
	public float CoyoteTime = 0.12f;
	[Tooltip("How long a pressed jump is buffered before landing.")]
	public float JumpBufferTime = 0.12f;
	[Tooltip("Number of mid-air jumps beyond the initial ground jump.")]
	public int MaxAirJumps = 0;

	[Header("Ground Probe")]
	public LayerMask GroundMask = ~0;
	[Tooltip("Sphere radius for ground check.")]
	public float GroundProbeRadius = 0.40f;
	[Tooltip("Downward cast distance from probe origin.")]
	public float GroundProbeDistance = 0.64f;
	[Tooltip("Up offset from transform.position for probe start.")]
	public float GroundProbeOriginOffset = -0.98f;

	// Crouch ---------------------------------------------------------------
	[Header("Crouch")]
	public bool EnableCrouch = true;
	public float StandingHeight = 2.0f;
	public float CrouchHeight = 1.2f;
	[Tooltip("Lerp speed for capsule & camera height transition.")]
	public float CrouchTransitionSpeed = 10f;
	[Tooltip("Eye Y local position when standing.")]
	public float StandingEyeHeight = 0.8f;
	[Tooltip("Eye Y local position when crouched.")]
	public float CrouchedEyeHeight = -0.1f;
	[Tooltip("Layer mask for ceiling (used to block uncrouch).")]
	public LayerMask CeilingMask = ~0;
	[Tooltip("Extra head clearance needed to allow standing.")]
	public float HeadClearanceBuffer = 0.05f;
	public Transform CameraPivot;

	// Components ------------------------------------------------------------
	private ControlObserver _input;
	private Rigidbody _rb;
	private CapsuleCollider _capsule;

	// Ground / Jump state
	private bool _isGrounded;
	private bool _hadGroundLastFrame;
	private float _lastGroundedAt;
	private float _lastJumpPressedAt = -999f;
	private int _airJumpsUsed;
	private bool _jumpPossibleThisFrame;
	private RaycastHit _lastGroundHit;

	// Crouch state
	private float _capsuleTargetHeight;
	private bool _isCrouched;

	// Horizontal movement state
	private Vector3 _targetHorizVelocity;

	private void Awake()
	{
		_input = GetComponent<ControlObserver>();
		_rb = GetComponent<Rigidbody>();
		_capsule = GetComponent<CapsuleCollider>();

		_rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

		if (!CameraTransform && Camera.main) CameraTransform = Camera.main.transform;

		_capsule.height = StandingHeight;
		_capsule.center = new Vector3(0f, (StandingHeight * 0.5f) - StandingHeight * 0.5f, 0f); // will be recentered by crouch logic
		_capsuleTargetHeight = StandingHeight;

		GroundProbeRadius = Mathf.Min(GroundProbeRadius, _capsule.radius * 0.95f);
	}

	private void Update()
	{
		UpdateTargetHorizontalVelocity();

		if (EnableCrouch) UpdateCrouchInput();

		// Jump buffering
		if (_input.ConsumeJump())
			_lastJumpPressedAt = Time.time;
	}

	private void FixedUpdate()
	{
		CheckGround();
		TryProcessJump(); // may set vertical velocity
		ApplyHorizontalForces();

		if (!_isGrounded && ExtraAirGravity > 0f)
			_rb.AddForce(Vector3.down * ExtraAirGravity, ForceMode.Acceleration);
	}

	private void LateUpdate()
	{
		if (EnableCrouch)
			UpdateCrouchHeights();
	}

	// -------------------- Horizontal Movement (relative) -------------------
	private void UpdateTargetHorizontalVelocity()
	{
		Vector2 move = _input.MoveDirection;

		float baseSpeed = MaxWalkSpeed;
		bool sprinting = _input.SprintHeld && move.y > 0.01f && !_isCrouched;
		if (sprinting) baseSpeed *= SprintMultiplier;
		if (_isCrouched) baseSpeed *= 0.55f; // crouch speed multiplier

		if (move.sqrMagnitude < 0.0001f)
		{
			_targetHorizVelocity = Vector3.zero;
			return;
		}

		// Camera-relative axes
		Vector3 forward = Vector3.forward;
		Vector3 right = Vector3.right;
		if (CameraTransform)
		{
			forward = CameraTransform.forward; forward.y = 0f; forward.Normalize();
			right = CameraTransform.right; right.y = 0f; right.Normalize();
		}

		Vector3 wishDir = right * move.x + forward * move.y;
		if (wishDir.sqrMagnitude > 1f) wishDir.Normalize();

		_targetHorizVelocity = wishDir * baseSpeed;
	}

	private void ApplyHorizontalForces()
	{
		Vector3 v = _rb.linearVelocity;

		// Platform velocity if grounded
		Vector3 platformVel = Vector3.zero;
		if (_isGrounded && _lastGroundHit.collider != null)
		{
			Rigidbody platformRb = _lastGroundHit.rigidbody;
			if (platformRb) platformVel = platformRb.linearVelocity;
		}

		// Relative horizontal velocity
		Vector3 horiz = new Vector3(v.x, 0f, v.z) - new Vector3(platformVel.x, 0f, platformVel.z);

		// No input -> do nothing (preserve platform/friction motion)
		if (_targetHorizVelocity.sqrMagnitude < 0.0001f)
			return;

		// Optional hard reverse reset (use horiz relative frame)
		/*if (ZeroVelocityOnHardReverse &&
			_targetHorizVelocity != Vector3.zero &&
			horiz != Vector3.zero)
		{
			float dot = Vector3.Dot(horiz.normalized, _targetHorizVelocity.normalized);
			if (dot < ReverseDotThreshold)
				horiz = Vector3.zero;
		}*/

		bool speedingUp = _targetHorizVelocity.sqrMagnitude > horiz.sqrMagnitude + 0.0001f;
		float tau = speedingUp ? AccelTau : DecelTau;
		if (tau <= 0f) tau = 0.0001f;

		Vector3 neededAccel = (_targetHorizVelocity - horiz) / tau;
		_rb.AddForce(new Vector3(neededAccel.x, 0f, neededAccel.z), ForceMode.Acceleration);
	}

	// ------------------------- Ground & Jump -------------------------------
	private void CheckGround()
	{
		_hadGroundLastFrame = _isGrounded;

		Vector3 origin = transform.position + Vector3.up * (GroundProbeOriginOffset + GroundProbeRadius);
		bool hit = Physics.SphereCast(
			origin,
			GroundProbeRadius,
			Vector3.down,
			out RaycastHit rh,
			GroundProbeDistance,
			GroundMask,
			QueryTriggerInteraction.Ignore);

		if (hit)
		{
			_lastGroundHit = rh;
			_isGrounded = true;
			_lastGroundedAt = Time.time;
			if (!_hadGroundLastFrame)
				_airJumpsUsed = 0;
		}
		else
		{
			_isGrounded = false;
		}
	}

	private void TryProcessJump()
	{
		_jumpPossibleThisFrame = false;

		bool buffered = (Time.time - _lastJumpPressedAt) <= JumpBufferTime;
		bool withinCoyote = (Time.time - _lastGroundedAt) <= CoyoteTime;

		bool canGroundJump = _isGrounded || withinCoyote;
		bool canAirJump = !canGroundJump && _airJumpsUsed < MaxAirJumps;

		_jumpPossibleThisFrame = canGroundJump || canAirJump;

		if (!buffered || !_jumpPossibleThisFrame)
			return;

		PerformJump(canGroundJump);
		_lastJumpPressedAt = -999f;
	}

	private void PerformJump(bool groundOrCoyote)
	{
		// Stand up if crouched and we have clearance
		if (EnableCrouch && _isCrouched && HasHeadClearance())
			_capsuleTargetHeight = StandingHeight;

		float jumpVelY = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * JumpHeight);

		Vector3 vel = _rb.linearVelocity;
		if (vel.y < 0f) vel.y = 0f;
		vel.y = jumpVelY;
		_rb.linearVelocity = vel;

		if (!groundOrCoyote)
			_airJumpsUsed++;
	}

	// ----------------------------- Crouch ----------------------------------
	private void UpdateCrouchInput()
	{
		bool crouchHeld = _input.CrouchHeld;
		if (crouchHeld)
		{
			_capsuleTargetHeight = CrouchHeight;
		}
		else
		{
			_capsuleTargetHeight = HasHeadClearance() ? StandingHeight : CrouchHeight;
		}
	}

	private void UpdateCrouchHeights()
	{
		float currentHeight = _capsule.height;
		if (!Mathf.Approximately(currentHeight, _capsuleTargetHeight))
		{
			float newHeight = Mathf.MoveTowards(currentHeight, _capsuleTargetHeight, CrouchTransitionSpeed * Time.deltaTime);
			_capsule.height = newHeight;

			// Recenter so feet stay planted (center.y = halfHeight - StandingHalfHeight offset)
			float heightDelta = StandingHeight - _capsule.height;
			_capsule.center = new Vector3(0f, -heightDelta * 0.5f, 0f);
		}

		_isCrouched = _capsule.height < (StandingHeight - 0.01f);

		if (CameraPivot)
		{
			float targetEye = (_capsuleTargetHeight == CrouchHeight) ? CrouchedEyeHeight : StandingEyeHeight;
			Vector3 lp = CameraPivot.localPosition;
			lp.y = Mathf.MoveTowards(lp.y, targetEye, CrouchTransitionSpeed * Time.deltaTime);
			CameraPivot.localPosition = lp;
		}
	}

	private bool HasHeadClearance()
	{
		float radius = _capsule.radius * 0.95f;
		Vector3 point1 = transform.position + Vector3.up * radius;
		Vector3 point2 = transform.position + Vector3.up * (StandingHeight - radius);
		return !Physics.CheckCapsule(point1, point2, radius - 0.01f, CeilingMask, QueryTriggerInteraction.Ignore);
	}

	// ----------------------------- Gizmos ----------------------------------
#if UNITY_EDITOR
	private void OnDrawGizmosSelected()
	{
		// Desired velocity
		Gizmos.color = Color.yellow;
		Gizmos.DrawLine(transform.position, transform.position + _targetHorizVelocity);

		// Ground probe
		DrawGroundProbeGizmos();

		// Crouch clearance preview
		if (EnableCrouch && _capsule != null && _capsule.height < StandingHeight)
		{
			Gizmos.color = HasHeadClearance() ? Color.green : Color.red;
			float r = _capsule.radius;
			Vector3 top = transform.position + Vector3.up * (StandingHeight - r);
			Gizmos.DrawWireSphere(top, r * 0.9f);
		}
	}

	private void OnDrawGizmos() => DrawGroundProbeGizmos();

	private void DrawGroundProbeGizmos()
	{
		Vector3 origin = transform.position + Vector3.up * (GroundProbeOriginOffset + GroundProbeRadius);
		Vector3 end = origin + Vector3.down * GroundProbeDistance;
		Gizmos.color = _jumpPossibleThisFrame ? Color.green : Color.red;
		Gizmos.DrawLine(origin, end);
		Gizmos.DrawWireSphere(origin, GroundProbeRadius);

		if (_lastGroundHit.collider != null)
		{
			Vector3 hitPoint = _lastGroundHit.point + Vector3.up * GroundProbeRadius;
			Gizmos.DrawWireSphere(hitPoint, GroundProbeRadius * 0.9f);
		}
		else
		{
			Gizmos.DrawWireSphere(end, GroundProbeRadius * 0.9f);
		}
	}
#endif
}
