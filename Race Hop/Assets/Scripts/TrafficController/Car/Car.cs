using UnityEngine;
using System.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class Car : MonoBehaviour
{
	#region Inspector (Shared Core)

	[Header("Motion Direction")]
	public bool moveForward = true;

	[Header("Lane Info")]
	public Lane currentLane;
	public float deleteThreshold = 2f;

	[Header("Detection Distances")]
	public float checkAheadDistance = 15f;
	public float rearCheckDistance = 8f;

	[Header("Gizmos")]
	public bool gizmoShowForwardZones = true;
	public bool gizmoShowBackwardZones = true;
	public bool gizmoShowAheadBehindLinks = true;
	public bool gizmoShowCourtesyZone = true;
	public bool gizmoShowLaneChangeTarget = true;
	public bool gizmoShowSpeedLabel = false;

	#endregion

	public TrafficHandler TrafficHandler { get; private set; }
	public float CurrentSpeed => speedController != null ? speedController.CurrentSpeed : 0f;
	public CarScanResult LatestScan { get; private set; } = CarScanResult.Empty;

	private CarSpeedController speedController;
	private CarLaneChangeController laneChangeController;
	private Rigidbody rb;

	#region Struct
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

	void Awake()
	{
		TrafficHandler = FindFirstObjectByType<TrafficHandler>();
		speedController = GetComponent<CarSpeedController>();
		laneChangeController = GetComponent<CarLaneChangeController>();
		rb = GetComponent<Rigidbody>();

		if (TrafficHandler == null) { Debug.LogError("TrafficHandler not found"); enabled = false; }
		rb.constraints = RigidbodyConstraints.FreezeRotationX |
						   RigidbodyConstraints.FreezeRotationZ;
	}

	// Per‑frame “AI” / perception.
	void Update()
	{
		LatestScan = ScanEnvironment();           // 1. perception
		speedController?.HandleSpeed(LatestScan); // 2. speed logic (sets CurrentSpeed)
		laneChangeController?.HandleLaneChange(LatestScan); // 3. lane changes
	}

	void FixedUpdate()
	{
		CheckForEndOfLane();
	}

	void OnDrawGizmos()
	{
		if (currentLane == null) return;
		DrawCoreGizmos();
		laneChangeController?.DrawLaneChangeGizmos();
		speedController?.DrawSpeedLabel();
	}

	#region Scanning
	private CarScanResult ScanEnvironment()
	{
		if (currentLane == null) return CarScanResult.Empty;

		CarScanResult scanResult = CarScanResult.Empty;

		Vector3 laneStart = currentLane.startPosition.position;
		Vector3 laneEnd = currentLane.endPosition.position;
		Vector3 dir = (laneEnd - laneStart).normalized;

		float myDist = Vector3.Dot(rb.position - laneStart, dir);

		foreach (var other in currentLane.cars)
		{
			if (other == null || other == this) continue;

			Rigidbody rigidbody = other.GetComponent<Rigidbody>();

			float otherCarsDistanceFromStart = Vector3.Dot(rigidbody.position - laneStart, dir);
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

	private void CheckForEndOfLane()
	{
		if (currentLane == null) return;
		Vector3 target = moveForward ? currentLane.endPosition.position
									 : currentLane.startPosition.position;
		if (Vector3.Distance(rb.position, target) < deleteThreshold)
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
		if (currentLane != null)
			currentLane.UnsubscribeCar(this);
	}
	#endregion

	#region Gizmos
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
