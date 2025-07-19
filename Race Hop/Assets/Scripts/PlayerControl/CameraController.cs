using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
	[Header("References")]
	[Tooltip("The transform whose Y (yaw) we rotate (usually the player root or body). If null, will use transform.parent.")]
	public Transform playerYawRoot;

	[Tooltip("Observer supplying Move/Look input.")]
	public ControlObserver controlObserver;

	[Header("Sensitivity")]
	[Tooltip("Mouse sensitivity multiplier (applied to pointer delta).")]
	public float mouseSensitivity = 2.5f;
	[Tooltip("Controller stick degrees per second at full deflection.")]
	public float controllerYawSpeed = 220f;
	public float controllerPitchSpeed = 180f;

	[Header("Pitch Clamp")]
	public float minPitch = -85f;
	public float maxPitch = 85f;

	[Header("Smoothing (Optional)")]
	[Tooltip("Enable exponential smoothing of look input.")]
	public bool smoothingEnabled = true;
	[Tooltip("Seconds (tau) to reach ~63% of a look delta; smaller = snappier.")]
	public float smoothingTau = 0.05f;

	[Header("Inversion")]
	public bool invertY = false;

	private float yaw;
	private float pitch;
	private Camera cam;

	// For smoothing
	private Vector2 smoothedLook;

	void Awake()
	{
		if (!controlObserver)
		{
			controlObserver = GetComponentInParent<ControlObserver>();
			if (!controlObserver)
				controlObserver = FindFirstObjectByType<ControlObserver>();
		}

		if (!playerYawRoot)
		{
			if (transform.parent != null)
			{
				playerYawRoot = transform.parent;
			}
			else
			{
				playerYawRoot = transform;
			}	
		}

		cam = GetComponent<Camera>();
	}

	void Start()
	{
		if (playerYawRoot)
			yaw = playerYawRoot.eulerAngles.y;

		Vector3 localEuler = transform.localEulerAngles;

		pitch = localEuler.x;
		if (pitch > 180f) pitch -= 360f;
		pitch = Mathf.Clamp(pitch, minPitch, maxPitch);


		Cursor.lockState = CursorLockMode.Locked;
		Cursor.visible = false;
	}

	void Update()
	{
		if (!controlObserver) return;

		Vector2 rawLook = controlObserver.LookDelta;

		Vector2 processed = rawLook * mouseSensitivity;

		if (invertY) processed.y = -processed.y;

		if (smoothingEnabled)
		{
			processed = ApplySmoothingToMouseInput(processed);
		}

		ApplyRotationToCamera(processed);
	}

	private void ApplyRotationToCamera(Vector2 processed)
	{
		yaw += processed.x;
		pitch -= processed.y;
		pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

		if (playerYawRoot)
		{
			playerYawRoot.rotation = Quaternion.Euler(0f, yaw, 0f);
		}

		transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
	}

	private Vector2 ApplySmoothingToMouseInput(Vector2 processed)
	{
		float dt = Time.deltaTime;
		float alpha = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, smoothingTau));
		smoothedLook += (processed - smoothedLook) * alpha;
		return smoothedLook;
	}
}
