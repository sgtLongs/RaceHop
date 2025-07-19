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
	public bool smoothingEnabled = false;
	[Tooltip("Seconds (tau) to reach ~63% of a look delta; smaller = snappier.")]
	public float smoothingTau = 0.05f;

	[Header("Inversion")]
	public bool invertY = false;

	[Header("Cursor")]
	public bool lockCursorOnStart = true;
	public bool hideCursor = true;

	[Header("Zoom / FOV (Optional)")]
	public bool enableZoom = false;
	public float zoomStep = 5f;
	public float minFov = 50f;
	public float maxFov = 90f;
	public float fovLerpSpeed = 20f;

	private float yaw;    // Accumulated yaw in degrees
	private float pitch;  // Accumulated pitch in degrees
	private Camera cam;

	// For smoothing
	private Vector2 smoothedLook;
	private Vector2 lookVelocity; // not "velocity" in physics sense, just tracking

	void Awake()
	{
		if (!controlObserver)
		{
			controlObserver = GetComponentInParent<ControlObserver>();
			if (!controlObserver)
				controlObserver = FindObjectOfType<ControlObserver>();
		}

		if (!playerYawRoot)
		{
			if (transform.parent != null)
				playerYawRoot = transform.parent;
			else
				playerYawRoot = transform; // fallback
		}

		cam = GetComponent<Camera>();
	}

	void Start()
	{
		// Initialize yaw/pitch from starting orientation
		if (playerYawRoot)
			yaw = playerYawRoot.eulerAngles.y;

		Vector3 localEuler = transform.localEulerAngles;
		// Convert to signed pitch
		pitch = localEuler.x;
		if (pitch > 180f) pitch -= 360f;
		pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

		if (lockCursorOnStart)
		{
			Cursor.lockState = CursorLockMode.Locked;
			Cursor.visible = !hideCursor;
		}
	}

	void Update()
	{
		if (!controlObserver) return;

		Vector2 rawLook = controlObserver.LookDelta;

		// Distinguish mouse vs controller:
		bool isStick = IsGamepadRightStick(rawLook);

		Vector2 processed = rawLook;

		if (isStick)
		{
			// Stick gives a normalized vector (-1..1). Convert to deg/frame:
			// degreesPerSecond * deltaTime * stickValue
			processed.x = rawLook.x * controllerYawSpeed * Time.deltaTime;
			processed.y = rawLook.y * controllerPitchSpeed * Time.deltaTime;
		}
		else
		{
			// Mouse delta (already per frame). Apply sensitivity
			processed *= mouseSensitivity;
		}

		if (invertY) processed.y = -processed.y;

		// Optional smoothing (exponential toward processed)
		if (smoothingEnabled)
		{
			float dt = Time.deltaTime;
			float alpha = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, smoothingTau));
			smoothedLook += (processed - smoothedLook) * alpha;
			processed = smoothedLook;
		}

		// Apply
		yaw += processed.x;
		pitch -= processed.y; // subtract because up mouse = look up (screen coords vs world)
		pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

		// Set rotations
		if (playerYawRoot)
		{
			playerYawRoot.rotation = Quaternion.Euler(0f, yaw, 0f);
		}

		transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
	}

	bool IsGamepadRightStick(Vector2 v)
	{
		// Heuristic: mouse delta can be large/spiky; stick usually within [-1,1]
		return Mathf.Abs(v.x) <= 1f && Mathf.Abs(v.y) <= 1f &&
			   Gamepad.current != null && Gamepad.current.rightStick.IsActuated();
	}

	// Utility to toggle cursor at runtime (bind to a UI key if desired)
	public void ToggleCursorLock()
	{
		if (Cursor.lockState == CursorLockMode.Locked)
		{
			Cursor.lockState = CursorLockMode.None;
			Cursor.visible = true;
		}
		else
		{
			Cursor.lockState = CursorLockMode.Locked;
			Cursor.visible = !hideCursor;
		}
	}
}
