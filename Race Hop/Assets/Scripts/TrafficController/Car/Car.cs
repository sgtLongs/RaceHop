using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;




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

	[Tooltip("How far we count cars behind for BOOST logic. If <= 0, uses rearCheckDistance.")]
	public float rearCountDistance = 25f;   // tweak to taste

	Transform dangerField;

	#endregion

	public TrafficHandler TrafficHandler { get; private set; }
	public float CurrentSpeed => speedController != null ? speedController.CurrentSpeed : 0f;
	public CarScanResult LatestScan { get; private set; } = CarScanResult.Empty;

	private CarSpeedController speedController;
	private CarLaneChangeController laneChangeController;
	private Rigidbody rb;

	public float BaseSpeed = 5;
	public bool isStatic = false;

	public float voracity = 0.14f;

	private float yPosition;
	private Quaternion rotation;

	public Rigidbody Rigidbody { get { return rb; } private set { } }

	public struct CarScan
	{
		public Car Car { get; private set; }
		public float Distance { get; private set; }

		public CarScan(Car car, float distance)
		{
			Car = car;
			Distance = distance;
		}
	}

	#region Struct
	public struct CarScanResult
	{
		public List<CarScan> aheadCars;

		public List<CarScan> behindCars;

		public bool HasCarAhead => aheadCars != null && aheadCars.Count > 0;
		public bool HasCarBehind => behindCars != null && behindCars.Count > 0;

		public static CarScanResult Empty => new CarScanResult
		{
			aheadCars = new List<CarScan>(),
			behindCars = new List<CarScan>()
		};
	}
	#endregion


	void Awake()
	{
		TrafficHandler = FindFirstObjectByType<TrafficHandler>();
		speedController = GetComponent<CarSpeedController>();
		laneChangeController = GetComponent<CarLaneChangeController>();
		rb = GetComponent<Rigidbody>();
		dangerField = GameObject.FindGameObjectWithTag("DangerField").transform;


		if (TrafficHandler == null) { Debug.LogError("TrafficHandler not found"); enabled = false; }
		rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
	}

	void Update()
	{
			float randValue = Random.value;
			if(randValue > 0.66)
			{
				BaseSpeed -= voracity;
			}

			if (randValue < 0.33)
			{
				BaseSpeed += voracity;
			}

			if(gameObject.transform.position.z < dangerField.position.z)
			{
				BaseSpeed -= voracity + 0.2f;
			}

			LatestScan = ScanEnvironment();
			speedController?.HandleSpeed(LatestScan);
			laneChangeController?.HandleLaneChange(LatestScan);
	}

	void FixedUpdate()
	{
		CheckForEndOfLane();

		rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
		rb.angularVelocity = Vector3.zero;
	}

	void OnDrawGizmosSelected()
	{
		if (currentLane == null) return;
		DrawCoreGizmos();
		laneChangeController?.DrawLaneChangeGizmos();
		speedController?.DrawSpeedLabel();
	}

	private CarScanResult ScanEnvironment()
	{
		if (currentLane == null) return CarScanResult.Empty;

		CarScanResult scan = CarScanResult.Empty;

		List<CarScan> aheadList = new List<CarScan>();
		List<CarScan> behindList = new List<CarScan>();

		Vector3 laneStart = currentLane.startPosition.position;
		Vector3 laneEnd = currentLane.endPosition.position;

		Vector3 dir;
		if (!FindDirection(scan, laneStart, laneEnd, out dir)) return scan;

		PopulateScanLists(aheadList, behindList, dir);
		SortScanListsByDistance(aheadList, behindList);

		scan.aheadCars = aheadList;
		scan.behindCars = behindList;

		return scan;
	}

	private float CalculateDistanceFromStartOfLane(Vector3 dir)
	{
		Vector3 laneStartPosition = new Vector3(currentLane.startPosition.position.x, 0f, currentLane.startPosition.position.z);
		Vector3 carPosition = new Vector3(rb.position.x, 0f, rb.position.z);
		float myDist = Vector3.Dot(carPosition - laneStartPosition, dir);
		return myDist;
	}

	private static void SortScanListsByDistance(List<CarScan> aheadList, List<CarScan> behindList)
	{
		aheadList.Sort((a, b) => a.Distance.CompareTo(b.Distance));
		behindList.Sort((a, b) => a.Distance.CompareTo(b.Distance));
	}

	private void PopulateScanLists(List<CarScan> aheadList, List<CarScan> behindList, Vector3 carDirection)
	{
		Vector3 laneStartPosition = new Vector3(currentLane.startPosition.position.x, 0f, currentLane.startPosition.position.z);

		foreach (Car otherCar in currentLane.cars)
		{
			if (otherCar == null || otherCar == this) continue;

			float gap = CalculateGapBetweenOtherCar(carDirection, otherCar);

			if (gap > 0f)
			{
				if (gap <= checkAheadDistance) aheadList.Add(new CarScan(otherCar, gap));
			}
			else if (gap < 0f)
			{
				float behindGap = -gap;

				if (behindGap <= rearCheckDistance) behindList.Add(new CarScan(otherCar, behindGap));
			}
		}
	}

	private float CalculateGapBetweenOtherCar(Vector3 carDirection, Car otherCar)
	{
		float distanceFromStart = CalculateDistanceFromStartOfLane(carDirection);
		Vector3 laneStartPosition = new Vector3(currentLane.startPosition.position.x, 0f, currentLane.startPosition.position.z);

		Rigidbody otherRigidbody = otherCar.GetComponent<Rigidbody>();
		Vector3 otherPosition = new Vector3(otherRigidbody.position.x, 0f, otherRigidbody.position.z);

		float otherDist = Vector3.Dot(otherPosition - laneStartPosition, carDirection);

		float gap = otherDist - distanceFromStart;

		return gap;
	}

	private static bool FindDirection(CarScanResult scan, Vector3 laneStart, Vector3 laneEnd, out Vector3 dir)
	{
		dir = laneEnd - laneStart;
		dir.y = 0f;
		if (dir.sqrMagnitude < 1e-6f) return false;
		dir.Normalize();
		return true;
	}



	#region Movement & Lifecycle

	private void CheckForEndOfLane()
	{
		if (currentLane == null) return;

		if(rb.position.z < currentLane.startPosition.position.z || rb.position.z > currentLane.endPosition.position.z)
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
				Gizmos.DrawLine(transform.position, scan.aheadCars[0].Car.transform.position);
			}
			if (scan.HasCarBehind)
			{
				Gizmos.color = Color.green;
				Gizmos.DrawLine(transform.position, scan.behindCars[0].Car.transform.position);
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
