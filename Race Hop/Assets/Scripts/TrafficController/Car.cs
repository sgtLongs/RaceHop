using UnityEngine;

public class Car : MonoBehaviour
{
	[Header("Car Settings")]
	public bool moveForward = true;
	public float maxForwardSpeed = 10f;
	public float forwardAcceleration = 5f;
	public float backwardSpeed = 5f;

	[Header("Cleanup Settings")]
	public float deleteThreshold = 2f;

	[Header("Lane Info")]
	public Lane currentLane;

	[Header("Backward Reaction Settings")]
	public float backwardBoostMultiplier = 1.5f;      // 1.5x base backward speed
	public float backwardBoostLerpSpeed = 4f;         // How fast we lerp up to boosted speed
	public float backwardRecoverLerpSpeed = 2f;       // How fast we return to normal


	[Header("Lane Change Settings")]
	public float laneChangeSpeed = 5f;
	public float checkAheadDistance = 10f;

	[SerializeField]private float currentSpeed = 0f;
	private LaneHandler laneHandler;
	private bool isChangingLane = false;

	void Start()
	{
		laneHandler = FindFirstObjectByType<LaneHandler>();
		if (laneHandler == null)
		{
			Debug.LogError("LaneHandler not found in the scene!");
			return;
		}

		if (!moveForward)
		{
			currentSpeed = backwardSpeed;
		}

		// Register with current lane
		if (currentLane != null)
		{
			currentLane.SubscribeCar(this);
		}
	}

	void Update()
	{
		if (moveForward)
		{
			HandleForwardMovement();
		}
		else
		{
			HandleBackwardMovement();
		}

		// Apply movement
		Vector3 direction = moveForward ? transform.forward : -transform.forward;
		transform.position += direction * currentSpeed * Time.deltaTime;

		// Lifecycle
		CheckForEndOfLane();
	}

	private void HandleBackwardMovement()
	{
		// Base target = normal backward cruising speed
		float baseSpeed = backwardSpeed;

		// Compute “half detection” distance
		float halfCheck = checkAheadDistance;

		// Find a car directly behind (toward lane end) within halfCheck
		Car carBehind = null;
		float distanceBehind = float.MaxValue;

		if (currentLane != null)
		{
			Vector3 laneStart = currentLane.startPosition.position;
			Vector3 laneEnd = currentLane.endPosition.position;
			Vector3 laneDir = (laneEnd - laneStart).normalized;

			float myDist = Vector3.Dot(transform.position - laneStart, laneDir);

			foreach (var other in currentLane.cars)
			{
				if (other == null || other == this) continue;

				float otherDist = Vector3.Dot(other.transform.position - laneStart, laneDir);

				// For a backward-moving car, "behind" (approaching us) means: otherDist > myDist
				float gap = myDist - otherDist;
				if (gap > 0f && gap < halfCheck)
				{
					if (gap < distanceBehind)
					{
						distanceBehind = gap;
						carBehind = other;
					}
				}
			}
		}

		float target = baseSpeed;

		if (carBehind != null)
		{
			// We have a car behind us inside half detection distance – boost backward speed.
			float boosted = baseSpeed - (baseSpeed * (backwardBoostMultiplier - 1));

			// Closer = stronger push toward boosted speed. (gap 0 → full boost, gap = halfCheck → small boost)
			float proximityT = 1f - Mathf.Clamp01(distanceBehind / halfCheck);


			// Lerp toward boosted speed using proximity factor
			target = Mathf.Lerp(baseSpeed, boosted, proximityT);

			// Smoothly move currentSpeed toward the target (boost) faster
			currentSpeed = Mathf.Lerp(currentSpeed, target, backwardBoostLerpSpeed * Time.deltaTime);
		}
		else
		{
			// No one close behind – ease back to normal backward speed
			currentSpeed = Mathf.Lerp(currentSpeed, target, backwardRecoverLerpSpeed * Time.deltaTime);
		}
	}


	private void HandleForwardMovement()
	{
		float distanceAhead = float.MaxValue;
		Car carAhead = null;
		float targetSpeed = maxForwardSpeed;

		carAhead = laneHandler.GetCarAhead(currentLane, this, out distanceAhead);

		if (carAhead != null)
		{
			targetSpeed = CalculateTargetSpeed(distanceAhead, carAhead, targetSpeed);
		}

		currentSpeed = CalculateCurrentSpeed(targetSpeed, carAhead, distanceAhead);

		if (!isChangingLane &&
		currentLane != null &&
		laneHandler != null &&
		distanceAhead < checkAheadDistance)
		{
			TryChangeLaneIfBlocked();
		}
	}

	private float CalculateCurrentSpeed(float targetSpeed, Car carAhead, float distanceAhead)
	{
		bool braking = targetSpeed < currentSpeed;
		float accelMagnitude = braking
			? forwardAcceleration * 8f
			: forwardAcceleration;

		currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, accelMagnitude * Time.deltaTime);

		if (carAhead != null)
		{
			float minGap = 1.5f;
			if (distanceAhead < float.MaxValue)
			{
				float allowedForward = distanceAhead - minGap;
				float intendedMove = currentSpeed * Time.deltaTime;

				if (intendedMove > allowedForward)
				{
					float clampedSpeed = allowedForward / Mathf.Max(Time.deltaTime, 0.0001f);
					currentSpeed = carAhead != null
						? Mathf.Max(clampedSpeed, (carAhead.GetCurrentSpeed() - 2f))
						: clampedSpeed;
				}
			}
		}

