using UnityEngine;
using System.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class Car : MonoBehaviour
{
	#region Inspector (Shared Core)

	[Header("Motion Direction")]
	public bool moveForward = true;

	[Header("Lane Info")]
	public Lane currentLane;
	public float deleteThreshold = 2f;

	[Header("Detection Distances")]
	public float checkAheadDistance = 15f;   // (Used by speed + lane change)
	public float rearCheckDistance = 8f;    // (Used by courtesy yield)

	[Header("Gizmos")]
	public bool gizmoShowForwardZones = true;
	public bool gizmoShowBackwardZones = true;
	public bool gizmoShowAheadBehindLinks = true;
	public bool gizmoShowCourtesyZone = true;
	public bool gizmoShowLaneChangeTarget = true;
	public bool gizmoShowSpeedLabel = false;

	#endregion

	#region Public (exposed to controllers)

	public TrafficHandler TrafficHandler { get; private set; }

	// Speed is *owned* by CarSpeedController, but we expose it:
	public float CurrentSpeed => speedController != null ? speedController.CurrentSpeed : 0f;

	// Latest per-lane scan (produced once per frame for all consumers):
	public CarScanResult LatestScan { get; private set; } = CarScanResult.Empty;

	#endregion

	#region Private

	private CarSpeedController speedController;
	private CarLaneChangeController laneChangeController;

	#endregion

	#region Struct (shared)

	public struct CarScanResult
	{
		public Car carAhead;
		public float distanceAhead;

		public Car carBehind;
		public float distanceBehind;

		public bool HasCarAhead => carAhead != null;
		public bool HasCarBehind => carBehind != null;

		public static CarScanResult Empty => new CarScanResult
		{
			carAhead = null,
			carBehind = null,
			distanceAhead = float.MaxValue,
			distanceBehind = float.MaxValue
		};
	}

	#endregion

	#region Unity

	void Awake()
	{
		TrafficHandler = FindFirstObjectByType<TrafficHandler>();
		speedController = GetComponent<CarSpeedController>();
		laneChangeController = GetComponent<CarLaneChangeController>();

		if (TrafficHandler == null)
		{
			Debug.LogError("[Car] LaneHandler not found.");
			enabled = false;
			return;
		}
		
	}

	void Update()
	{
		// 1. Gather passive perception once.
		LatestScan = ScanEnvironment();

		// 2. Speed logic
		speedController?.HandleSpeed(LatestScan);

		// 3. Translate
		ApplyMovement(speedController?.CurrentSpeed ?? 0f);

		// 4. Lane change decisions (after we know speed)
		laneChangeController?.HandleLaneChange(LatestScan);

		// 5. Lifecycle / cleanup
		CheckForEndOfLane();
	}

	void OnDrawGizmos()
	{
		if (currentLane == null) return;
		DrawCoreGizmos();          // Forward/back zones & link lines
		laneChangeController?.DrawLaneChangeGizmos(); // Courtesy + target sphere
		speedController?.DrawSpeedLabel();            // Optional speed text
	}

	#endregion

	#region Scanning (shared)

	/**
	 * Populates a car scan with the nearest car behind and in front
	 */
	private CarScanResult ScanEnvironment()
	{
		if (currentLane == null) return CarScanResult.Empty;

		CarScanResult scanResult = CarScanResult.Empty;

		Vector3 laneStart = currentLane.startPosition.position;
		Vector3 laneEnd = currentLane.endPosition.position;
		Vector3 dir = (laneEnd - laneStart).normalized;

		float myDist = Vector3.Dot(transform.position - laneStart, dir);

		foreach (var other in currentLane.cars)
		{
			if (other == null || other == this) continue;

			float otherCarsDistanceFromStart = Vector3.Dot(other.transform.position - laneStart, dir);
			float gap = otherCarsDistanceFromStart - myDist;

			if (gap > 0f && gap < scanResult.distanceAhead && gap < checkAheadDistance)
			{
				scanResult.distanceAhead = gap;
				scanResult.carAhead = other;
			}
			if (gap < 0f)
			{
				float behindGap = -gap;
				if (behindGap < scanResult.distanceBehind && behindGap < rearCheckDistance)
				{
					scanResult.distanceBehind = behindGap;
					scanResult.carBehind = other;
				}
			}
		}
		return scanResult;
	}

	#endregion

	#region Movement & Lifecycle

	private void ApplyMovement(float speed)
	{
		Vector3 dir = moveForward ? transform.forward : -transform.forward;
		transform.position += dir * speed * Time.deltaTime;
	}

	private void CheckForEndOfLane()
	{
		if (currentLane == null) return;
		Vector3 targetPoint = moveForward ? currentLane.endPosition.position
										  : currentLane.startPosition.position;

		if (Vector3.Distance(transform.position, targetPoint) < deleteThreshold)
		{
			currentLane.UnsubscribeCar(this);
			Destroy(gameObject);
		}
	}

	public void CompleteLaneChange(Lane newLane)
	{
		currentLane = newLane;
	}

	void OnDestroy()
	{
		currentLane.UnsubscribeCar(this);
	}
	#endregion

	#region Gizmos (core only)

	private void DrawCoreGizmos()
	{
		var scan = LatestScan;

		if (moveForward && gizmoShowForwardZones)
		{
			Gizmos.color = Color.cyan;
			DrawDirectionalZone(checkAheadDistance, 3f, transform.forward);
			Gizmos.color = Color.yellow;
			DrawDirectionalZone(checkAheadDistance * 1.5f, 1.5f, transform.forward);
		}
		else if (!moveForward && gizmoShowBackwardZones)
		{
			Gizmos.color = Color.magenta;
			DrawDirectionalZone(checkAheadDistance, 2f, -transform.forward);
		}

		if (gizmoShowAheadBehindLinks)
		{
			if (scan.HasCarAhead)
			{
				Gizmos.color = Color.red;
				Gizmos.DrawLine(transform.position, scan.carAhead.transform.position);
			}
			if (scan.HasCarBehind)
			{
				Gizmos.color = Color.green;
				Gizmos.DrawLine(transform.position, scan.carBehind.transform.position);
			}
		}
	}

	internal void DrawDirectionalZone(float distance, float width, Vector3 direction)
	{
		float height = 2f;
		Vector3 dirNorm = direction.normalized;
		Vector3 center = transform.position + dirNorm * (distance * 0.5f);
		Vector3 size = new Vector3(width, height, distance);
		Gizmos.DrawWireCube(center, size);
	}

	#endregion
}
