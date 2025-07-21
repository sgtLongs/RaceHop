using UnityEngine;
using System.Collections.Generic;

public class GameController : MonoBehaviour
{
	[Header("Spawning")]
	public float spawnInterval = 2.5f;
	public int maxCars = 25;             // <= 0 means "no cap"

	private float spawnTimer;
	private TrafficHandler trafficHandler;

	private readonly List<Car> activeCars = new List<Car>();

	void Awake()
	{
		trafficHandler = GetComponent<TrafficHandler>();
		if (trafficHandler == null)
			Debug.LogError("LaneHandler missing on GameController.");
	}

	void Update()
	{
		spawnTimer += Time.deltaTime;

		if (spawnTimer >= spawnInterval)
		{
			

			// Only spawn if below cap (or cap disabled)
			if (maxCars <= 0 || GameObject.FindGameObjectsWithTag("Car").Length < maxCars)
			{
				spawnTimer -= spawnInterval; // keep leftover fraction
				TrySpawnCar();
			}
			else
			{
				// Option 1: do NOT reset timer until a spawn succeeds -> keeps pressure to spawn ASAP
				// spawnTimer stays >= spawnInterval; as soon as a slot frees we spawn next frame.
				// If you prefer a fixed rhythm even when capped, move the line above (subtract) outside the if.
			}
		}
	}

	private void TrySpawnCar()
	{
		trafficHandler.SpawnCar();
	}

	// Fallback periodic cleanup (optional)
	private float cleanupTimer;
	void LateUpdate()
	{
		cleanupTimer += Time.deltaTime;
		if (cleanupTimer >= 5f)
		{
			cleanupTimer = 0f;
			activeCars.RemoveAll(c => c == null);
		}
	}
}
