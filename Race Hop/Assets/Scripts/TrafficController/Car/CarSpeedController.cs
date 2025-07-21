using UnityEditor;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Car))]
public class CarSpeedController : MonoBehaviour
{
	[Header("Forward Motion")]
	public float maxForwardSpeed = 10f;
	public float forwardAcceleration = 5f;   // base accel
	public float brakingMultiplier = 8f;     // how much stronger braking adjust is

	[Header("Backward Motion / Reaction")]
	public float backwardSpeed = 5f;
	public float backwardBoostMultiplier = 1.5f;        // keep original behavior comment
	public float backwardBoostLerpSpeed = 4f;
	public float backwardRecoverLerpSpeed = 2f;

	[Header("Debug")]
	public bool gizmoShowSpeedLabel = false;

	public float CurrentSpeed { get; private set; }

	private Car car;

	void Awake()
	{
		car = GetComponent<Car>();
		if (!car.moveForward)
			CurrentSpeed = backwardSpeed;
	}

	public void HandleSpeed(Car.CarScanResult scan)
	{
		if (car.moveForward)
			UpdateSpeedForward(scan);
		else
			UpdateSpeedBackward(scan);
	}

	private void UpdateSpeedForward(Car.CarScanResult scan)
	{
		float target = maxForwardSpeed;
		
		if (scan.HasCarAhead)
			target = ComputeForwardTargetSpeed(scan);

		bool braking = target < CurrentSpeed;
		float accelMag = braking ? forwardAcceleration * brakingMultiplier
								 : forwardAcceleration;

		CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, target, accelMag * Time.deltaTime);

		if (scan.HasCarAhead)
			EnforceForwardGap(scan);
	}

	public float GetCurrentSpeed()
	{
		return car.moveForward ? CurrentSpeed : CurrentSpeed * -1;
	}

	private float ComputeForwardTargetSpeed(Car.CarScanResult scan)
	{
		float decelZone = car.checkAheadDistance;
		float minZone = decelZone * 0.4f;

		float dist = scan.distanceAhead;
		float aheadSpd = scan.carAhead.GetComponent<CarSpeedController>()?.GetCurrentSpeed() ?? 0f;
		float desiredMinSpeed = aheadSpd - 1f;
		float target = maxForwardSpeed;

		if (dist < decelZone)
		{
			float blend = (dist <= minZone)
				? 1f
				: (decelZone - dist) / (decelZone - minZone);
			blend = Mathf.Clamp01(blend);
			target = Mathf.Lerp(maxForwardSpeed, desiredMinSpeed, blend);
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
		float baseSpeed = backwardSpeed;
		float target = baseSpeed;

		if (scan.HasCarBehind)
		{
			float boosted = baseSpeed - (baseSpeed * (backwardBoostMultiplier - 1));
			float proximityT = 1f - Mathf.Clamp01(scan.distanceBehind / car.checkAheadDistance);
			target = Mathf.Lerp(baseSpeed, boosted, proximityT);

			CurrentSpeed = Mathf.Lerp(CurrentSpeed, target,
				backwardBoostLerpSpeed * Time.deltaTime);
		}
		else
		{
			CurrentSpeed = Mathf.Lerp(CurrentSpeed, target,
				backwardRecoverLerpSpeed * Time.deltaTime);
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
