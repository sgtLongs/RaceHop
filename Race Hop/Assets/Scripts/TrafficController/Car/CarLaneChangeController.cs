using UnityEngine;
using System.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[RequireComponent(typeof(Car))]
[RequireComponent(typeof(Rigidbody))]
public class CarLaneChangeController : MonoBehaviour
{
	public float laneChangeSpeed = 5f;      // normalised 0‑1 per second
	public float courtesyCooldown = 2f;
	public bool showCourtesyZone = true;

	public float lastCourtesyTime;

	private Car car;
	private CarSpeedController speedController;
	private Rigidbody rb;
	private bool isChanging;

	private float BaseSpeed;

	void Awake()
	{
		car = GetComponent<Car>();
		speedController = GetComponent<CarSpeedController>();
		rb = GetComponent<Rigidbody>();
	}

	public void HandleLaneChange(Car.CarScanResult scan)
	{
		if (isChanging || car.currentLane == null || car.TrafficHandler == null) return;

		BaseSpeed = car.BaseSpeed;

		// 1. stuck‑behind‑car
		if (car.moveForward && scan.HasCarAhead && scan.distanceAhead < car.checkAheadDistance)
			TrySwitchLane();

		// 2. courtesy yield
		if (scan.HasCarBehind &&
			scan.distanceBehind <= car.rearCheckDistance &&
			Time.time - lastCourtesyTime >= courtesyCooldown)
		{
			if (car.TrafficHandler.FindSwitchableLane(scan.carBehind) == null && TrySwitchLane())
				lastCourtesyTime = Time.time;
		}
	}

	bool TrySwitchLane()
	{
		Lane target = car.TrafficHandler.SwitchCarLane(car);
		if (target == null) return false;
		StartCoroutine(LateralLerp(target));
		return true;
	}

	/* quintic smootherstep */
	private static float Ease(float t) => t * t * t * (10f + t * (-15f + 6f * t));

	IEnumerator LateralLerp(Lane targetLane)
	{
		isChanging = true;

		// Un‑/re‑register for scans immediately.
		Lane oldLane = car.currentLane;
		oldLane.UnsubscribeCar(car);
		targetLane.SubscribeCar(car);
		car.currentLane = targetLane;

		/* geometry */
		Vector3 newDir = (targetLane.endPosition.position - targetLane.startPosition.position).normalized;
		Quaternion targetRot = Quaternion.LookRotation(newDir, Vector3.up);
		rb.rotation = targetRot;                    // snap yaw

		/* determine initial lateral offset */
		float progress = Vector3.Dot(rb.position - targetLane.startPosition.position, newDir) /
						 Mathf.Max(Vector3.Distance(targetLane.startPosition.position, targetLane.endPosition.position), 0.001f);
		Vector3 laneCenter = Vector3.Lerp(targetLane.startPosition.position, targetLane.endPosition.position, progress);
		Vector3 startOffset = rb.position - laneCenter;

		float t = 0f; float speedNorm = Mathf.Max(laneChangeSpeed, 0.0001f);

		while (t < 1f)
		{
			t += Time.deltaTime * speedNorm;
			float q = Ease(Mathf.Clamp01(t));

			float dist = Vector3.Dot(rb.position - targetLane.startPosition.position, newDir);
			laneCenter = targetLane.startPosition.position + newDir * dist;

			Vector3 offset = Vector3.LerpUnclamped(startOffset, Vector3.zero, q);

			rb.position = laneCenter + offset;

			yield return null;
		}

		// final center snap
		float finalDist = Vector3.Dot(rb.position - targetLane.startPosition.position, newDir);
		rb.position = targetLane.startPosition.position + newDir * finalDist;
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
			zoneColor = new Color(0f, 1f, 1f, 0.15f);
		else if (rearCanSelfSwitch)
			zoneColor = new Color(0f, 0.9f, 0.2f, 0.25f);
		else
			zoneColor = cooldownReady
				? new Color(1f, 0.55f, 0f, 0.35f)
				: new Color(0.5f, 0.5f, 0.5f, 0.30f);

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
