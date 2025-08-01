using UnityEngine;

/// <summary>
/// Destroy the player when they touch this adjustable trigger box.
/// Attach to any GameObject. Requires a BoxCollider set as trigger.
/// NOTE: For trigger events to fire, at least one collider involved must have a Rigidbody
/// (typically your Player).
/// </summary>
[RequireComponent(typeof(BoxCollider))]
[AddComponentMenu("Gameplay/Player Kill Zone")]
public class PlayerKillBox : MonoBehaviour
{
	[Header("Hitbox (Trigger Box)")]
	[Tooltip("Local-space center of the trigger box.")]
	public Vector3 boxCenter = new Vector3(0f, 1f, 0f);

	[Tooltip("Local-space size of the trigger box.")]
	public Vector3 boxSize = new Vector3(4f, 2f, 4f);

	[Header("Target Filter")]
	[Tooltip("Only objects with this tag will be destroyed.")]
	public string playerTag = "Player";

	[Tooltip("Destroy the whole player root object instead of just the touched collider's object.")]
	public bool destroyRootObject = true;

	[Header("Gizmos")]
	public bool showGizmosWhenSelectedOnly = true;
	[Range(0f, 1f)] public float gizmoFillAlpha = 0.08f;

	private BoxCollider _box;

	private void Reset()
	{
		_box = GetComponent<BoxCollider>();
		_box.isTrigger = true;
		_box.center = boxCenter;
		_box.size = boxSize;
	}

	private void OnValidate()
	{
		if (_box == null) _box = GetComponent<BoxCollider>();
		if (_box == null) return;

		_box.isTrigger = true;
		_box.center = boxCenter;
		if (boxSize.x <= 0f) boxSize.x = 0.01f;
		if (boxSize.y <= 0f) boxSize.y = 0.01f;
		if (boxSize.z <= 0f) boxSize.z = 0.01f;
		_box.size = boxSize;
	}

	private void Awake()
	{
		_box = GetComponent<BoxCollider>();
		_box.isTrigger = true;
	}

	private void OnTriggerEnter(Collider other)
	{
		if (!IsPlayer(other)) return;

		GameObject victim = destroyRootObject ? other.transform.root.gameObject : other.gameObject;
		if (victim != null)
		{
			Destroy(victim);
		}
	}

	private bool IsPlayer(Collider other)
	{
		// Allow tagging either the touched collider or the player's root
		return other.CompareTag(playerTag) || other.transform.root.CompareTag(playerTag);
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

		Color wire = new Color(1f, 0.25f, 0.25f, 1f);
		Color fill = new Color(1f, 0.25f, 0.25f, gizmoFillAlpha);

		Gizmos.color = fill;
		Gizmos.DrawCube(boxCenter, boxSize);

		Gizmos.color = wire;
		Gizmos.DrawWireCube(boxCenter, boxSize);

		Gizmos.matrix = old;
	}
	#endregion
}
