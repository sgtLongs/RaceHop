using System.Collections.Generic;
using UnityEngine;

public class LaneHandler : MonoBehaviour
{
	[Header("Highway Settings")]
	public int NumberOfLanes = 3;
	public float LaneSpacing = 3f;
	public float HighwayLength = 100f; // Distance forward for the end of the highway

	[Header("Lane Prefab (Optional)")]
	public GameObject lanePrefab;
	public GameObject carPrefab;

	private List<Lane> lanes = new List<Lane>();

	public bool spawnACar;

	private struct LaneCheckDebug
	{
		public Vector3 center;
		public Vector3 forwardDir;
		public float length;
		public float width;
		public Color color;
	}

	private struct CarHighlightDebug
	{
		public Vector3 position;
		public float radius;
		public Color color;
	}

	private List<LaneCheckDebug> laneDebugRects = new List<LaneCheckDebug>();
	private List<CarHighlightDebug> carHighlights = new List<CarHighlightDebug>();


	void Start()
	{
		CreateLanes();
	}

	void Update()
	{
		if(spawnACar == true)
		{
			SpawnCar();
			spawnACar = false;
		}
	}

	public int GetLaneIndex(Lane lane)
	{
		return lanes.IndexOf(lane);
	}

	public int GetLaneCount()
	{
		return lanes.Count;
	}

	public Lane GetLaneByIndex(int index)
	{
		if (index >= 0 && index < lanes.Count)
			return lanes[index];
		return null;
	}

	/**
	 * PUBLIC METHOD FOR A CAR TO CALL TO SWITCH LANES
	 */

	public Lane SwitchCarLane(Car car)
	{
		if (car == null || car.currentLane == null)
		{
			Debug.LogWarning("Invalid car or current lane for lane switch.");
			return null;
		}

		int currentIndex = GetLaneIndex(car.currentLane);
		if (currentIndex == -1) return null;

		bool preferLeft = ShouldCarSwitchToLeftLane();
		int[] directions = preferLeft ? new int[] { -1, 1 } : new int[] { 1, -1 };

		foreach (int dir in directions)
		{
			int targetIndex = currentIndex + dir;
			if (targetIndex >= 0 && targetIndex < GetLaneCount())
			{
				Lane targetLane = GetLaneByIndex(targetIndex);
				if (targetLane != null && IsLaneClearForCar(car, targetLane))
				{
					// Switch lane subscription
					car.currentLane.UnsubscribeCar(car);
					targetLane.SubscribeCar(car);

					return targetLane; // Safe lane found
				}
			}
		}

		return null; // Neither lane is safe
	}

	private static bool ShouldCarSwitchToLeftLane()
	{
		return Random.value > 0.5f;
	}

	public Car GetCarAhead(Lane lane, Car car, out float distanceAhead)
	{
		distanceAhead = float.MaxValue;
		Car closestCar = null;

		if (lane == null || car == null) return null;

		Vector3 laneStart = lane.startPosition.position;
		Vector3 laneEnd = lane.endPosition.position;
		Vector3 laneDir = (laneEnd - laneStart).normalized;

		float carDistance = Vector3.Dot(car.transform.position - laneStart, laneDir);

		foreach (Car otherCar in lane.cars)
		{
			if (otherCar == null || otherCar == car) continue;

			float otherDistance = Vector3.Dot(otherCar.transform.position - laneStart, laneDir);

			if (otherDistance > carDistance) // Ahead
			{
				float gap = otherDistance - carDistance;
				if (gap < distanceAhead)
				{
					distanceAhead = gap;
					closestCar = otherCar;
				}
			}
		}

		return closestCar;
	}

	public bool IsCarTooClose(Lane lane, Car car, float checkDistance)
	{
		float distanceAhead;
		GetCarAhead(lane, car, out distanceAhead);
		return distanceAhead < checkDistance;
	}

	private bool IsLaneClearForCar(Car car, Lane targetLane)
	{
		float safeDistance = car.checkAheadDistance + 1; // Length of the "safe" zone forward and backward
		laneDebugRects.Clear();
		carHighlights.Clear();

		Vector3 laneStart = car.currentLane.startPosition.position;
		Vector3 laneEnd = car.currentLane.endPosition.position;
		Vector3 laneDir = (laneEnd - laneStart).normalized;
		float laneLength = Vector3.Distance(laneStart, laneEnd);
		float distanceAlongLane = Vector3.Dot(car.transform.position - laneStart, laneDir);
		float progress = Mathf.Clamp01(distanceAlongLane / laneLength);

		Vector3 targetLaneStart = targetLane.startPosition.position;
		Vector3 targetLaneEnd = targetLane.endPosition.position;
		Vector3 equivalentPos = Vector3.Lerp(targetLaneStart, targetLaneEnd, progress);

		// Draw rectangle on target lane
		Vector3 laneForward = (targetLaneEnd - targetLaneStart).normalized;
		Vector3 laneRight = Vector3.Cross(Vector3.up, laneForward).normalized;
		float laneWidth = 3f; // approximate lane width
		laneDebugRects.Add(new LaneCheckDebug
		{
			center = equivalentPos,
			forwardDir = laneForward,
			length = safeDistance * 2f,
			width = laneWidth,
			color = Color.red
		});

		foreach (Car otherCar in targetLane.cars)
		{
			if (otherCar == null || otherCar == car) continue;

			carHighlights.Add(new CarHighlightDebug
			{
				position = otherCar.transform.position,
				radius = 1f,
				color = Color.magenta
			});

			float dist = Vector3.Distance(otherCar.transform.position, equivalentPos);
			if (dist < safeDistance)
			{
				return false; // Unsafe
			}
		}

		return true; // Safe
	}