		return currentSpeed;
	}

	private float CalculateTargetSpeed(float distanceAhead, Car carAhead, float targetSpeed)
	{
		float decelZone = checkAheadDistance * 1.5f;
		float minZone = decelZone * 0.4f;
		float carAheadSpeed = carAhead.GetCurrentSpeed();
		float desiredMinSpeed = carAheadSpeed - 1f;

		if (distanceAhead < decelZone)
		{
			float blend;
			if (distanceAhead <= minZone)
				blend = 1f;
			else
				blend = (decelZone - distanceAhead) / (decelZone - minZone);
			blend = Mathf.Clamp01(blend);

			targetSpeed = Mathf.Lerp(maxForwardSpeed, desiredMinSpeed, blend);
		}

		if (distanceAhead < minZone * 0.5f)
		{
			targetSpeed = Mathf.Min(targetSpeed, carAheadSpeed - 1f);
		}

		return targetSpeed;
	}

	public float GetCurrentSpeed()
	{
		return (moveForward == true) ? currentSpeed : currentSpeed * -1;
	}


	private void CheckForEndOfLane()
	{
		if (currentLane == null) return;

		Vector3 targetPoint = moveForward ? currentLane.endPosition.position : currentLane.startPosition.position;

		if (Vector3.Distance(transform.position, targetPoint) < deleteThreshold)
		{
			currentLane.UnsubscribeCar(this);
			Destroy(gameObject);
		}
	}

	private bool IsCarAhead()
	{
		if (currentLane == null) return false;

		Vector3 laneStart = currentLane.startPosition.position;
		Vector3 laneEnd = currentLane.endPosition.position;
		Vector3 laneDir = (laneEnd - laneStart).normalized;

		float myDistanceAlongLane = Vector3.Dot(transform.position - laneStart, laneDir);

		foreach (Car otherCar in currentLane.cars)
		{
			if (otherCar == null || otherCar == this) continue;

			float otherDistance = Vector3.Dot(otherCar.transform.position - laneStart, laneDir);

			if (otherDistance > myDistanceAlongLane) // Ahead
			{
				float gap = otherDistance - myDistanceAlongLane;
				if (gap < checkAheadDistance)
				{
					return true;
				}
			}
		}

		return false;
	}

	private void TryChangeLaneIfBlocked()
	{
		Lane targetLane = laneHandler.SwitchCarLane(this);
		if (targetLane != null)
		{
			StartCoroutine(LerpToLane(targetLane));
		}
	}

	private System.Collections.IEnumerator LerpToLane(Lane targetLane)
	{
		isChangingLane = true;

		Vector3 startPos = transform.position;

		Vector3 laneStart = currentLane.startPosition.position;
		Vector3 laneEnd = currentLane.endPosition.position;
		Vector3 laneDir = (laneEnd - laneStart).normalized;
		float laneLength = Vector3.Distance(laneStart, laneEnd);
		float distanceAlongLane = Vector3.Dot(transform.position - laneStart, laneDir);
		float progress = Mathf.Clamp01(distanceAlongLane / laneLength);

		Vector3 newTarget = Vector3.Lerp(targetLane.startPosition.position, targetLane.endPosition.position, progress);
		Quaternion targetRotation = Quaternion.LookRotation((targetLane.endPosition.position - targetLane.startPosition.position).normalized);

		float t = 0f;
		while (t < 1f)
		{
			t += Time.deltaTime * laneChangeSpeed;
			transform.position = Vector3.Lerp(startPos, newTarget, t);
			transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, t);
			yield return null;
		}

		transform.position = newTarget;
		transform.rotation = targetRotation;

		currentLane = targetLane;
		isChangingLane = false;
	}

	private void OnDrawGizmos()
	{
		if (currentLane == null) return;

		// FORWARD CARS: show forward detection (critical + decel zone)
		if (moveForward)
		{
			// Critical lane‑change zone (full width)
			Gizmos.color = Color.cyan;
			DrawDirectionalZone(checkAheadDistance, 3f, transform.forward);

			// Deceleration zone (1.5× length, thinner)
			Gizmos.color = Color.yellow;
			DrawDirectionalZone(checkAheadDistance * 1.5f, 1.5f, transform.forward);

			// Direction line (to max decel length)
			Gizmos.color = Color.blue;
			Gizmos.DrawLine(transform.position,
							transform.position + transform.forward * (checkAheadDistance * 1.5f));
		}
		else
		{
			// BACKWARD CARS:
			// Car is *moving* along -transform.forward, so traffic "behind it" (relative to travel)
			// is actually in the +transform.forward direction. We visualize the boost zone THERE.

			float halfCheck = checkAheadDistance;

			Gizmos.color = Color.magenta;
			DrawDirectionalZone(halfCheck, 2.0f, -transform.forward);

			Gizmos.color = Color.magenta;
			Gizmos.DrawLine(transform.position,
							transform.position - transform.forward * halfCheck);

		}
	}

	/// <summary>
	/// Draws a wire box extending in a chosen direction from the car.
	/// </summary>
	/// <param name="distance">Length along the direction.</param>
	/// <param name="width">Lateral width of the box.</param>
	/// <param name="direction">World-space direction (normalized) the zone extends toward.</param>
	private void DrawDirectionalZone(float distance, float width, Vector3 direction)
	{
		float height = 2f;
		Vector3 dirNorm = direction.normalized;
		Vector3 center = transform.position + dirNorm * (distance * 0.5f);
		Vector3 size = new Vector3(width, height, distance);
		Gizmos.DrawWireCube(center, size);
	}


}
