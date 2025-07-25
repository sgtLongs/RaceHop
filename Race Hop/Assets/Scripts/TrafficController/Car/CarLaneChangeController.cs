using UnityEngine;
using System.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[RequireComponent(typeof(Car))]
[RequireComponent(typeof(Rigidbody))]
public class CarLaneChangeController : MonoBehaviour
{
	[Header("Lane Change Tuning")]
	[Tooltip("Higher = faster lateral settle. Roughly 'per second' responsiveness.")]
	[Range(0.1f, 8f)] public float laneChangeSpeed = 3f; // controller 'speed' (1/s)
	[Tooltip("Limits sideways acceleration so you don't flip or slide too hard (m/s^2).")]
	[Range(1f, 100f)] public float maxLateralAccel = 25f;

	[Header("Courtesy Yield")]
	public float courtesyCooldown = 2f;
	public bool showCourtesyZone = true;
	public float lastCourtesyTime;

	[Header("Yaw Alignment")]
	[Tooltip("Rotate the car to face along the lane using torque.")]
	public bool alignYawWithLane = true;
	[Tooltip("Yaw alignment speed (per second).")]
	[Range(0.1f, 10f)] public float yawAlignSpeed = 4f;

	private Car car;
	private CarSpeedController speedController;
	private Rigidbody rb;

	// Lane change state
	private bool isChanging;
	private Lane targetLane;

	// Cached each physics step while changing
	private Vector3 laneDir;        // normalized along-lane direction
	private Vector3 laneStart;      // targetLane.startPosition
	private Vector3 laneEnd;        // targetLane.endPosition

	void Awake()
	{
		car = GetComponent<Car>();
		speedController = GetComponent<CarSpeedController>();
		rb = GetComponent<Rigidbody>();

		// Recommended rigidbody settings for stable lateral control:
		// rb.interpolation = RigidbodyInterpolation.Interpolate;
		// rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
	}

	public void HandleLaneChange(Car.CarScanResult scan)
	{
		if (isChanging || car.currentLane == null || car.TrafficHandler == null) return;

		// 1) stuck behind a car -> try to switch
		if (car.moveForward && scan.HasCarAhead && scan.distanceAhead < car.checkAheadDistance)
			TrySwitchLane();

		// 2) courtesy yield
		if (scan.HasCarBehind &&
			scan.distanceBehind <= car.rearCheckDistance &&
			Time.time - lastCourtesyTime >= courtesyCooldown)
		{
			if (car.TrafficHandler.FindSwitchableLane(scan.carBehind) == null && TrySwitchLane())
				lastCourtesyTime = Time.time;
		}
	}

	bool TrySwitchLane()
	{
		Lane lane = car.TrafficHandler.SwitchCarLane(car);
		if (lane == null) return false;

		StartLaneChange(lane);
		return true;
	}

	private void StartLaneChange(Lane newLane)
	{
		// Immediately move car's registration so scanning uses the new lane
		Lane oldLane = car.currentLane;
		oldLane.UnsubscribeCar(car);
		newLane.SubscribeCar(car);
		car.currentLane = newLane;

		targetLane = newLane;
		laneStart = targetLane.startPosition.position;
		laneEnd = targetLane.endPosition.position;
		laneDir = (laneEnd - laneStart).normalized;

		isChanging = true;
	}

