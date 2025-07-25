using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to any GameObject. Creates a thin trigger "plane" above this object.
/// While a collider tagged `playerTag` is inside the plane, applies a continuous force
/// to its Rigidbody each FixedUpdate. Includes gizmos to visualize the hitbox.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class ForcePlaneTrigger : MonoBehaviour
{
	[Header("Detection (Trigger Plane)")]
	[Tooltip("Vertical offset of the plane above this object (local space).")]
	public float height = 2f;

	[Tooltip("Size of the plane (X/Z = width/depth). Y is thickness of the trigger volume.")]
	public Vector3 planeSize = new Vector3(4f, 0.2f, 4f);

	[Header("Target Filter")]
	[Tooltip("Only objects with this tag will be affected.")]
	public string playerTag = "Player";

	[Header("Force Settings")]
	[Tooltip("Direction of the force to apply. If 'Use Local Space' is on, this is in local space; otherwise world space.")]
	public Vector3 forceDirection = Vector3.down;

	[Tooltip("How strong the force is (tune based on ForceMode).")]
	public float forceMagnitude = 150f;

	[Tooltip("Force mode used when applying the force.\n" +
			 "Force: mass-aware continuous push\n" +
			 "Acceleration: mass-agnostic continuous push\n" +
			 "Impulse/VelocityChange: per-frame bursts (stronger)")]
	public ForceMode forceMode = ForceMode.Force;

	[Tooltip("Interpret 'forceDirection' in this object's local space.")]
	public bool useLocalSpaceDirection = true;

	[Header("Gizmos")]
	public bool showGizmosWhenSelectedOnly = true;
	[Range(0f, 1f)] public float gizmoFillAlpha = 0.08f;

	private BoxCollider _box;

	// Bodies detected this frame; used to apply force once per RB per FixedUpdate
	private readonly HashSet<Rigidbody> _bodiesThisStep = new HashSet<Rigidbody>();

	private void Reset()
	{
		_box = GetComponent<BoxCollider>();
		_box.isTrigger = true;
		if (planeSize.y <= 0f) planeSize.y = 0.2f;
		_box.size = planeSize;
		_box.center = new Vector3(0f, height, 0f);
	}

	private void OnValidate()
	{
		if (_box == null) _box = GetComponent<BoxCollider>();
		if (_box == null) return;

		_box.isTrigger = true;
		if (planeSize.y <= 0.0001f) planeSize.y = 0.2f;
		_box.size = planeSize;
		_box.center = new Vector3(0f, height, 0f);
	}

	private void Awake()
	{
		_box = GetComponent<BoxCollider>();
		_box.isTrigger = true;
	}

	private void OnTriggerStay(Collider other)
	{
		if (!other.CompareTag(playerTag)) return;

		// Find a Rigidbody to push
		Rigidbody rb = other.attachedRigidbody;
		if (rb == null) rb = other.GetComponentInParent<Rigidbody>();
		if (rb == null) return;

		_bodiesThisStep.Add(rb);
	}

	private void FixedUpdate()
	{
		if (_bodiesThisStep.Count == 0) return;

		Vector3 dir = forceDirection.sqrMagnitude > 0f
			? forceDirection.normalized
			: Vector3.down;

		if (useLocalSpaceDirection)
			dir = transform.TransformDirection(dir);

		Vector3 force = dir * forceMagnitude;

		foreach (var rb in _bodiesThisStep)
		{
			if (rb != null)
				rb.AddForce(force, forceMode);
		}

		_bodiesThisStep.Clear();
	}

	#region Gizmos
	private void OnDrawGizmos()
	{
		if (showGizmosWhenSelectedOnly) return;
		DrawGizmosInternal();
	}

	private void OnDrawGizmosSelected()
	{
		if (!showGizmosWhenSelectedOnly) return;
		DrawGizmosInternal();
	}

	private void DrawGizmosInternal()
	{
		if (_box == null) _box = GetComponent<BoxCollider>();
		if (_box == null) return;

		Matrix4x4 old = Gizmos.matrix;
		Gizmos.matrix = transform.localToWorldMatrix;

		Color wire = new Color(0.1f, 0.7f, 1f, 1f);
		Color fill = new Color(0.1f, 0.7f, 1f, gizmoFillAlpha);

		Vector3 center = new Vector3(0f, height, 0f);
		Vector3 size = planeSize;

		Gizmos.color = fill;
		Gizmos.DrawCube(center, size);

		Gizmos.color = wire;
		Gizmos.DrawWireCube(center, size);

		// Force direction arrow
		Vector3 dirLocal = useLocalSpaceDirection
			? (forceDirection.sqrMagnitude > 0f ? forceDirection.normalized : Vector3.down)
			: transform.InverseTransformDirection(
				forceDirection.sqrMagnitude > 0f ? forceDirection.normalized : Vector3.down);

		float arrowLen = Mathf.Clamp(size.magnitude * 0.35f, 0.5f, 5f);
		Vector3 start = center;
		Vector3 end = center + dirLocal * arrowLen;

		Gizmos.DrawLine(start, end);
		Vector3 right = Quaternion.AngleAxis(25f, Vector3.up) * (-dirLocal);
		Vector3 left = Quaternion.AngleAxis(-25f, Vector3.up) * (-dirLocal);
		Gizmos.DrawLine(end, end + right * (arrowLen * 0.15f));
		Gizmos.DrawLine(end, end + left * (arrowLen * 0.15f));

		Gizmos.matrix = old;
	}
	#endregion
}
