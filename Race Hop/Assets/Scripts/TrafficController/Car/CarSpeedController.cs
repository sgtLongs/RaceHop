using UnityEngine;
using System.Collections.Generic;
using static Car;
using System;
using System.Linq;




#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[RequireComponent(typeof(Car))]
[RequireComponent(typeof(Rigidbody))]
public class CarSpeedController : MonoBehaviour
{
	#region Inspector


	[Tooltip("Extra multiplier per car behind relative to motion (e.g., 0.10 = +10% per car).")]
	[Range(0f, 1f)] public float CountBoostPerCar = 0.05f;
	[Tooltip("Cap for total count-based multiplier (1 = none).")]
	public float MaxCountBoostMultiplier = 2.0f;

	public float RearObstructionBoostDistance = 40;

	[Header("Obstructing")]
	[Tooltip("Distance at which we must fully match the behind car's speed (m).")]
	public float ObstructMinGap = 7f;

	[Header("Following")]
	[Tooltip("Distance at which we must fully match the ahead car's speed (m).")]
	public float FollowMinGap = 6f;

	[Tooltip("If true, the decel zone is the car's checkAheadDistance; otherwise use this value.")]
	public bool UseCarAheadDistanceAsDecelZone = true;
	[Tooltip("Manual decel zone length if not using the Car's checkAheadDistance (m).")]
	public float ManualDecelZone = 15f;

	[Header("Response (m/s^2)")]
	[Tooltip("Max acceleration when speeding up.")]
	public float Accel = 6f;
	[Tooltip("Max deceleration (positive number).")]
	public float BrakeAccel = 10f;

	[Header("Smoothing (time constants)")]
	[Tooltip("Seconds to close ~63% of speed error while accelerating.")]
	public float AccelTau = 0.25f;
	[Tooltip("Seconds to close ~63% of speed error while braking.")]
	public float DecelTau = 0.20f;

	[Header("Debug")]
	public bool ShowDebugArrows = false;
	#endregion

	public float CurrentSpeed { get; private set; }   // along commanded direction

	private Car car;
	private Rigidbody rb;

	private Vector3 _pendingAccel = Vector3.zero;
	private Vector3 _motionDir = Vector3.forward;
	private float _targetSpeed = 0f;

	void Awake()
	{
		car = GetComponent<Car>();
		rb = GetComponent<Rigidbody>();

	}

	void FixedUpdate()
	{
		if (_pendingAccel.sqrMagnitude > 0f)
			rb.AddForce(_pendingAccel, ForceMode.Acceleration);

		CurrentSpeed = Vector3.Dot(GetVelocityXZ(), _motionDir);
	}

	public void HandleSpeed(CarScanResult rawScan)
	{
		_motionDir = ComputeMotionDirection();

		Vector3 laneFwd = GetLaneForward();

		bool sameDirAsLane = Vector3.Dot(_motionDir, laneFwd) >= 0f;

		List<CarScan> behindCarsRel = sameDirAsLane ? rawScan.behindCars : rawScan.aheadCars;


		_targetSpeed = car.BaseSpeed;

		float proximityModifier = 1f;

	
		float decelZone = ManualDecelZone;

		if (rawScan.HasCarAhead)
		{
			CarScan carAheadScan = rawScan.aheadCars[0];

			_targetSpeed = CalculateSpeedWithObstructingCar(carAheadScan);
		}

		/*if (IsObstructingCarsBehind(behindCarsRel))
		{
			_targetSpeed = CalculateProximitySpeed(behindCarsRel);
			_targetSpeed *= CalculateObtructionModifier(behindCarsRel);
		}*/

		_pendingAccel = CalculatePendingAcceleration(_targetSpeed);

		_pendingAccel = ApplyLateralDamping();
	}

	private bool IsObstructingCarsBehind(List<CarScan> behindCarsRel)
	{
		return behindCarsRel != null && behindCarsRel.Count > 0 && car.rearCheckDistance > 0.01f;
	}

	private float CalculateProximitySpeed(List<CarScan> behindCarsRel)
	{
		CarScan scan = behindCarsRel[0];

		float closestBehindGap = Mathf.Max(0f, scan.Distance);

		if (closestBehindGap <= ObstructMinGap)
		{
			return scan.Car.CurrentSpeed + 0.1f;
		}

		float effectiveDistance = closestBehindGap - ObstructMinGap;

		float percentOfRearCheckDistanceClose = 1f - Mathf.Clamp01(effectiveDistance / Mathf.Max(1e-3f, car.rearCheckDistance));

		float proximitySpeed = _targetSpeed;

		if (_targetSpeed < scan.Car.CurrentSpeed)
		{
			proximitySpeed = Mathf.Lerp(_targetSpeed, scan.Car.CurrentSpeed, percentOfRearCheckDistanceClose);
		}

		return proximitySpeed;
	}

