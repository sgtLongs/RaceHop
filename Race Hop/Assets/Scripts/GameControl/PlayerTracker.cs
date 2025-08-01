using TMPro;
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

	[Header("Settings")]
	[Tooltip("World-space ΔZ that triggers a lane shift.")]
	[SerializeField] private float shiftThreshold = 50f;

	[Tooltip("Optional damping speed for tracker motion (units/sec). " +
			 "Set to 0 for instant following.")]
	[SerializeField] private float followSpeed = 0f;

	public Transform backFog;
	public GameObject dangerField;
	public Vector3 dangerFieldSpeed;
	public float dangerFieldSpeedFalloff;
	public float maxDistanceTraveled = 0;

	// Next world‑space Z at which to shift lanes
	private float nextShiftZ;

	public TMP_Text maxDistanceText;

	void Start()
	{
		if (player == null)
		{
			player = GameObject.FindGameObjectWithTag("Player")?.transform;

			if (player == null)
			{
				return;
			}
		}


		// First trigger point = first whole chunk ahead of the player
		nextShiftZ = Mathf.Floor(player.position.z / shiftThreshold + 1) * shiftThreshold;
	}

	void Update()
	{
		if (player == null)
		{
			player = GameObject.FindGameObjectWithTag("Player")?.transform;

			if (player == null)
			{
				return;
			}

			nextShiftZ = Mathf.Floor(player.position.z / shiftThreshold + 1) * shiftThreshold;
		}

		maxDistanceText.text = (maxDistanceTraveled/10) + " Meters";

		UpdateDangerFieldPosition();

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
				maxDistanceTraveled++;
			}
			else
			{
				// Smooth follow (moves at most followSpeed units per second)
				pos.z = Mathf.MoveTowards(pos.z, playerZ, followSpeed * Time.deltaTime);
			}
			transform.position = pos;
		}

	}

	private void UpdateDangerFieldPosition()
	{
		Vector3 position = dangerField.transform.position;
		float distanceFromBackFog = position.z - backFog.position.z;

		dangerField.transform.position += dangerFieldSpeed * Time.deltaTime;


		if (distanceFromBackFog < 0)
		{
			dangerField.transform.position = new Vector3(position.x, position.y, backFog.position.z);
		}
	}

}
