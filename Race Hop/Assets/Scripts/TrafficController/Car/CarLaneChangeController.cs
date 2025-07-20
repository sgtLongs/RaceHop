using UnityEngine;
using System.Collections;
using UnityEditor;

[DisallowMultipleComponent]
[RequireComponent(typeof(Car))]
public class CarLaneChangeController : MonoBehaviour
{
	[Header("Lane Change")]
	public float laneChangeSpeed = 5f; // Interp rate: units of "normalized progress per second"

	[Header("Courtesy Yield")]
	public float courtesyCooldown = 2f;
	private float lastCourtesyTime = -999f;

	[Header("Gizmo")]
	public bool showCourtesyZone = true;

	private Car car;
	private CarSpeedController speed;
	private bool isChanging = false;

	void Awake()
	{
		car = GetComponent<Car>();
		speed = GetComponent<CarSpeedController>();
	}

	public void HandleLaneChange(Car.CarScanResult scan)
	{
		if (isChanging || car.currentLane == null || car.TrafficHandler == null)
			return;

		// 1. Standard “stuck behind car” change (forward only)
		if (car.moveForward && scan.HasCarAhead && scan.distanceAhead < car.checkAheadDistance)
			TrySwitchLane();

		// 2. Courtesy yield (rear car blocked & we are off cooldown)
		if (scan.HasCarBehind &&
			scan.distanceBehind <= car.rearCheckDistance &&
			Time.time - lastCourtesyTime >= courtesyCooldown)
		{
			Car rear = scan.carBehind;

			// If rear can already switch itself, we do nothing.
			if (car.TrafficHandler.FindSwitchableLane(rear) == null)
			{
				if (TrySwitchLane())
					lastCourtesyTime = Time.time;
			}
		}
	}

	private bool TrySwitchLane()
	{
		Lane newLane = car.TrafficHandler.SwitchCarLane(car);
		if (newLane != null)
		{
			StartCoroutine(LerpToLane(newLane));
			return true;
		}
		return false;
	}


	private static float SmoothStepQuintic(float t)
	{
		// t^3 (10 + t(-15 + 6t))  == 6t^5 -15t^4 +10t^3
		return t * t * t * (10f + t * (-15f + 6f * t));
	}


	private IEnumerator LerpToLane(Lane targetLane)
	{
		isChanging = true;

		Lane oldLane = car.currentLane;

		// Gather lane geometry
		Vector3 oldStart = oldLane.startPosition.position;
		Vector3 oldEnd = oldLane.endPosition.position;
		Vector3 oldDir = (oldEnd - oldStart).normalized;

		// Progress along old lane (scalar)
		float oldLaneLen = Mathf.Max(Vector3.Distance(oldStart, oldEnd), 0.001f);
		float distAlongOld = Vector3.Dot(transform.position - oldStart, oldDir);
		float progress = Mathf.Clamp01(distAlongOld / oldLaneLen);

		// Target lane geometry
		Vector3 newStart = targetLane.startPosition.position;
		Vector3 newEnd = targetLane.endPosition.position;
		Vector3 newDir = (newEnd - newStart).normalized;

		// Base center position on target lane at same progress *right now*
		Vector3 baseCenterStart = Vector3.Lerp(newStart, newEnd, progress);

		// Lateral vector from that center to our current position (start lateral offset).
		Vector3 startOffset = transform.position - baseCenterStart;

		// (Usually this is mostly perpendicular to lane direction; we won’t enforce strict orthogonality,
		// but you *could* project out any forward component if you want pure lateral:
		// startOffset -= Vector3.Project(startOffset, newDir); )

		// We want to end with zero offset (centered in new lane).
		Vector3 endOffset = Vector3.zero;

		// Bookkeeping: move car to new lane *immediately* so scans / speeds adapt.
		oldLane.UnsubscribeCar(car);
		targetLane.SubscribeCar(car);
		car.currentLane = targetLane;   // <-- immediate logical switch

		// OPTIONAL: If you want instant speed reaction THIS frame (not next),
		// you could expose a public method on Car to rescan right now.
		// e.g., car.ForceImmediateRescan(); (You'd need to implement it.)

		// Rotation handling
		Quaternion startRot = transform.rotation;
		Quaternion targetRot = Quaternion.LookRotation(newDir, Vector3.up);

		// Choose rotation strategy
		bool immediateYaw = true; // set false to ease rotation

		if (immediateYaw)
			transform.rotation = targetRot;  // snap so forward movement is aligned

		float t = 0f;
		float durationNormSpeed = Mathf.Max(laneChangeSpeed, 0.0001f); // laneChangeSpeed = "normalized progress per second"

		while (t < 1f)
		{
			t += Time.deltaTime * durationNormSpeed;
			float tClamped = Mathf.Clamp01(t);

			float q = SmoothStepQuintic(tClamped); // eased lateral progress

			// After Car.Update has already advanced us forward, we recompute a fresh base center
			// at the *current* projected distance on the new lane.
			// (Projection uses our current position each frame.)
			Vector3 posNow = transform.position;
			float distAlongNew = Vector3.Dot(posNow - newStart, newDir);

			// Alternatively, to keep progress monotonic even if drift occurs,
			// we can advance distAlongNew from previous frame – but projection is fine here.
			Vector3 baseCenter = newStart + newDir * distAlongNew;

			// Lerp lateral offset toward 0
			Vector3 offset = Vector3.LerpUnclamped(startOffset, endOffset, q);

			// Rebuild final position: base lane center + eased lateral offset
			Vector3 finalPos = baseCenter + offset;

			transform.position = finalPos;

			if (!immediateYaw)
				transform.rotation = Quaternion.Slerp(startRot, targetRot, q);
			
			yield return null;
		}

		// Final snap (safety)
		// Recompute base center at final projected distance
		{
			Vector3 posNow = transform.position;
			float distAlongNew = Vector3.Dot(posNow - newStart, newDir);
			Vector3 baseCenter = newStart + newDir * distAlongNew;
			transform.position = baseCenter;  // fully centered
			transform.rotation = targetRot;
		}

		car.CompleteLaneChange(targetLane);
		isChanging = false;
	}


