using UnityEngine;

/// <summary>
/// Rigidbody-based character movement: smoothed horizontal acceleration,
/// sprinting, crouching (with head clearance), buffered + coyote jumps,
/// and optional moving-platform delta following.
/// </summary>
[RequireComponent(typeof(ControlObserver))]
[RequireComponent(typeof(Rigidbody))]
public class MovementHandler1 : MonoBehaviour
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

	private ControlObserver _input;
	private Rigidbody _rb;

	// Smoothed horizontal velocity we own (x/z only).
	private Vector3 _playerHorizVelocity;
	private Vector3 _targetHorizVelocity;

	private void Awake()
	{
		_input = GetComponent<ControlObserver>();
		_rb = GetComponent<Rigidbody>();

		// Prevent tipping.
		_rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

		if (!CameraTransform && Camera.main) CameraTransform = Camera.main.transform;
	}

	private void Update()
	{
		UpdateTargetHorizontalVelocity();
	}

	private void FixedUpdate()
	{
		ApplyHorizontalForces();
	}

	private void ApplyHorizontalForces()
	{
		Vector3 v = _rb.linearVelocity;
		Vector3 horiz = new Vector3(v.x, 0f, v.z);

		// If player not providing input, do nothing: keep current horiz velocity (so platform friction works)
		if (_targetHorizVelocity.sqrMagnitude < 0.0001f)
			return;

		// Clamp desired to max speed (already done when building _targetHorizVelocity, but safe)
		Vector3 desired = _targetHorizVelocity;

		// Accel toward desired using time-constant form
		bool speedingUp = desired.sqrMagnitude > horiz.sqrMagnitude + 0.0001f;
		float tau = speedingUp ? AccelTau : DecelTau;
		if (tau <= 0f) tau = 0.0001f;

		// Optional: if you still want hard reverse, you can keep it, but it will fight platforms
		// (Recommend disabling)
		// if (ZeroVelocityOnHardReverse && horiz != Vector3.zero && desired != Vector3.zero) {
		//     float dot = Vector3.Dot(horiz.normalized, desired.normalized);
		//     if (dot < ReverseDotThreshold) horiz = Vector3.zero;
		// }

		Vector3 neededAccel = (desired - horiz) / tau;
		_rb.AddForce(new Vector3(neededAccel.x, 0f, neededAccel.z), ForceMode.Acceleration);
	}



	private void UpdateTargetHorizontalVelocity()
	{
		Vector2 move = _input.MoveDirection;

		float baseSpeed = MaxWalkSpeed;
		bool sprinting = _input.SprintHeld && move.y > 0.01f;
		if (sprinting) baseSpeed *= SprintMultiplier;

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


#if UNITY_EDITOR
	private void OnDrawGizmosSelected()
	{
		Gizmos.color = Color.yellow;
		Gizmos.DrawLine(transform.position, transform.position + _targetHorizVelocity);
		Gizmos.color = Color.green;
		Gizmos.DrawLine(transform.position, transform.position + _playerHorizVelocity);
	}
#endif
}
