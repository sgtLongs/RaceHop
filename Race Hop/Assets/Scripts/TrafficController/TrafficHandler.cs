using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

public class  TrafficHandler : MonoBehaviour
{
	#region Fields & Properties

	[Header("Highway Settings")]
	public int NumberOfLanes = 3;
	public float LaneSpacing = 3f;
	public float HighwayLength = 100f; // Distance forward for the end of the highway
	public float HighwayLaneSpeed = 10f;

	[Header("CarSettings")]
	public float forwardMovingPercent = 0.1f;
	public float carAverageSpeed = 6f;
	public float carSpeedVariability = 4f;
	public float driverVoractiy = 0.1f;

	[Header("Prefabs")]
	public GameObject lanePrefab;
	public GameObject carPrefab;
	public GameObject startCarPrefab;

	public float startCartSpawnOffset = 0.5f;
	public float laneYOffset = 1f;

	[Tooltip("How far down the lane (in metres) we look for other cars " +
		 "before allowing a new spawn.")]
	public float SpawnCheckDepth = 12f;

	public GameController gameController;

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
		SpawnStartCar();
		gameController.PopulateHighway();
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

	/// <summary>
	/// Adjusts every lane so that the start‑marker takes one Z value
	/// and the end‑marker takes another, leaving X & Y unchanged.
	/// </summary>
	/// <param name="startZ">World‑space Z to apply to every lane’s startPosition.</param>
	/// <param name="endZ">World‑space Z to apply to every lane’s endPosition.</param>
	public void SetAllLaneZ(float startZ, float endZ)
	{
		if (lanes == null || lanes.Count == 0) return;

		foreach (Lane lane in lanes)
		{
			if (lane == null) continue;

			// ------ Start point ------
			if (lane.startPosition != null)
			{
				Vector3 p = lane.startPosition.position;
				p.z = startZ;
				lane.startPosition.position = p;
			}

			// ------- End point -------
			if (lane.endPosition != null)
			{
				Vector3 p = lane.endPosition.position;
				p.z = endZ;
				lane.endPosition.position = p;
			}
		}
	}


	private void CreateLanes()
	{
		lanes.Clear();

		InstantiateHighway();

		for (int i = 0; i < NumberOfLanes; i++)
		{
			float offset = (i - (NumberOfLanes - 1) / 2f) * LaneSpacing;
			Lane lane = InstantiateLane(i, offset);
			lanes.Add(lane);
		}
	}

	private void InstantiateHighway()
	{
		GameObject highwayObj = Instantiate(lanePrefab, transform);
		highwayObj.transform.position = new Vector3(highwayObj.transform.position.x, highwayObj.transform.position.y + laneYOffset, highwayObj.transform.position.z + HighwayLength/2);
		Renderer renderer = highwayObj.GetComponent<Renderer>();

		renderer.material.SetFloat("_NumberOfLanes", NumberOfLanes);
		renderer.material.SetVector("_TextureSpeed", new Vector4(0, HighwayLaneSpeed * -1, 0,0));
		highwayObj.transform.localScale = new Vector3(NumberOfLanes/2, highwayObj.transform.localScale.y, highwayObj.transform.localScale.z);
	}

	private Lane InstantiateLane(int index, float offset)
	{
		GameObject laneObj = new GameObject($"Lane_{index}");
		laneObj.transform.parent = transform;

		Lane lane = laneObj.GetComponent<Lane>() ?? laneObj.AddComponent<Lane>();
		SetupLanePositions(index, offset, laneObj, lane);

		return lane;
	}

