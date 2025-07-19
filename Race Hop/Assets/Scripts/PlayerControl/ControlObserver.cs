using UnityEditor.ShaderGraph.Serialization;
using UnityEngine;
using UnityEngine.InputSystem;

public class ControlObserver : MonoBehaviour
{
	private PlayerControls controls;

	public Vector2 MoveDirection;

	private void Awake()
	{
		controls = new PlayerControls();
	}

	void OnEnable()
	{
		controls.Gameplay.Move.performed += OnMove;
		controls.Gameplay.Move.canceled += OnMoveCanceled;

		controls.Enable();
	}

	private void OnDisable()
	{
		controls.Gameplay.Move.performed -= OnMove;
		controls.Gameplay.Move.canceled -= OnMoveCanceled;

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
}
