using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[RequireComponent(typeof(Car))]
[RequireComponent(typeof(Rigidbody))]
public class CarSpeedController : MonoBehaviour
{
	#region Inspector
	public float MaxSpeed = 10f;
	public float forwardAcceleration = 5f;
	public float brakingMultiplier = 8f;
	public float backwardBoostMultiplier = 1.5f;
	public float backwardBoostLerpSpeed = 4f;
	public float backwardRecoverLerpSpeed = 2f;
	public bool gizmoShowSpeedLabel = false;
	#endregion

	public float CurrentSpeed { get; private set; }

	private Car car;
	private Rigidbody rb;

	private float BaseSpeed;

	void Awake()
	{
		car = GetComponent<Car>();
		rb = GetComponent<Rigidbody>();

		

		if (!car.moveForward) CurrentSpeed = MaxSpeed;
	}

	/* ─── LOGIC (called from Car.Update) ─── */
	public void HandleSpeed(Car.CarScanResult scan)
	{
		if (car.moveForward) UpdateSpeedForward(scan);
		else UpdateSpeedBackward(scan);
	}

	/* ─── PHYSICS APPLICATION ─── */
	void FixedUpdate()
	{
		MaxSpeed = car.BaseSpeed;

		Vector3 dir = car.moveForward ? transform.forward : -transform.forward;

		Vector3 lateral = rb.linearVelocity - Vector3.Project(rb.linearVelocity, dir);

		rb.linearVelocity = dir * CurrentSpeed + lateral;
	}

	private void UpdateSpeedForward(Car.CarScanResult scan)
	{
		float target = MaxSpeed;

		if (scan.HasCarAhead)
			target = ComputeForwardTargetSpeed(scan);

		bool braking = target < CurrentSpeed;
		float accelMag = braking ? forwardAcceleration * brakingMultiplier : forwardAcceleration;
		CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, target, accelMag * Time.deltaTime);

		if (scan.HasCarAhead)
			EnforceForwardGap(scan);
	}

	public float GetCurrentSpeed() => car.moveForward ? CurrentSpeed : CurrentSpeed * -1f;

	private float ComputeForwardTargetSpeed(Car.CarScanResult scan)
	{
		float decelZone = car.checkAheadDistance;
		float minZone = decelZone * 0.4f;

		float dist = scan.distanceAhead;
		float aheadSpd = scan.carAhead.GetComponent<CarSpeedController>()?.GetCurrentSpeed() ?? 0f;
		float desiredMinSpeed = aheadSpd - 1f;
		float target = MaxSpeed;

		if (dist < decelZone)
		{
			float blend = (dist <= minZone) ? 1f : (decelZone - dist) / (decelZone - minZone);
			blend = Mathf.Clamp01(blend);
			target = Mathf.Lerp(MaxSpeed, desiredMinSpeed, blend);
		}

		if (dist < minZone * 0.5f)
			target = Mathf.Min(target, aheadSpd - 1f);

		return target;
	}

	private void EnforceForwardGap(Car.CarScanResult scan)
	{
		float minGap = 7f;
		float allowedForward = scan.distanceAhead - minGap;
		float intendedMove = CurrentSpeed * Time.deltaTime;

		if (intendedMove > allowedForward)
		{
			float clamped = allowedForward / Mathf.Max(Time.deltaTime, 0.0001f);
			float aheadSpd = scan.carAhead.GetComponent<CarSpeedController>()?.GetCurrentSpeed() ?? 0f;
			CurrentSpeed = Mathf.Max(clamped, aheadSpd - 2f);
		}
	}

	private void UpdateSpeedBackward(Car.CarScanResult scan)
	{
		float baseSpeed = MaxSpeed;                 // cruise speed when reversing
		float target = baseSpeed;

		if (scan.HasCarBehind)
		{
			/* ---------- PARAMETERS ---------- */
			const float minGap = 8f;                              // metres – stop here
			float decelZone = car.checkAheadDistance * 2f;     // start easing twice as far
			float boostCap = baseSpeed * 1.5f;                // max allowed speed up

			/* ---------- DATA FROM CAR BEHIND ---------- */
			float behindSpeed = Mathf.Abs(
				scan.carBehind.GetComponent<CarSpeedController>()?.GetCurrentSpeed() ?? 0f
			);

			/* ---------- PROXIMITY BLEND (0 → far, 1 → at minGap) ---------- */
			float t = Mathf.Clamp01(
				(decelZone - scan.distanceBehind) /
				(decelZone - minGap)
			);

			/* ---------- DESIRED SPEED BASED ON BEHIND CAR ----------
			   Far away → our base speed
			   Close    → match the behind car's speed (could be zero)             */
			float blendedTarget = Mathf.Lerp(baseSpeed, behindSpeed, t);

			// Never exceed 1.5× our base cruise speed.
			blendedTarget = Mathf.Min(blendedTarget, boostCap);

			/* ---------- GAP‑SAFETY CAP ----------
			   Limit so we cannot move farther than the remaining gap this frame. */
			float allowedBackward = Mathf.Max(scan.distanceBehind - minGap, 0f);
			float gapTarget = allowedBackward / Mathf.Max(Time.deltaTime, 0.0001f);

			/* ---------- FINAL TARGET ---------- */
			target = Mathf.Min(blendedTarget, gapTarget);

			/* ---------- SMOOTH APPROACH ---------- */
			CurrentSpeed = Mathf.Lerp(CurrentSpeed, target,
									  backwardBoostLerpSpeed * Time.deltaTime);
		}
		else
		{
			/* ---------- NOTHING BEHIND ---------- */
			CurrentSpeed = Mathf.Lerp(CurrentSpeed, target,
									  backwardRecoverLerpSpeed * Time.deltaTime);
		}
	}





	// NEW – symmetric gap check for static obstacles behind us.
	private void EnforceBackwardGap(Car.CarScanResult scan)
	{
		const float minGap = 10f;

		// How far we are still allowed to move back this frame.
		float allowedBackward = scan.distanceBehind - minGap;
		if (allowedBackward < 0f) allowedBackward = 0f;

		float intendedMove = CurrentSpeed * Time.deltaTime;          // positive magnitude

		if (intendedMove > allowedBackward)
		{
			float clamped = allowedBackward / Mathf.Max(Time.deltaTime, 0.0001f);

			float behindSpd = scan.carBehind
							  .GetComponent<CarSpeedController>()?.GetCurrentSpeed()
							  ?? 0f;                                 // static = 0

			behindSpd = Mathf.Abs(behindSpd);                        // magnitude only

			// Keep at or below both the gap‑safe speed *and* the car‑behind speed‑2.
			CurrentSpeed = Mathf.Min(clamped, Mathf.Max(behindSpd - 2f, 0f));
		}
	}


#if UNITY_EDITOR
	public void DrawSpeedLabel()
	{
		if (!gizmoShowSpeedLabel) return;
		GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
		style.normal.textColor = Color.white;
		var scan = car.LatestScan;
		string label =
			$"spd:{CurrentSpeed:0.0}\n" +
			(scan.HasCarAhead ? $"Ahead:{scan.distanceAhead:0.0}" : "Ahead:-") + "\n" +
			(scan.HasCarBehind ? $"Behind:{scan.distanceBehind:0.0}" : "Behind:-");
		Handles.Label(transform.position + Vector3.up * 2f, label, style);
	}
#else
	public void DrawSpeedLabel() { }
#endif
}