	/**
	 * SPAWN A CAR
	 */

	public void SpawnCar()
	{
		if (lanes.Count == 0 || carPrefab == null)
		{
			Debug.LogWarning("No lanes or car prefab assigned.");
			return;
		}

		Lane chosenLane;
		bool spawnAtStart;
		Transform spawnPoint;
		spawnPoint = ChooseStartPosition(out chosenLane, out spawnAtStart, out spawnPoint);

		GameObject carObj = Instantiate(carPrefab, spawnPoint.position, Quaternion.identity);

		Vector3 targetDirection = chosenLane.endPosition.position - chosenLane.startPosition.position;
		carObj.transform.rotation = Quaternion.LookRotation(targetDirection.normalized, Vector3.up);

		Car carComponent = carObj.GetComponent<Car>();
		if (carComponent != null)
		{
			carComponent.moveForward = spawnAtStart;
			carComponent.currentLane = chosenLane;
		}
	}

	private Transform ChooseStartPosition(out Lane chosenLane, out bool spawnAtStart, out Transform spawnPoint)
	{
		chosenLane = lanes[Random.Range(0, lanes.Count)];
		spawnAtStart = Random.value > 0.5f;
		spawnPoint = spawnAtStart ? chosenLane.startPosition : chosenLane.endPosition;

		return spawnPoint;
	}

	/**
	 * CREATE LANES
	 */

	private void CreateLanes()
	{
		lanes.Clear();

		for (int i = 0; i < NumberOfLanes; i++)
		{
			float offset = (i - (NumberOfLanes - 1) / 2f) * LaneSpacing;

			Lane lane = InstatiateLane(i, offset);

			lanes.Add(lane);
		}
	}

	private Lane InstatiateLane(int i, float offset)
	{
		GameObject laneObj = lanePrefab != null ? Instantiate(lanePrefab, transform) : new GameObject($"Lane_{i}");
		laneObj.transform.parent = transform;

		Lane lane = laneObj.GetComponent<Lane>();
		if (lane == null)
			lane = laneObj.AddComponent<Lane>();

		CalculateStartAndEndOfLane(i, offset, laneObj, lane);

		return lane;
	}

	private void CalculateStartAndEndOfLane(int i, float offset, GameObject laneObj, Lane lane)
	{
		lane.startPosition = new GameObject($"Start_{i}").transform;
		lane.endPosition = new GameObject($"End_{i}").transform;

		lane.startPosition.parent = laneObj.transform;
		lane.endPosition.parent = laneObj.transform;

		lane.startPosition.position = transform.position + transform.right * offset;

		lane.endPosition.position = lane.startPosition.position + transform.forward * HighwayLength;
	}


	/**
	 * DRAW GIZMOS
	 */

	private void OnDrawGizmos()
	{
		// Draw lane lines
		Gizmos.color = Color.yellow;
		foreach (Lane lane in lanes)
		{
			if (lane.startPosition != null && lane.endPosition != null)
			{
				Gizmos.DrawLine(lane.startPosition.position, lane.endPosition.position);
				Gizmos.DrawSphere(lane.startPosition.position, 0.2f);
				Gizmos.DrawSphere(lane.endPosition.position, 0.2f);
			}
		}

		// Draw safe distance rectangles
		foreach (var rect in laneDebugRects)
		{
			Gizmos.color = rect.color;
			Vector3 forward = rect.forwardDir * (rect.length / 2f);
			Vector3 right = Vector3.Cross(Vector3.up, rect.forwardDir) * (rect.width / 2f);

			Vector3 p1 = rect.center + forward + right;
			Vector3 p2 = rect.center + forward - right;
			Vector3 p3 = rect.center - forward - right;
			Vector3 p4 = rect.center - forward + right;

			Gizmos.DrawLine(p1, p2);
			Gizmos.DrawLine(p2, p3);
			Gizmos.DrawLine(p3, p4);
			Gizmos.DrawLine(p4, p1);
		}

		// Draw highlighted cars
		foreach (var highlight in carHighlights)
		{
			Gizmos.color = highlight.color;
			Gizmos.DrawWireSphere(highlight.position, highlight.radius);
		}
	}

}
