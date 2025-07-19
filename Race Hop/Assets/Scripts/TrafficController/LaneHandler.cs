using System.Collections.Generic;
using UnityEngine;

public class LaneHandler : MonoBehaviour
{
	#region Fields & Properties

	[Header("Highway Settings")]
	public int NumberOfLanes = 3;
	public float LaneSpacing = 3f;
	public float HighwayLength = 100f; // Distance forward for the end of the highway

	[Header("Prefabs")]
	public GameObject lanePrefab;
	public GameObject carPrefab;

	private List<Lane> lanes = new List<Lane>();
	public bool spawnACar;

	// Debug Data
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

	#endregion

	#region Unity Lifecycle

	void Start()
	{
		CreateLanes();
	}

	void Update()
	{
		if (spawnACar)
		{
			SpawnCar();
			spawnACar = false;
		}
	}

	private void OnDrawGizmos()
	{
		DrawLaneGizmos();
		DrawDebugRects();
		DrawCarHighlights();
	}

	#endregion

	#region Lane Management

	private void CreateLanes()
	{
		lanes.Clear();

		for (int i = 0; i < NumberOfLanes; i++)
		{
			float offset = (i - (NumberOfLanes - 1) / 2f) * LaneSpacing;
			Lane lane = InstantiateLane(i, offset);
			lanes.Add(lane);
		}
	}

	private Lane InstantiateLane(int index, float offset)
	{
		GameObject laneObj = lanePrefab != null ? Instantiate(lanePrefab, transform) : new GameObject($"Lane_{index}");
		laneObj.transform.parent = transform;

		Lane lane = laneObj.GetComponent<Lane>() ?? laneObj.AddComponent<Lane>();
		SetupLanePositions(index, offset, laneObj, lane);

		return lane;
	}

	private void SetupLanePositions(int index, float offset, GameObject laneObj, Lane lane)
	{
		lane.startPosition = new GameObject($"Start_{index}").transform;
		lane.endPosition = new GameObject($"End_{index}").transform;

		lane.startPosition.parent = laneObj.transform;
		lane.endPosition.parent = laneObj.transform;

		lane.startPosition.position = transform.position + transform.right * offset;
		lane.endPosition.position = lane.startPosition.position + transform.forward * HighwayLength;
	}

	public int GetLaneIndex(Lane lane) => lanes.IndexOf(lane);
	public int GetLaneCount() => lanes.Count;

	public Lane GetLaneByIndex(int index)
	{
		if (index >= 0 && index < lanes.Count) return lanes[index];
		return null;
	}

	#endregion

	#region Car Management

	public void SpawnCar()
	{
		if (lanes.Count == 0 || carPrefab == null)
		{
			Debug.LogWarning("No lanes or car prefab assigned.");
			return;
		}

		Transform spawnPoint = ChooseStartPosition(out Lane chosenLane, out bool spawnAtStart, out _);

		GameObject carObj = Instantiate(carPrefab, spawnPoint.position, Quaternion.identity);
		carObj.transform.rotation = Quaternion.LookRotation(chosenLane.endPosition.position - chosenLane.startPosition.position, Vector3.up);

		Car carComponent = carObj.GetComponent<Car>();
		if (carComponent != null)
		{
			carComponent.moveForward = spawnAtStart;
			carComponent.currentLane = chosenLane;
			carComponent.currentLane.SubscribeCar(carComponent);
		}
	}

	private Transform ChooseStartPosition(out Lane chosenLane, out bool spawnAtStart, out Transform spawnPoint)
	{
		chosenLane = lanes[Random.Range(0, lanes.Count)];

		spawnAtStart = Random.value > 0.5f;
		spawnPoint = spawnAtStart ? chosenLane.startPosition : chosenLane.endPosition;
		return spawnPoint;
	}

	#endregion

	#region Lane Switching Logic

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
		int[] directions = preferLeft ? new[] { -1, 1 } : new[] { 1, -1 };

		foreach (int dir in directions)
		{
			int targetIndex = currentIndex + dir;
			if (targetIndex >= 0 && targetIndex < GetLaneCount())
			{
				Lane targetLane = GetLaneByIndex(targetIndex);
				if (targetLane != null && IsLaneClearForCar(car, targetLane))
				{
					car.currentLane.UnsubscribeCar(car);
					targetLane.SubscribeCar(car);
					return targetLane;
				}
			}
		}
		return null;
	}

	public Lane FindSwitchableLane(Car car)
	{
		if (car == null || car.currentLane == null)
		{
			Debug.LogWarning("Invalid car or current lane for lane check.");
			return null;
		}

		int currentIndex = GetLaneIndex(car.currentLane);
		if (currentIndex == -1) return null;

		bool preferLeft = ShouldCarSwitchToLeftLane();
		int[] directions = preferLeft ? new[] { -1, 1 } : new[] { 1, -1 };

		foreach (int dir in directions)
		{
			int targetIndex = currentIndex + dir;
			if (targetIndex < 0 || targetIndex >= GetLaneCount()) continue;

			Lane targetLane = GetLaneByIndex(targetIndex);
			if (targetLane != null && IsLaneClearForCar(car, targetLane)) return targetLane;
		}

		return null;
	}

	private static bool ShouldCarSwitchToLeftLane() => Random.value > 0.5f;

	private bool IsLaneClearForCar(Car car, Lane targetLane)
	{
		float safeDistance = car.checkAheadDistance + 1;
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

		// Debug rectangle
		laneDebugRects.Add(new LaneCheckDebug
		{
			center = equivalentPos,
			forwardDir = (targetLaneEnd - targetLaneStart).normalized,
			length = safeDistance * 2f,
			width = 3f,
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

			if (Vector3.Distance(otherCar.transform.position, equivalentPos) < safeDistance)
			{
				return false;
			}
		}
		return true;
	}

	#endregion

	#region Car Distance Checks

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

			if (otherDistance > carDistance && otherDistance - carDistance < distanceAhead)
			{
				distanceAhead = otherDistance - carDistance;
				closestCar = otherCar;
			}
		}
		return closestCar;
	}

	public Car GetCarBehind(Lane lane, Car car, out float distanceBehind)
	{
		distanceBehind = float.MaxValue;
		Car closest = null;

		if (lane == null || car == null) return null;

		Vector3 laneStart = lane.startPosition.position;
		Vector3 laneEnd = lane.endPosition.position;
		Vector3 laneDir = (laneEnd - laneStart).normalized;

		float carDist = Vector3.Dot(car.transform.position - laneStart, laneDir);

		foreach (Car other in lane.cars)
		{
			if (other == null || other == car) continue;

			float otherDist = Vector3.Dot(other.transform.position - laneStart, laneDir);

			if (otherDist < carDist && carDist - otherDist < distanceBehind)
			{
				distanceBehind = carDist - otherDist;
				closest = other;
			}
		}
		return closest;
	}

	public bool IsCarTooClose(Lane lane, Car car, float checkDistance)
	{
		GetCarAhead(lane, car, out float distanceAhead);
		return distanceAhead < checkDistance;
	}

	#endregion

	#region Gizmo Drawing

	private void DrawLaneGizmos()
	{
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
	}

	private void DrawDebugRects()
	{
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
	}

	private void DrawCarHighlights()
	{
		foreach (var highlight in carHighlights)
		{
			Gizmos.color = highlight.color;
			Gizmos.DrawWireSphere(highlight.position, highlight.radius);
		}
	}

	#endregion
}
