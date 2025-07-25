using UnityEngine;
using System.Collections.Generic;

public class GameController : MonoBehaviour
{
	[Header("Spawning")]
	public float spawnInterval = 2.5f;
	public int maxCars = 25;             // <= 0 means "no cap"

	private float spawnTimer;
	public TrafficHandler trafficHandler;

	private readonly List<Car> activeCars = new List<Car>();

	public GameObject PlayerPrefab;
	public Vector3 PlayerSpawnOffset = new Vector3(0,3,0);
	public Vector3 PlayerSpawnRotation = new Vector3(0,0,0);
	private Car StartCar;

	public float timeScale;

	public bool spawnPlayer = true;

	public int MaxNumberOfInitialPopulationFailures = 10;


	void Awake()
	{
		if (trafficHandler == null)
			Debug.LogError("LaneHandler missing on GameController.");

		
	}

	void Update()
	{
		Time.timeScale = timeScale;
		Time.fixedDeltaTime = 0.02f * Time.timeScale;

		if(spawnPlayer)
		{
			spawnPlayer = false;
			SpawnPlayer();
		}

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

	public void PopulateHighway()
	{
		int numberOfFailures = 0;

		while(numberOfFailures < MaxNumberOfInitialPopulationFailures && GameObject.FindGameObjectsWithTag("Car").Length < maxCars)
		{
			bool success = trafficHandler.SpawnCarAtRandomFreePoint();

			if(!success)
			{
				numberOfFailures++;
			}
		}
	}

	public void SetStartCar(Car car)
	{
		this.StartCar = car;
	}

	public void SpawnPlayer()
	{
		Vector3 spawnPosition = StartCar.Rigidbody.position + PlayerSpawnOffset;

		Instantiate(PlayerPrefab, spawnPosition, Quaternion.Euler(PlayerSpawnRotation));
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