	#region Gizmos
	public void DrawLaneChangeGizmos()
	{
		if (!showCourtesyZone) return;

		var scan = car.LatestScan;
		bool hasRear = scan.HasCarBehind && scan.distanceBehind <= car.rearCheckDistance;
		bool cooldownReady = Time.time - lastCourtesyTime >= courtesyCooldown;

		Color zoneColor;
		bool rearCanSelfSwitch = false;
		if (hasRear)
			rearCanSelfSwitch = car.TrafficHandler != null &&
								car.TrafficHandler.FindSwitchableLane(scan.carBehind) != null;

		if (!hasRear)
			zoneColor = new Color(0f, 1f, 1f, 0.15f);          // no rear car
		else if (rearCanSelfSwitch)
			zoneColor = new Color(0f, 0.9f, 0.2f, 0.25f);       // rear will handle itself
		else
			zoneColor = cooldownReady
				? new Color(1f, 0.55f, 0f, 0.35f)               // we plan to yield
				: new Color(0.5f, 0.5f, 0.5f, 0.30f);            // blocked but cooling

		DrawCourtesyZone(car.rearCheckDistance, 2.5f, -transform.forward, zoneColor);

		if (isChanging && car.gizmoShowLaneChangeTarget)
		{
			Gizmos.color = Color.white;
			Gizmos.DrawWireSphere(transform.position + transform.forward * 0.5f, 0.3f);
		}

		if (hasRear && car.gizmoShowAheadBehindLinks)
		{
			Gizmos.color = zoneColor;
			Gizmos.DrawLine(transform.position, scan.carBehind.transform.position);
		}
	}

	private void DrawCourtesyZone(float distance, float width, Vector3 direction, Color color)
	{
#if UNITY_EDITOR
		Vector3 dir = direction.normalized;
		Vector3 right = transform.right;
		Vector3 center = transform.position + dir * (distance * 0.5f);
		float halfW = width * 0.5f;
		float halfL = distance * 0.5f;
		Vector3 v0 = center + right * halfW + dir * halfL;
		Vector3 v1 = center - right * halfW + dir * halfL;
		Vector3 v2 = center - right * halfW - dir * halfL;
		Vector3 v3 = center + right * halfW - dir * halfL;

		Color face = new Color(color.r, color.g, color.b, color.a * 0.25f);
		Color outline = new Color(color.r, color.g, color.b, Mathf.Clamp01(color.a * 0.9f));
		Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
		Handles.DrawSolidRectangleWithOutline(new Vector3[] { v0, v1, v2, v3 }, face, outline);
#else
        Vector3 center = transform.position + direction.normalized * (distance * 0.5f);
        Vector3 size   = new Vector3(width, 0.05f, distance);
        Color prev = Gizmos.color;
        Gizmos.color = color;
        Gizmos.DrawWireCube(center, size);
        Gizmos.color = prev;
#endif
	}
	#endregion
}
