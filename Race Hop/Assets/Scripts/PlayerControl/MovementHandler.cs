using UnityEngine;

/// <summary>
/// Rigidbody-based character movement: smoothed horizontal acceleration,
/// sprinting, crouching (with head clearance), buffered + coyote jumps,
/// and optional moving-platform delta following.
/// </summary>
[RequireComponent(typeof(ControlObserver))]
[RequireComponent(typeof(Rigidbody))]
public class MovementHandler : MonoBehaviour
{
	#region --- Inspector: Movement Speeds & Response ---

	[Header("Movement Speeds")]
	public float MaxWalkSpeed = 5f;
	public float SprintMultiplier = 5f;
	[Tooltip("Horizontal speed multiplier while crouched.")]
	public float CrouchSpeedMultiplier = 0.55f;

	[Header("Acceleration (Time Constants)")]
	[Tooltip("Seconds to reach ~63% of a new target speed while accelerating.")]
	public float AccelTau = 0.15f;
	[Tooltip("Seconds to decay ~63% toward zero / lower magnitude when decelerating.")]
	public float DecelTau = 0.20f;
	[Header("Hard Reverse Cutoff")]
	[Range(-1f, 1f)] public float ReverseDotThreshold = -0.2f;
	public bool ZeroVelocityOnHardReverse = true;

	[Header("Heading / Camera")]
	[Tooltip("Optional camera (used only for direction reference).")]
	public Transform CameraTransform;
	[Tooltip("Degrees per second for yaw alignment (unused here but reserved).")]
	public float YawLerpSpeed = 720f;

	#endregion

	#region --- Inspector: Crouch ---

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
	public Transform CameraPivot;   // pivot whose local Y adjusts with crouch

	#endregion

	#region --- Inspector: Jump & Ground ---

	[Header("Jump")]
	[Tooltip("Desired apex height in meters.")]
	public float JumpHeight = 5f;
	[Tooltip("Extra downward accel while airborne for snappier arc.")]
	public float ExtraAirGravity = 30f;
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

	#endregion

	#region --- Inspector: Platform Inheritance ---

	public enum PlatformInheritMode { None, OnLandingImpulse, Continuous, ContinuousHybrid }
	[Header("Moving Platform")]
	public PlatformInheritMode InheritMode = PlatformInheritMode.Continuous;

	#endregion

	#region --- Components & Cached References ---

	private ControlObserver _input;
	private Rigidbody _rb;
	private CapsuleCollider _capsule;

	#endregion

	#region --- Private State: Movement / Crouch ---

	private Vector3 _targetHorizVelocity;
	private Vector3 _currentHorizVelocity;

	private bool _sprinting;
	private bool _isCrouched;
	private float _capsuleTargetHeight;

	#endregion

	#region --- Private State: Ground / Jump ---

	private bool _isGrounded;
	private float _lastGroundedAt;
	private float _lastJumpPressedAt = -999f;
	private int _airJumpsUsed;
	private bool _jumpPossibleThisFrame;

	private RaycastHit _lastGroundHit;
	private bool _hadGroundLastFrame;

	#endregion

	#region --- Private State: Platform ---

	private Transform _currentPlatform;
	private Vector3 _platformLastPos;
	private bool _platformJustSet;
	private Vector3 _platformVelocity;

	private Vector3 _lastPlatformVelocity;
	private float _lastPlatformContactTime;
	private bool _withinCoyoteFromPlatform => (Time.time - _lastPlatformContactTime) <= CoyoteTime;


	private bool _inheritedOnLeavePlatform;
	[SerializeField] private bool _continuousVelocityCarry = false;

	[SerializeField] private bool ExtendedAirbornePlatformCarry = true; // master toggle
	[SerializeField] private bool LockOffsetInsteadOfDelta = false;     // optional alt mode

	private Transform _airCarryPlatform;          // platform we keep following while airborne
	private Vector3 _airCarryPlatformLastPos;     // last sampled position for delta mode
	private Vector3 _airCarryOffset;              // stored offset for lock-offset mode
	private bool _carryingPlatformInAir;          // active airborne follow

	// Frozen platform velocity after leaving ground (horizontal only)
	private Vector3 _frozenPlatformVelocity;
	private bool _hasFrozenPlatformVelocity;

	// Player-only smoothed horizontal velocity (rename from _currentHorizVelocity for clarity)
	private Vector3 _playerHorizVelocity;

	// Frozen horizontal platform velocity captured when leaving ground or jumping off a platform
	private Vector3 _frozenCarryVelocity;
	private bool _hasFrozenCarry;







	#endregion

	#region --- Unity Lifecycle ---

