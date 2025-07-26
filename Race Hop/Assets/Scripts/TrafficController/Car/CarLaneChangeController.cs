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

	[Header("Post‑Change Snap")]
	public bool snapXToLaneOnComplete = true;
	[Tooltip("Only snap when the lateral error is already tiny.")]
	public float snapTolerance = 0.15f;   // metres

	[Header("Lane Keeping (post‑change)")]
	public bool laneKeepingEnabled = true;
	[Tooltip("Lower = gentler lane keeping; higher = snappier. (per second)")]
	[Range(0.1f, 4f)] public float laneKeepSpeed = 1.2f;
	[Tooltip("Max lateral accel during lane keeping (m/s^2).")]
	[Range(1f, 50f)] public float laneKeepMaxAccel = 8f;
	[Tooltip("Ignore tiny errors to avoid jitter (metres).")]
	[Range(0f, 0.2f)] public float laneKeepDeadzone = 0.02f;
	[Tooltip("How far from center until full force kicks in (metres).")]
	[Range(0.05f, 2f)] public float laneKeepScaleRadius = 0.5f;



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
		// Pick which lane geometry to follow
		Lane activeLane = isChanging ? targetLane : car.currentLane;
		if (activeLane == null) return;

		// Recompute lane line each step (in case endpoints move)
		Vector3 start = activeLane.startPosition.position;
		Vector3 end = activeLane.endPosition.position;
		Vector3 dir = (end - start).normalized;

		// --- Lateral error (position) and velocity ---
		// Progress along lane:
		float distAlong = Vector3.Dot(rb.position - start, dir);
		Vector3 laneCenter = start + dir * distAlong;

		// Lateral (perpendicular to dir) error:
		Vector3 toCenter = rb.position - laneCenter;
		Vector3 posErrorLat = toCenter - Vector3.Project(toCenter, dir);

		// Split velocity into along + lateral
		Vector3 velAlong = Vector3.Project(rb.linearVelocity, dir);
		Vector3 velLat = rb.linearVelocity - velAlong;

		// Choose controller gains & limits
		bool usingLaneKeep = (!isChanging && laneKeepingEnabled);
		float s = usingLaneKeep ? Mathf.Max(0.1f, laneKeepSpeed) : Mathf.Max(0.1f, laneChangeSpeed);
		float maxAccel = usingLaneKeep ? laneKeepMaxAccel : maxLateralAccel;

		// Critical damping coefficients
		float kp = s * s;
		float kd = 2f * s;

		// Distance-based scaling (small force near center, larger when far)
		float errMag = posErrorLat.magnitude;
		float dead = usingLaneKeep ? laneKeepDeadzone : 0f;
		float scale = 0f;
		if (errMag > dead)
		{
			// 0 at deadzone edge -> 1 by laneKeepScaleRadius
			float t = (errMag - dead) / Mathf.Max(0.001f, laneKeepScaleRadius);
			scale = Mathf.Clamp01(t);
		}

		// Desired lateral acceleration from PD
		Vector3 desiredLatAccel = (-kp * posErrorLat) - (kd * velLat);
		desiredLatAccel *= usingLaneKeep ? Mathf.Lerp(0.15f, 1f, scale) : 1f; // soften near center when keeping

		// Clamp to prevent unrealistic lateral shove
		if (desiredLatAccel.sqrMagnitude > maxAccel * maxAccel)
			desiredLatAccel = desiredLatAccel.normalized * maxAccel;

		// Apply as acceleration (mass‑independent)
		rb.AddForce(desiredLatAccel, ForceMode.Acceleration);

		// Optional: keep yaw aligned with lane even after change
		if (alignYawWithLane)
		{
			Vector3 flatDir = new Vector3(dir.x, 0f, dir.z).normalized;
			Vector3 flatFwd = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
			float angle = Vector3.SignedAngle(flatFwd, flatDir, Vector3.up);
			float sy = Mathf.Max(0.1f, yawAlignSpeed);
			float kpy = sy * sy, kdy = 2f * sy;
			float angVelY = rb.angularVelocity.y * Mathf.Rad2Deg;
			float desiredAngAccY = (-kpy * angle) - (kdy * angVelY);
			rb.AddTorque(new Vector3(0f, desiredAngAccY * Mathf.Deg2Rad, 0f), ForceMode.Acceleration);
		}

		// Lane-change completion (lane keeping continues afterward)
		if (isChanging)
		{
			const float posEps = 0.05f, velEps = 0.05f;
			if (posErrorLat.magnitude <= posEps && velLat.magnitude <= velEps)
			{
				isChanging = false;
				targetLane = null;
			}
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