	private void SetupLanePositions(int index, float offset, GameObject laneObj, Lane lane)
	{
		lane.startPosition = new GameObject($"Start_{index}").transform;
		lane.endPosition = new GameObject($"End_{index}").transform;

		laneObj.transform.position = transform.position + transform.right * offset + transform.up * laneYOffset + transform.forward * (HighwayLength/2);

		lane.startPosition.parent = laneObj.transform;
		lane.endPosition.parent = laneObj.transform;

		lane.startPosition.position = transform.position + transform.right * offset;
		//lane.startPosition.position = transform.position + transform.right * offset;
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

	public void SpawnStartCar()
	{
		if (lanes.Count == 0 || startCarPrefab == null)
		{
			Debug.LogWarning("No lanes or car prefab assigned.");
			return;
		}

		

		Lane chosenLane = lanes[Mathf.FloorToInt(lanes.Count / 2)];

		Vector3 spawnPoint = Vector3.Lerp(chosenLane.startPosition.position, chosenLane.endPosition.position, startCartSpawnOffset);

		GameObject carObj = Instantiate(startCarPrefab, spawnPoint, Quaternion.identity);
		carObj.transform.rotation = Quaternion.LookRotation(chosenLane.endPosition.position - chosenLane.startPosition.position, Vector3.up);

		Car carComponent = carObj.GetComponent<Car>();
		if (carComponent != null)
		{
			carComponent.moveForward = true;
			carComponent.BaseSpeed = 0;
			carComponent.currentLane = chosenLane;
			carComponent.isStatic = true;
			carComponent.currentLane.SubscribeCar(carComponent);
		}

		gameController.SetStartCar(carComponent);
	}

	public void SpawnCar()
	{
		if (lanes.Count == 0 || carPrefab == null)
		{
			Debug.LogWarning("No lanes or car prefab assigned.");
			return;
		}

		Transform? spawnPoint = ChooseStartPosition(out Lane chosenLane, out bool spawnAtStart, out _);

		if (spawnPoint == null) return;

		GameObject carObj = Instantiate(carPrefab, spawnPoint.position, Quaternion.identity);
		carObj.transform.rotation = Quaternion.LookRotation(chosenLane.endPosition.position - chosenLane.startPosition.position, Vector3.up);

		int directionModifier = spawnAtStart ? 1 : -1;

		Car carComponent = carObj.GetComponent<Car>();
		if (carComponent != null)
		{
			carComponent.moveForward = spawnAtStart;
			carComponent.BaseSpeed = 8f * directionModifier;
			carComponent.currentLane = chosenLane;
			carComponent.currentLane.SubscribeCar(carComponent);
		}
	}

	private Transform? ChooseStartPosition(out Lane chosenLane,
									  out bool spawnAtStart,
									  out Transform spawnPoint)
	{
		laneDebugRects.Clear();
		const int MAX_ATTEMPTS = 10;

		for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
		{
			// 1. random lane + end‑point
			chosenLane = lanes[Random.Range(0, lanes.Count)];
			spawnAtStart = ShouldCarMoveForward();
			spawnPoint = spawnAtStart ? chosenLane.startPosition
										: chosenLane.endPosition;

			// 2. free space?
			if (!SpawnAreaOccupied(chosenLane, spawnPoint.position, spawnAtStart))
				return spawnPoint;
		}

		Debug.LogWarning($"TrafficHandler: couldn’t find a free spawn after " +
						 $"{MAX_ATTEMPTS} attempts – forcing first lane.");
		chosenLane = lanes[0];
		spawnAtStart = true;
		spawnPoint = null;
		return spawnPoint;
	}

	/// <summary>
	/// True if a rectangle (LaneSpacing × SpawnCheckDepth) starting at
	/// <paramref name="origin"/> in the travel direction contains another
	/// car in the same lane.  Always records a gizmo rectangle showing the test.
	/// </summary>
	private bool SpawnAreaOccupied(Lane lane, Vector3 origin, bool forwardDir)
	{
		// Direction the new car would travel.
		Vector3 dir = (lane.endPosition.position - lane.startPosition.position).normalized;
		if (!forwardDir) dir = -dir;

		float halfWidth = LaneSpacing * 0.5f;
		float halfLength = SpawnCheckDepth * 0.5f;

		// -------- Occupancy test (cars already registered in this lane) --------
		bool occupied = false;

		foreach (Car other in lane.cars)
		{
			if (other == null) continue;

			Vector3 delta = other.transform.position - origin;
			float longDist = Vector3.Dot(delta, dir);                   // forward distance
			if (longDist < 0f || longDist > SpawnCheckDepth) continue;    // outside front edge?

			Vector3 lateral = delta - dir * longDist;                    // sideways offset
			if (lateral.magnitude <= halfWidth)
			{
				occupied = true;
				break;
			}
		}

		// -------- Secondary physics overlap (catches cars not yet subscribed) --------
		if (!occupied)
		{
			Vector3 centre = origin + dir * halfLength;
			Vector3 halfExts = new Vector3(halfWidth, 2f, halfLength);  // y = 4 m tall
			Collider[] hits = Physics.OverlapBox(centre,
													halfExts,
													Quaternion.LookRotation(dir),
													~0,
													QueryTriggerInteraction.Ignore);

			foreach (Collider hit in hits)
				if (hit.CompareTag("Car")) { occupied = true; break; }
		}

		// --------‑‑‑  DEBUG RECTANGLE  ‑‑‑--------
		laneDebugRects.Add(new LaneCheckDebug
		{
			center = origin + dir * halfLength,
			forwardDir = dir,
			length = SpawnCheckDepth,
			width = LaneSpacing,
			color = occupied ? new Color(1f, 0.3f, 0.3f)            // red = blocked
								  : new Color(0.3f, 1f, 0.3f)            // green = clear
		});

		return occupied;
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

	/// <summary>
	/// Spawns a car at a random, free point on a random lane.
	/// Returns true if a car was spawned.
	/// </summary>
	/// <param name="edgeBuffer">Minimum distance from lane ends (metres) to avoid spawning too close to endpoints.</param>
	/// <param name="maxAttemptsPerLane">Tries per lane before giving up and changing lanes.</param>
	public bool SpawnCarAtRandomFreePoint(float edgeBuffer = 3f, int maxAttemptsPerLane = 8)
	{
		if (lanes == null || lanes.Count == 0 || carPrefab == null)
		{
			Debug.LogWarning("SpawnCarAtRandomFreePoint: No lanes or car prefab assigned.");
			return false;
		}

		laneDebugRects.Clear(); // for gizmos

		int totalAttempts = lanes.Count * maxAttemptsPerLane;
		for (int attempt = 0; attempt < totalAttempts; attempt++)
		{
			// 1) Pick a random lane
			Lane lane = lanes[Random.Range(0, lanes.Count)];
			if (lane == null || lane.startPosition == null || lane.endPosition == null) continue;

			Vector3 start = lane.startPosition.position;
			Vector3 end = lane.endPosition.position;
			Vector3 dir = (end - start).normalized;
			float laneLen = Vector3.Distance(start, end);

			// Ensure we have room for the buffer
			if (laneLen <= edgeBuffer * 2f) continue;

			// 2) Pick a random position t in [buffer, 1-buffer]
			float tMin = edgeBuffer / laneLen;
			float tMax = 1f - tMin;
			float t = Random.Range(tMin, tMax);
			Vector3 spawnPos = Vector3.Lerp(start, end, t);

			// 3) Check the area around the point is free (symmetric ahead/behind)
			float halfLen = SpawnCheckDepth * 0.5f;
			if (!IsRegionFreeAroundPoint(lane, spawnPos, halfLen))
				continue;


			bool moveForward = ShouldCarMoveForward();
			SpawnCarObject(moveForward, lane, spawnPos);

			return true;
		}

		Debug.LogWarning("SpawnCarAtRandomFreePoint: Could not find a free spot after multiple attempts.");
		return false;
	}

	private bool ShouldCarMoveForward()
	{
		if(Random.value <= forwardMovingPercent) return false;
		return false;
	}

	private void SpawnCarObject(bool moveForward, Lane lane, Vector3 spawnPosition)
	{
		GameObject carObj = Instantiate(carPrefab, spawnPosition, quaternion.identity);
		Car carComponent = carObj.GetComponent<Car>();

		if (carComponent != null)
		{
			carComponent.moveForward = moveForward;
			carComponent.BaseSpeed = GetCarSpeed(moveForward);
			carComponent.currentLane = lane;
			lane.SubscribeCar(carComponent);
		}
	}

	private float GetCarSpeed(bool moveForward)
	{
		float speedDifference = (carSpeedVariability * Random.value * Random.value) * ((Mathf.RoundToInt(Random.value) * 2) - 1);

		float speed = carAverageSpeed + speedDifference;

		if (moveForward)
		{
			speed /= 2;
			
		}
		else
		{
			speed *= -1;
		}

		return speed;
	}

	/// <summary>
	/// Returns true if a rectangle (LaneSpacing × (2 * halfLength)) centered at
	/// <paramref name="origin"/> along the lane is free of cars. Records a gizmo rect.
	/// </summary>
	private bool IsRegionFreeAroundPoint(Lane lane, Vector3 origin, float halfLength)
	{
		Vector3 start = lane.startPosition.position;
		Vector3 end = lane.endPosition.position;
		Vector3 dir = (end - start).normalized;

		float halfWidth = LaneSpacing * 0.5f;
		bool occupied = false;

		// --- Check lane's registered cars ---
		foreach (Car other in lane.cars)
		{
			if (other == null) continue;

			Vector3 delta = other.transform.position - origin;
			float longDist = Vector3.Dot(delta, dir); // signed distance along lane
			if (Mathf.Abs(longDist) > halfLength) continue;

			Vector3 lateral = delta - dir * longDist;
			if (lateral.magnitude <= halfWidth)
			{
				occupied = true;
				break;
			}
		}

		// --- Physics overlap (catches cars not yet subscribed) ---
		if (!occupied)
		{
			Vector3 centre = origin;
			Vector3 halfExts = new Vector3(halfWidth, 2f, halfLength); // y=4m tall
			Collider[] hits = Physics.OverlapBox(
				centre,
				halfExts,
				Quaternion.LookRotation(dir),
				~0,
				QueryTriggerInteraction.Ignore
			);

			foreach (Collider hit in hits)
			{
				if (hit != null && hit.CompareTag("Car")) { occupied = true; break; }
			}
		}

		// --- Debug rect for gizmos ---
		laneDebugRects.Add(new LaneCheckDebug
		{
			center = origin,
			forwardDir = dir,
			length = halfLength * 2f,
			width = LaneSpacing,
			color = occupied ? new Color(1f, 0.3f, 0.3f) : new Color(0.3f, 1f, 0.3f)
		});

		return !occupied;
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