	private void Awake()
	{
		_input = GetComponent<ControlObserver>();
		_rb = GetComponent<Rigidbody>();
		_capsule = GetComponent<CapsuleCollider>();

		_capsuleTargetHeight = StandingHeight;
		GroundProbeRadius = Mathf.Min(GroundProbeRadius, _capsule.radius * 0.95f);

		// Prevent tipping
		_rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

		if (!CameraTransform && Camera.main) CameraTransform = Camera.main.transform;
	}

	private void Update()
	{
		UpdateTargetHorizontalVelocity();
		if (EnableCrouch) UpdateCrouchInput();

		if (_input.ConsumeJump())
			_lastJumpPressedAt = Time.time;
	}

	private void FixedUpdate()
	{
		CheckGround();
		ApplyPlatformDelta();
		ApplyAirbornePlatformCarry();
		MaybeInheritOnPlatformLeave();
		ApplyHorizontalAcceleration();   // build baseline horizontal first
		TryProcessJump();                // now modify velocity for jump (incl. platform)
		if (!_isGrounded && ExtraAirGravity > 0f)
			_rb.AddForce(Vector3.down * ExtraAirGravity, ForceMode.Acceleration);
	}


	private void LateUpdate()
	{
		if (EnableCrouch)
			UpdateCrouchHeights();
	}

	#endregion

	#region --- Input & Horizontal Movement ---

	private void UpdateTargetHorizontalVelocity()
	{
		Vector2 move = _input.MoveDirection;
		float baseSpeed = MaxWalkSpeed;

		bool movingForward = move.y > 0.01f;
		_sprinting = _input.SprintHeld && movingForward && !_isCrouched;

		if (_sprinting) baseSpeed *= SprintMultiplier;
		if (_isCrouched) baseSpeed *= CrouchSpeedMultiplier;

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

		Vector3 wishDir = (right * move.x + forward * move.y);
		if (wishDir.sqrMagnitude > 1f) wishDir.Normalize();
		_targetHorizVelocity = wishDir * baseSpeed;
	}

