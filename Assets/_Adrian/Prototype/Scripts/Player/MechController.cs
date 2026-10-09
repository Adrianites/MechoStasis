using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.UI;

public class MechController : MonoBehaviour, IDamageable
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float airControlMultiplier = 0.5f;
    [SerializeField] private float sprintMultiplier = 1.5f;

    [Header("Dash")]
    [SerializeField] private float dashSpeed = 10f;
    [SerializeField] private float dashDuration = 0.15f;
    [SerializeField] private float dashCooldown = 0.75f;
    [SerializeField] private Slider dashCooldownSlider;

    [Header("Jump")]
    [SerializeField] private float jumpForce = 5f;
    [SerializeField] private float gravityMultiplier = 1f;
    [SerializeField] private float coyoteTime = 0.1f;

    [Header("Camera")]
    [SerializeField] private float mouseSensitivity = 20f;
    [SerializeField] private float upDownLookRange = 80f;
    [SerializeField] private float cameraReturnSpeed = 8f;

    [Header("Cockpit Motion")]
    [SerializeField] private float cockpitPositionSway = 0.02f;
    [SerializeField] private float cockpitRotationSway = 1.25f;
    [SerializeField] private float cockpitBobAmount = 0.01f;
    [SerializeField] private float cockpitBobFrequency = 8f;
    [SerializeField] private float cockpitMotionSmooth = 10f;
    [SerializeField] private bool CockpitRotate = false;

    [Header("Health")]
    [SerializeField] private int health = 10;
    [SerializeField] private bool isDead = false;
    [SerializeField] private Slider healthSlider;

    [Header("References")]
    [SerializeField] private CharacterController characterController;
    [SerializeField] private CinemachineCamera mainCam;
    [SerializeField] private Transform ogCockpitCamRotTransform;
    [SerializeField] private CinemachineCamera cockpitCam;
    [SerializeField] private InputHandler playerInputHandler;
    [SerializeField] private GameObject deathCanvas;
    [SerializeField] private GameObject healthCanvas;


    private CinemachineImpulseListener cockpitImpulseListener;
    private Quaternion mainCamRestRotation;
    private Quaternion cockpitCamRestRotation;
    private Vector3 cockpitCamRestPosition;
    private float mainCameraPitch;
    private float cockpitCameraPitch;
    private float cockpitCameraYaw;
    private Vector3 cockpitCameraPositionOffset;
    private Vector3 cockpitCameraRotationOffset;

    private Vector3 currentMovement;
    private float coyoteTimeCounter;
    private bool jumpRequested;
    private bool dashRequested;
    private float dashTimer;
    private float dashCooldownTimer;
    private Vector3 dashDirection;

    private Vector3 previousHorizontalVelocity;
    private float movementImpulseTimer;
    private float previousVerticalVelocity;
    private bool wasGrounded;

    private void Start()
    {
        if (healthSlider != null)
        {
            healthSlider.maxValue = health;
            healthSlider.value = health;
        }

        if (dashCooldownSlider != null)
        {
            dashCooldownSlider.maxValue = dashCooldown;
            dashCooldownSlider.value = dashCooldown;
        }

        dashCooldownTimer = dashCooldown;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        wasGrounded = characterController != null && characterController.isGrounded;
        previousVerticalVelocity = currentMovement.y;

        if (cockpitCam != null)
            cockpitImpulseListener = cockpitCam.GetComponent<CinemachineImpulseListener>();

        if (mainCam != null)
        {
            mainCamRestRotation = mainCam.transform.localRotation;
            mainCameraPitch = NormalizePitch(mainCamRestRotation.eulerAngles.x);
        }

        if (cockpitCam != null)
        {
            if (ogCockpitCamRotTransform != null)
            {
                cockpitCam.transform.rotation = ogCockpitCamRotTransform.rotation;
            }
            
            cockpitCamRestRotation = cockpitCam.transform.localRotation;
            cockpitCamRestPosition = cockpitCam.transform.localPosition;
            cockpitCameraPitch = NormalizePitch(cockpitCamRestRotation.eulerAngles.x);
            cockpitCameraYaw = NormalizePitch(cockpitCamRestRotation.eulerAngles.y);
        }
    }

    public void TakeDamage(int damage)
    {
        health -= damage;

        if (healthSlider != null)
        {
            healthSlider.value = health;
        }

        if (health <= 0 && !isDead)
        {
            healthSlider.value = 0;
            isDead = true;
            deathCanvas.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void Update()
    {
        if (characterController == null || playerInputHandler == null)
            return;
        if (isDead)
            return;

        HandleMovement();
        HandleRotation();
    }

    private void HandleMovement()
    {
        dashCooldownTimer = Mathf.Min(dashCooldown, dashCooldownTimer + Time.deltaTime);

        if (dashCooldownSlider != null)
        {
            dashCooldownSlider.value = dashCooldownTimer;
        }

        bool groundedBeforeMove = characterController.isGrounded;

        Vector2 input = playerInputHandler.MovementInput;
        bool sprinting = playerInputHandler.SprintTriggered;

        Vector3 inputDirection = new Vector3(input.x, 0f, input.y);
        Vector3 worldDirection = transform.TransformDirection(inputDirection);

        if (worldDirection.sqrMagnitude > 1f)
            worldDirection.Normalize();

        Vector3 horizontalVelocity;
        float effectiveMoveSpeed = moveSpeed * (sprinting ? sprintMultiplier : 1f);

        if (dashRequested && dashCooldownTimer >= dashCooldown)
        {
            dashRequested = false;
            dashTimer = dashDuration;
            dashCooldownTimer = 0f;

            dashDirection = worldDirection.sqrMagnitude > 0.001f
                ? worldDirection.normalized
                : transform.forward;
        }

        if (dashTimer > 0f)
        {
            dashTimer -= Time.deltaTime;
            horizontalVelocity = dashDirection * dashSpeed;
        }
        else if (groundedBeforeMove)
        {
            coyoteTimeCounter = coyoteTime;
            horizontalVelocity = worldDirection * effectiveMoveSpeed;

            if (currentMovement.y < 0f)
                currentMovement.y = -0.5f;
        }
        else
        {
            coyoteTimeCounter -= Time.deltaTime;

            if (worldDirection.sqrMagnitude > 0.001f)
            {
                horizontalVelocity =
                    worldDirection * effectiveMoveSpeed * airControlMultiplier;
            }
            else
            {
                horizontalVelocity = new Vector3(
                    currentMovement.x,
                    0f,
                    currentMovement.z
                );
            }

            currentMovement.y +=
                Physics.gravity.y * gravityMultiplier * Time.deltaTime;
        }

        currentMovement.x = horizontalVelocity.x;
        currentMovement.z = horizontalVelocity.z;

        if (jumpRequested && coyoteTimeCounter > 0f)
        {
            currentMovement.y = jumpForce;
            coyoteTimeCounter = 0f;
            jumpRequested = false;
        }

        Vector3 currentHorizontalVelocity =
            new Vector3(currentMovement.x, 0f, currentMovement.z);

        float fallingSpeed = Mathf.Max(0f, -currentMovement.y);

        characterController.Move(currentMovement * Time.deltaTime);

        bool groundedAfterMove = characterController.isGrounded;

        previousVerticalVelocity = currentMovement.y;
        wasGrounded = groundedAfterMove;
        previousHorizontalVelocity = currentHorizontalVelocity;
    }



    private void HandleRotation()
    {
        bool cockpitLookHeld = playerInputHandler.UnlockCockpitCamTriggered;
        CockpitRotate = cockpitLookHeld;

        Vector2 lookInput = playerInputHandler.RotationInput;

        float mouseX = lookInput.x * mouseSensitivity * Time.deltaTime;

        float mouseY = lookInput.y * mouseSensitivity * Time.deltaTime;

        if (CockpitRotate)
        {
            cockpitCameraYaw += mouseX;
            cockpitCameraPitch = Mathf.Clamp
            (cockpitCameraPitch - mouseY, -upDownLookRange, upDownLookRange);

            if (mainCam != null)
            {
                mainCam.transform.localRotation = mainCamRestRotation;
            }
        }
        else
        {
            transform.Rotate(Vector3.up * mouseX);

            mainCameraPitch = Mathf.Clamp(
                mainCameraPitch - mouseY,
                -upDownLookRange,
                upDownLookRange
            );

            cockpitCameraYaw = Mathf.Lerp(cockpitCameraYaw, 0f, cameraReturnSpeed * Time.deltaTime);
            cockpitCameraPitch = Mathf.Lerp(cockpitCameraPitch, 0f, cameraReturnSpeed * Time.deltaTime);

            if (mainCam != null)
            {
                Quaternion targetRotation = mainCamRestRotation * Quaternion.Euler(mainCameraPitch, 0f, 0f);
                mainCam.transform.localRotation = Quaternion.Slerp(
                    mainCam.transform.localRotation,
                    targetRotation,
                    cameraReturnSpeed * Time.deltaTime
                );
            }
        }
        
        if (cockpitCam != null)
        {
            UpdateCockpitMotion();

            Quaternion cockpitLookRotation = cockpitCamRestRotation * Quaternion.Euler(cockpitCameraPitch, cockpitCameraYaw, 0f);
            Quaternion cockpitSwayRotation = Quaternion.Euler(cockpitCameraRotationOffset);

            cockpitCam.transform.localRotation = cockpitLookRotation * cockpitSwayRotation;
            cockpitCam.transform.localPosition = cockpitCamRestPosition + cockpitCameraPositionOffset;
        }
    }

    private void UpdateCockpitMotion()
    {
        if (cockpitCam == null)
        {
            return;
        }

        Vector3 localVelocity = transform.InverseTransformDirection(new Vector3(currentMovement.x, 0f, currentMovement.z));
        float movePercent = Mathf.Clamp01(localVelocity.magnitude / Mathf.Max(0.01f, moveSpeed));

        Vector3 targetPositionOffset = new Vector3(
            -localVelocity.x * cockpitPositionSway,
            Mathf.Sin(Time.time * cockpitBobFrequency) * cockpitBobAmount * movePercent,
            -localVelocity.z * cockpitPositionSway
        );

        Vector3 targetRotationOffset = new Vector3(
            localVelocity.z * cockpitRotationSway,
            0f,
            -localVelocity.x * cockpitRotationSway
        );

        float smoothFactor = cockpitMotionSmooth * Time.deltaTime;
        cockpitCameraPositionOffset = Vector3.Lerp(cockpitCameraPositionOffset, targetPositionOffset, smoothFactor);
        cockpitCameraRotationOffset = Vector3.Lerp(cockpitCameraRotationOffset, targetRotationOffset, smoothFactor);
    }

    private float NormalizePitch(float pitch)
    {
        return Mathf.DeltaAngle(0f, pitch);
    }

    private void OnEnable()
    {
        if (playerInputHandler == null)
            return;

        playerInputHandler.OnJumpPerformed += OnJumpInput;
        playerInputHandler.OnJumpCanceled += OnJumpInputCanceled;
        playerInputHandler.OnDashPerformed += OnDashInput;
    }

    private void OnDisable()
    {
        if (playerInputHandler == null)
            return;

        playerInputHandler.OnJumpPerformed -= OnJumpInput;
        playerInputHandler.OnJumpCanceled -= OnJumpInputCanceled;
        playerInputHandler.OnDashPerformed -= OnDashInput;
    }

    private void OnJumpInput()
    {
        jumpRequested = true;
    }

    private void OnJumpInputCanceled()
    {
        jumpRequested = false;
    }

    private void OnDashInput()
    {
        dashRequested = true;
    }
}
