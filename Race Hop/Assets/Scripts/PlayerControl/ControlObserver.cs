using UnityEditor.ShaderGraph.Serialization;
using UnityEngine;
using UnityEngine.InputSystem;

public class ControlObserver : MonoBehaviour
{
	private PlayerControls controls;

	public Vector2 MoveDirection;
	public Vector2 LookDelta;
	public bool SprintHeld;
	private bool jumpQueued;
	public bool CrouchHeld;

	private void Awake()
	{
		controls = new PlayerControls();
	}

	void OnEnable()
	{
		controls.Gameplay.Move.performed += OnMove;
		controls.Gameplay.Move.canceled += OnMoveCanceled;

		controls.Gameplay.Look.performed += OnLook;
		controls.Gameplay.Look.canceled += OnLookCanceled;

		controls.Gameplay.Sprint.performed += OnSprintPerformed;
		controls.Gameplay.Sprint.canceled += OnSprintCanceled;

		controls.Gameplay.Jump.performed += _ => jumpQueued = true;

		controls.Gameplay.Crouch.performed += OnCrouchPerformed;
		controls.Gameplay.Crouch.canceled += OnCrouchCanceled;

		controls.Enable();
	}

	private void OnDisable()
	{
		controls.Gameplay.Move.performed -= OnMove;
		controls.Gameplay.Move.canceled -= OnMoveCanceled;

		controls.Gameplay.Look.performed -= OnLook;
		controls.Gameplay.Look.canceled -= OnLookCanceled;

		controls.Gameplay.Sprint.performed -= OnSprintPerformed;
		controls.Gameplay.Sprint.canceled -= OnSprintCanceled;

		controls.Gameplay.Jump.performed -= _ => jumpQueued = true;

		controls.Gameplay.Crouch.performed -= OnCrouchPerformed;
		controls.Gameplay.Crouch.canceled -= OnCrouchCanceled;

		controls.Disable();
	}

	public void OnMove(InputAction.CallbackContext ctx)
	{
		MoveDirection = ctx.ReadValue<Vector2>();

		Debug.Log(JsonUtility.ToJson(MoveDirection));
	}

	public void OnMoveCanceled(InputAction.CallbackContext ctx)
	{
		MoveDirection = Vector2.zero;
	}

	private void OnLook(InputAction.CallbackContext ctx) => LookDelta = ctx.ReadValue<Vector2>();
	private void OnLookCanceled(InputAction.CallbackContext _) => LookDelta = Vector2.zero;

	private void OnSprintPerformed(InputAction.CallbackContext _)
	{
		Debug.Log("sprint!");
		SprintHeld = true;
	}
	private void OnSprintCanceled(InputAction.CallbackContext _) => SprintHeld = false;

	private void OnCrouchPerformed(InputAction.CallbackContext ctx)
	{
		CrouchHeld = true;
	}

	private void OnCrouchCanceled(InputAction.CallbackContext ctx)
	{
		CrouchHeld = false;
	}

	public bool ConsumeJump()
	{
		if (jumpQueued)
		{
			jumpQueued = false;
			return true;
		}
		return false;
	}

}
