using UnityEngine;

/// <summary>
/// Follows the player smoothly along +Z and slides the highway forward
/// each time a distance chunk (shiftThreshold) is crossed.
/// Backward motion does not rewind either the tracker or the lanes.
/// </summary>
public class PlayerTracker : MonoBehaviour
{
	[Header("References")]
	[SerializeField] private Transform player;              // Player Transform to watch
	[SerializeField] private TrafficHandler trafficHandler; // Highway / lane owner

	[Header("Settings")]
	[Tooltip("World-space ΔZ that triggers a lane shift.")]
	[SerializeField] private float shiftThreshold = 50f;

	[Tooltip("Optional damping speed for tracker motion (units/sec). " +
			 "Set to 0 for instant following.")]
	[SerializeField] private float followSpeed = 0f;

	// Next world‑space Z at which to shift lanes
	private float nextShiftZ;

	void Start()
	{
		if (player == null)
			player = GameObject.FindGameObjectWithTag("Player")?.transform;

		if (trafficHandler == null || player == null)
		{
			Debug.LogError("PlayerTracker: missing references.");
			enabled = false;
			return;
		}

		// First trigger point = first whole chunk ahead of the player
		nextShiftZ = Mathf.Floor(player.position.z / shiftThreshold + 1) * shiftThreshold;
	}

	void Update()
	{
		float playerZ = player.position.z;

		//---------------------------
		// 1) Seamless tracker move
		//---------------------------
		if (playerZ > transform.position.z) // forward only
		{
			Vector3 pos = transform.position;

			if (followSpeed <= 0f)
			{
				// Instant follow
				pos.z = playerZ;
			}
			else
			{
				// Smooth follow (moves at most followSpeed units per second)
				pos.z = Mathf.MoveTowards(pos.z, playerZ, followSpeed * Time.deltaTime);
			}
			transform.position = pos;
		}

		//-----------------------------------
		// 2) Chunk‑based lane advancement
		//-----------------------------------
		while (playerZ >= nextShiftZ)
		{
			ShiftLanes(shiftThreshold);
			nextShiftZ += shiftThreshold;
		}
	}

	/// <summary>Moves every lane forward by deltaZ using TrafficHandler.</summary>
	private void ShiftLanes(float deltaZ)
	{
		// Any lane will give us current Z extents; index 0 is fine
		Lane refLane = trafficHandler.GetLaneByIndex(0);
		if (refLane == null) return;

		float newStartZ = refLane.startPosition.position.z + deltaZ;
		float newEndZ = refLane.endPosition.position.z + deltaZ;

		trafficHandler.SetAllLaneZ(newStartZ, newEndZ);
	}
}