	private float CalculateObtructionModifier(List<CarScan> behindCarsRel)
	{
		IEnumerable<CarScan> ObstructedCars = behindCarsRel.Where<CarScan>(x => 
		x.Distance < RearObstructionBoostDistance && 
		car.TrafficHandler.FindSwitchableLane(x.Car));

		float countMul = 1f + CountBoostPerCar * Mathf.Max(0, ObstructedCars.Count());
		//countMul = Mathf.Clamp(countMul, 1f, MaxCountBoostMultiplier);
		return countMul;
	}

	private float CalculateSpeedWithObstructingCar(CarScan carAhead)
	{
		float otherCarSpeed = carAhead.Car.Rigidbody.linearVelocity.z;

		float normalizedDistanceFromTarget = Mathf.Clamp01(1f - Mathf.InverseLerp(FollowMinGap, ManualDecelZone, Mathf.Max(0f, carAhead.Distance)));

		float targetFromAhead = Mathf.Lerp(otherCarSpeed - 0.1f, _targetSpeed, normalizedDistanceFromTarget);

		if (carAhead.Distance < FollowMinGap)
		{
			return otherCarSpeed - 0.1f;
		}

		return targetFromAhead;
	}

	private Vector3 ApplyLateralDamping()
	{
		Vector3 v = GetVelocityXZ();
		Vector3 lateral = v - Vector3.Project(v, _motionDir);
		return _pendingAccel + -lateral * 2.0f;
	}

	private Vector3 CalculatePendingAcceleration(float targetSpeed)
	{
		float current = rb.linearVelocity.z;
		float error = targetSpeed - current;

		bool speedingUp = error > 0f;

		float tau = Mathf.Max(0.001f, speedingUp ? AccelTau : DecelTau);
		float accelLimit = speedingUp ? Accel : BrakeAccel;

		float desiredAccel1D = Mathf.Clamp(error / tau, -BrakeAccel, Accel);
		desiredAccel1D = Mathf.Clamp(desiredAccel1D, -accelLimit, accelLimit);

		

		return desiredAccel1D * _motionDir;
	}

	public void DrawSpeedLabel()
	{
#if UNITY_EDITOR
		if (car != null && car.gizmoShowSpeedLabel)
		{
			Handles.color = Color.white;
			Vector3 pos = transform.position + Vector3.up * 1.8f;
			string text = $"v: {CurrentSpeed:0.0} m/s\n→ {_targetSpeed:0.0}";
			Handles.Label(pos, text);
			if (ShowDebugArrows)
				Handles.ArrowHandleCap(0, transform.position + Vector3.up * 0.2f,
					Quaternion.LookRotation(_motionDir), 1.0f, EventType.Repaint);
		}
#endif
	}

	// ---- helpers ----
	private Vector3 GetVelocityXZ()
	{
		return new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
	}

	private Vector3 ComputeMotionDirection()
	{
		// Along lane; flip if moveForward is false. (If you prefer signed BaseSpeed, say the word.)
		Vector3 dir;

		Vector3 a = car.currentLane.startPosition.position;
		Vector3 b = car.currentLane.endPosition.position;

		dir = b - a; 
		dir.y = 0f;

		if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;

		dir.Normalize();

		return dir;
	}

	private Vector3 GetLaneForward()
	{
		if (car.currentLane != null)
		{
			Vector3 a = car.currentLane.startPosition.position;
			Vector3 b = car.currentLane.endPosition.position;
			Vector3 f = b - a; f.y = 0f;
			if (f.sqrMagnitude < 1e-6f) return Vector3.forward;
			return f.normalized;
		}
		return Vector3.forward;
	}

	private float GetOtherCarSpeed(Vector3 dir, Car other)
	{
		if (other == null) return 0f;

		var otherRigidbody = other.GetComponent<Rigidbody>(); if (otherRigidbody == null) return 0f;

		Vector3 otherVelocity = new Vector3(otherRigidbody.linearVelocity.x, 0f, otherRigidbody.linearVelocity.z);

		return Vector3.Dot(otherVelocity, dir);
	}

	private void OnDrawGizmos()
	{
		DrawDirectionalZone(ObstructMinGap, 0.5f, Vector3.back);
	}

	internal void DrawDirectionalZone(float distance, float width, Vector3 direction)
	{
		float height = 2f;
		Vector3 dirNorm = direction.normalized;
		Vector3 center = transform.position + dirNorm * (distance * 0.5f);
		Vector3 size = new Vector3(width, height, distance);
		Gizmos.DrawWireCube(center, size);
	}
}