	void FixedUpdate()
	{
		if (!isChanging || targetLane == null) return;

		// Recompute the lane line each frame in case endpoints move
		laneStart = targetLane.startPosition.position;
		laneEnd = targetLane.endPosition.position;
		laneDir = (laneEnd - laneStart).normalized;

		// --- Compute lateral offset (position error) ---
		// Distance along lane where the car currently is:
		float distAlong = Vector3.Dot(rb.position - laneStart, laneDir);
		// Lane center at same progress:
		Vector3 laneCenter = laneStart + laneDir * distAlong;

		// Lateral error is the vector from lane center to the car, with the along-lane part removed.
		// (Numerically, this is already perpendicular to laneDir, but we ensure it.)
		Vector3 posError = rb.position - laneCenter;
		Vector3 posErrorAlong = Vector3.Project(posError, laneDir);
		Vector3 posErrorLat = posError - posErrorAlong; // pure lateral error

		// --- Lateral velocity (for damping) ---
		Vector3 velAlong = Vector3.Project(rb.linearVelocity, laneDir);
		Vector3 velLat = rb.linearVelocity - velAlong;

		// --- PD Controller (critically damped) ---
		// Choose gains from laneChangeSpeed 's':
		// For a 2nd-order crit-damped system: x'' + 2*s*x' + s^2*x = 0
		float s = Mathf.Max(0.1f, laneChangeSpeed);
		float kp = s * s;
		float kd = 2f * s;

		// Desired lateral acceleration:
		Vector3 desiredLatAccel = (-kp * posErrorLat) - (kd * velLat);

		// Clamp to avoid unrealistic side force
		if (desiredLatAccel.sqrMagnitude > maxLateralAccel * maxLateralAccel)
			desiredLatAccel = desiredLatAccel.normalized * maxLateralAccel;

		// Apply as acceleration so it’s mass‑independent
		rb.AddForce(desiredLatAccel, ForceMode.Acceleration);

		// --- Optional: align yaw to lane using torque (Y axis only) ---
		if (alignYawWithLane)
		{
			Vector3 forward = transform.forward;
			Vector3 flatDir = new Vector3(laneDir.x, 0f, laneDir.z).normalized;
			Vector3 flatFwd = new Vector3(forward.x, 0f, forward.z).normalized;

			// Signed angle around Y
			float angle = Vector3.SignedAngle(flatFwd, flatDir, Vector3.up);
			// PD in angular domain (critically damped around yaw):
			float sy = Mathf.Max(0.1f, yawAlignSpeed);
			float kpy = sy * sy;
			float kdy = 2f * sy;

			// Approx angular velocity around Y (in degrees/sec -> convert to radians if you prefer)
			float angVelY = rb.angularVelocity.y * Mathf.Rad2Deg;
			float desiredAngAccY = (-kpy * angle) - (kdy * angVelY);

			// Convert to radians/sec^2 for torque Acceleration mode
			float desiredAngAccYRad = desiredAngAccY * Mathf.Deg2Rad;
			Vector3 torque = new Vector3(0f, desiredAngAccYRad, 0f);
			rb.AddTorque(torque, ForceMode.Acceleration);
		}

		// --- Completion criterion: near center and not sliding sideways ---
		const float posEps = 0.05f;  // metres
		const float velEps = 0.05f;  // m/s
		if (posErrorLat.magnitude <= posEps && velLat.magnitude <= velEps)
		{
			isChanging = false;
			targetLane = null;
		}
	}

	#region Gizmos
	public void DrawLaneChangeGizmos()
	{
		if (!showCourtesyZone) return;

		var scan = car.LatestScan;
		bool hasRear = scan.HasCarBehind && scan.distanceBehind <= car.rearCheckDistance;
		bool cooldownReady = Time.time - lastCourtesyTime >= courtesyCooldown;

		Color zoneColor;
		bool rearCanSelfSwitch = false;
		if (hasRear)
			rearCanSelfSwitch = car.TrafficHandler != null &&
								car.TrafficHandler.FindSwitchableLane(scan.carBehind) != null;

		if (!hasRear)
			zoneColor = new Color(0f, 1f, 1f, 0.15f);
		else if (rearCanSelfSwitch)
			zoneColor = new Color(0f, 0.9f, 0.2f, 0.25f);
		else
			zoneColor = cooldownReady
				? new Color(1f, 0.55f, 0f, 0.35f)
				: new Color(0.5f, 0.5f, 0.5f, 0.30f);

		DrawCourtesyZone(car.rearCheckDistance, 2.5f, -transform.forward, zoneColor);

		if (isChanging && car.gizmoShowLaneChangeTarget)
		{
			Gizmos.color = Color.white;
			Gizmos.DrawWireSphere(transform.position + transform.forward * 0.5f, 0.3f);
		}

		if (hasRear && car.gizmoShowAheadBehindLinks)
		{
			Gizmos.color = zoneColor;
			Gizmos.DrawLine(transform.position, scan.carBehind.transform.position);
		}
	}

	private void DrawCourtesyZone(float distance, float width, Vector3 direction, Color color)
	{
#if UNITY_EDITOR
		Vector3 dir = direction.normalized;
		Vector3 right = transform.right;
		Vector3 center = transform.position + dir * (distance * 0.5f);
		float halfW = width * 0.5f;
		float halfL = distance * 0.5f;
		Vector3 v0 = center + right * halfW + dir * halfL;
		Vector3 v1 = center - right * halfW + dir * halfL;
		Vector3 v2 = center - right * halfW - dir * halfL;
		Vector3 v3 = center + right * halfW - dir * halfL;

		Color face = new Color(color.r, color.g, color.b, color.a * 0.25f);
		Color outline = new Color(color.r, color.g, color.b, Mathf.Clamp01(color.a * 0.9f));
		Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
		Handles.DrawSolidRectangleWithOutline(new Vector3[] { v0, v1, v2, v3 }, face, outline);
#else
		Vector3 center = transform.position + direction.normalized * (distance * 0.5f);
		Vector3 size   = new Vector3(width, 0.05f, distance);
		Color prev = Gizmos.color;
		Gizmos.color = color;
		Gizmos.DrawWireCube(center, size);
		Gizmos.color = prev;
#endif
	}
	#endregion
}