	private void ApplyHorizontalAcceleration()
	{
		const float eps = 0.0001f;
		float dt = Time.fixedDeltaTime;

		if (ZeroVelocityOnHardReverse &&
	_targetHorizVelocity != Vector3.zero &&
	_playerHorizVelocity != Vector3.zero)
		{
			float dot = Vector3.Dot(_playerHorizVelocity.normalized, _targetHorizVelocity.normalized);
			if (dot < ReverseDotThreshold)
				_playerHorizVelocity = Vector3.zero;
		}

		bool speedingUp = _targetHorizVelocity.sqrMagnitude >
						  _currentHorizVelocity.sqrMagnitude + eps;
		float tau = speedingUp ? AccelTau : DecelTau;
		float alpha = (tau <= 0f) ? 1f : 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, tau));

		_playerHorizVelocity += (_targetHorizVelocity - _playerHorizVelocity) * alpha;

		// Compose final horizontal = player + (frozen carry if airborne)
		Vector3 finalHoriz = _playerHorizVelocity;
		if (!_isGrounded && _hasFrozenCarry)
			finalHoriz += _frozenCarryVelocity;

		// Apply preserving vertical
		Vector3 v = _rb.linearVelocity;
		v.x = finalHoriz.x;
		v.z = finalHoriz.z;
		_rb.linearVelocity = v;

	}

	#endregion

	#region --- Ground & Platform ---

	private void CheckGround()
	{
		_hadGroundLastFrame = _isGrounded;

		Vector3 origin =
			transform.position + Vector3.up * (GroundProbeOriginOffset + GroundProbeRadius);

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
			_hasFrozenPlatformVelocity = false;
			_frozenPlatformVelocity = Vector3.zero;
			_lastGroundHit = rh;
			_isGrounded = true;
			_lastGroundedAt = Time.time;
			_inheritedOnLeavePlatform = false;
			_hasFrozenCarry = false;
			_frozenCarryVelocity = Vector3.zero;

			Transform platform = rh.collider.transform;
			if (platform != _currentPlatform)
			{
				_currentPlatform = platform;
				_platformLastPos = _currentPlatform.position;
				_platformJustSet = true;
			}

			// We have (re)landed -> stop airborne carrying
			_carryingPlatformInAir = false;

			// Normal platform setup:
			if (platform != _currentPlatform)
			{
				_currentPlatform = platform;
				_platformLastPos = _currentPlatform.position;
				_platformJustSet = true;
			}

			// Always refresh last-platform references when grounded on something:
			_airCarryPlatform = _currentPlatform;
			_airCarryPlatformLastPos = (_airCarryPlatform ? _airCarryPlatform.position : Vector3.zero);


			if (!_hadGroundLastFrame)
				_airJumpsUsed = 0;
		}
		else
		{
			if (_hadGroundLastFrame)
			{
				// If we walked / fell off (not via PerformJump before leaving), snapshot last platform vel:
				if (_lastPlatformVelocity.sqrMagnitude > 0.000001f)
				{
					_frozenCarryVelocity = new Vector3(_lastPlatformVelocity.x, 0f, _lastPlatformVelocity.z);
					_hasFrozenCarry = true;
				}
			}
			_isGrounded = false;
			_currentPlatform = null;
		}

	}

	private void ApplyPlatformDelta()
	{
		if (!_isGrounded || _currentPlatform == null)
		{
			// Airborne: supply frozen snapshot if we have one; otherwise zero.
			if (_hasFrozenPlatformVelocity)
				_platformVelocity = _frozenPlatformVelocity;
			else
				_platformVelocity = Vector3.zero;
			return;
		}

		// --- Grounded on a platform ---
		Vector3 currentPos = _currentPlatform.position;

		if (_platformJustSet)
		{
			_platformLastPos = currentPos;
			_platformVelocity = Vector3.zero;
			_platformJustSet = false;

			_lastPlatformContactTime = Time.time;
			_lastPlatformVelocity = _platformVelocity;
			return;
		}

		Vector3 delta = currentPos - _platformLastPos;
		float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;
		_platformVelocity = delta / dt;
		_platformVelocity.y = 0f;

		if (delta.sqrMagnitude > 0f)
		{
			_rb.MovePosition(_rb.position + new Vector3(delta.x, 0f, delta.z));
		}

		_lastPlatformVelocity = _platformVelocity;
		_lastPlatformContactTime = Time.time;
		_platformLastPos = currentPos;
	}


	/// <summary>
	/// While airborne, continue inheriting motion from the last platform so the player
	/// stays in that frame until landing.
	/// </summary>
	private void ApplyAirbornePlatformCarry()
	{
		if (!_carryingPlatformInAir || !_airCarryPlatform)
			return;

		// If platform got destroyed or disabled:
		if (!_airCarryPlatform.gameObject.activeInHierarchy)
		{
			_carryingPlatformInAir = false;
			return;
		}

		Vector3 currentPos = _airCarryPlatform.position;
		float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;

		if (LockOffsetInsteadOfDelta)
		{
			// Maintain fixed offset (horizontal only if desired)
			Vector3 desired = _airCarryPlatform.position + _airCarryOffset;
			Vector3 move = desired - transform.position;
			move.y = 0f; // ignore vertical (remove if you want vertical tracking)
			if (move.sqrMagnitude > 0f)
				_rb.MovePosition(_rb.position + move);

			// Update a synthetic "platform velocity"
			_lastPlatformVelocity = move / dt;
			_platformVelocity = _lastPlatformVelocity;
		}
		else
		{
			// Delta-carry mode
			Vector3 delta = currentPos - _airCarryPlatformLastPos;
			delta.y = 0f;  // ignore vertical (change if needed)
			if (delta.sqrMagnitude > 0f)
			{
				_rb.MovePosition(_rb.position + delta);
				// Update velocities so mid-air jumps still inherit *current* motion.
				_platformVelocity = delta / dt;
				_lastPlatformVelocity = _platformVelocity;
			}
		}

		_airCarryPlatformLastPos = currentPos;
	}


	/// <summary>
	/// If we *just* left a moving platform (walked / fell off, not a jump that already inherited),
	/// inject that last horizontal platform velocity once so motion is continuous.
	/// </summary>
	private void MaybeInheritOnPlatformLeave()
	{
		// We were grounded last frame, now airborne
		if (_hadGroundLastFrame && !_isGrounded &&
			!_inheritedOnLeavePlatform &&
			_lastPlatformVelocity.sqrMagnitude > 0.00001f)
		{
			Vector3 carry = new Vector3(_lastPlatformVelocity.x, 0f, _lastPlatformVelocity.z);

			// Add to smoothing baseline so acceleration logic starts from carried frame.
			_currentHorizVelocity += carry;

			// Add to actual rigidbody velocity.
			Vector3 v = _rb.linearVelocity;
			v.x += carry.x;
			v.z += carry.z;
			_rb.linearVelocity = v;

			_inheritedOnLeavePlatform = true;
		}
	}


	#endregion

	#region --- Jump ---

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
		if (_isCrouched && HasHeadClearance())
			_capsuleTargetHeight = StandingHeight;

		float jumpVelY = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * JumpHeight);

		Vector3 v = _rb.linearVelocity;

		bool hasPlatformContext = _currentPlatform != null || _hasFrozenPlatformVelocity;
		bool shouldInheritPlatform =
			(InheritMode != PlatformInheritMode.None) &&
			hasPlatformContext &&
			groundOrCoyote;


		if (shouldInheritPlatform)
		{
			Vector3 carry = new Vector3(_platformVelocity.x, 0f, _platformVelocity.z);
			_inheritedOnLeavePlatform = true;
			// Use whatever _platformVelocity currently is (ground frame or coyote reuse)
			_frozenCarryVelocity = carry;
			_hasFrozenCarry = carry.sqrMagnitude > 0.000001f;
		}

		if (v.y < 0f) v.y = 0f;
		v.y = jumpVelY;
		_rb.linearVelocity = v;

		if (!groundOrCoyote)
			_airJumpsUsed++;
	}




	#endregion

	#region --- Crouch ---

	private void UpdateCrouchInput()
	{
		bool crouchHeld = _input.CrouchHeld;

		if (crouchHeld)
		{
			_capsuleTargetHeight = CrouchHeight;
		}
		else
		{
			_capsuleTargetHeight = HasHeadClearance()
				? StandingHeight
				: CrouchHeight;
		}
	}

	private void UpdateCrouchHeights()
	{
		float currentHeight = _capsule.height;
		if (!Mathf.Approximately(currentHeight, _capsuleTargetHeight))
		{
			float newHeight = Mathf.MoveTowards(
				currentHeight,
				_capsuleTargetHeight,
				CrouchTransitionSpeed * Time.deltaTime);

			_capsule.height = newHeight;

			// Recenter so feet stay planted.
			float heightDelta = StandingHeight - _capsule.height;
			_capsule.center = new Vector3(0f, -heightDelta * 0.5f, 0f);
		}

		_isCrouched = _capsule.height < (StandingHeight - 0.01f);

		// Camera / eye height
		if (CameraPivot)
		{
			float targetEye = (_capsuleTargetHeight == CrouchHeight)
				? CrouchedEyeHeight
				: StandingEyeHeight;

			Vector3 lp = CameraPivot.localPosition;
			lp.y = Mathf.MoveTowards(lp.y, targetEye,
				CrouchTransitionSpeed * Time.deltaTime);
			CameraPivot.localPosition = lp;
		}
	}

	private bool HasHeadClearance()
	{
		float radius = _capsule.radius * 0.95f;
		Vector3 point1 = transform.position + Vector3.up * radius;
		Vector3 point2 = transform.position + Vector3.up * (StandingHeight - radius);
		return !Physics.CheckCapsule(
			point1,
			point2,
			radius - 0.01f,
			CeilingMask,
			QueryTriggerInteraction.Ignore);
	}

	#endregion

	#region --- External API ---

	public void SetSprinting(bool enabled) => _sprinting = enabled;

	#endregion

	#region --- Gizmos (Editor Only) ---

