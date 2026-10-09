using UnityEngine;
using UnityEngine.InputSystem;


[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float WalkSpeed = 5f;
    [SerializeField] private float RunSpeed = 10f;

    [SerializeField] private float JumpForce = 8f;
    [SerializeField] private float Gravity = 20f;

    [SerializeField] private float LookSensitivity = 0.2f;
    [SerializeField] private float LookAngleLimit = 90f;

    private Camera mainCamera;
    private CharacterController characterController;

    private InputAction moveInput;
    private InputAction runInput;

    private InputAction jumpInput;
    private bool isJumping = false;

    private float currentMoveSpeed = 0.0f;
    private Vector3 moveDirection = Vector3.zero;
    private float lookAngle = 0.0f;

    private void Start()
    {
        mainCamera = GetComponentInChildren<Camera>();
        characterController = GetComponent<CharacterController>();

        moveInput = InputSystem.actions.FindAction("Move");
        runInput = InputSystem.actions.FindAction("Sprint");

        jumpInput = InputSystem.actions.FindAction("Jump");
        jumpInput.performed += HandleJump;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        currentMoveSpeed = WalkSpeed;


    }

    private void Update()
    {
        Vector2 moveVector = moveInput.ReadValue<Vector2>();
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        if (!characterController.isGrounded)
            isJumping = false;

        currentMoveSpeed = runInput.IsPressed() ? RunSpeed : WalkSpeed;
        HandleMovement(moveVector);
        HandleLooking(mouseDelta);
    }

    private void HandleMovement(Vector2 moveVector)
    {
        Vector3 forward = transform.TransformDirection(Vector3.forward);
        Vector3 right = transform.TransformDirection(Vector3.right);

        float oldY = moveDirection.y;

        Vector2 newSpeed = new Vector2(moveVector.y * currentMoveSpeed, moveVector.x * currentMoveSpeed); 

        moveDirection = (forward * newSpeed.x) + (right * newSpeed.y);
        moveDirection.y = (isJumping && characterController.isGrounded) ? JumpForce : oldY;

        if(!characterController.isGrounded)
            moveDirection.y -= Gravity * Time.deltaTime;

        characterController.Move(moveDirection * Time.deltaTime);
        

    }

    private void HandleJump(InputAction.CallbackContext _)
    {
        isJumping = true;
    }

    private void HandleLooking(Vector2 mouseDelta)
    {
        lookAngle += mouseDelta.y * LookSensitivity;
        lookAngle = Mathf.Clamp(lookAngle, -LookAngleLimit, LookAngleLimit);

        mainCamera.transform.localRotation = Quaternion.Euler(lookAngle, 0f, 0f);
        transform.rotation *= Quaternion.Euler(0f, mouseDelta.x * LookSensitivity, 0f);

    }


}