#if UNITY_EDITOR
	private void OnDrawGizmosSelected()
	{
		if (_capsule)
		{
			// Current capsule
			Gizmos.color = Color.cyan;
			float r = _capsule.radius;
			Vector3 bottom = transform.position + Vector3.up * r;
			Vector3 top = transform.position + Vector3.up * (_capsule.height - r);
			Gizmos.DrawWireSphere(bottom, r);
			Gizmos.DrawWireSphere(top, r);

			// Clearance preview for standing from crouch
			if (EnableCrouch && _capsuleTargetHeight == StandingHeight && _capsule.height < StandingHeight)
			{
				bool clear = HasHeadClearance();
				Gizmos.color = clear ? Color.green : Color.red;
				float standR = r;
				Vector3 sBottom = transform.position + Vector3.up * standR;
				Vector3 sTop = transform.position + Vector3.up * (StandingHeight - standR);
				Gizmos.DrawWireSphere(sTop, standR * 0.9f);
				Gizmos.DrawLine(sBottom, sTop);
			}
		}

		// Desired vs actual velocity lines
		Gizmos.color = Color.yellow;
		Gizmos.DrawLine(transform.position, transform.position + _targetHorizVelocity);
		Gizmos.color = Color.green;
		Gizmos.DrawLine(transform.position, transform.position + _currentHorizVelocity);

		DrawGroundProbeGizmos();
	}

	private void OnDrawGizmos()
	{
		DrawGroundProbeGizmos();
	}

	private void DrawGroundProbeGizmos()
	{
		Gizmos.color = _jumpPossibleThisFrame ? Color.green : Color.red;
		Vector3 origin = transform.position + Vector3.up * (GroundProbeOriginOffset + GroundProbeRadius);
		Vector3 end = origin + Vector3.down * GroundProbeDistance;
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

	#endregion
}
