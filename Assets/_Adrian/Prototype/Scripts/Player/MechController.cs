using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.UI;
using System.Collections;

public class MechController : MonoBehaviour, IDamageable
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float airControlMultiplier = 0.5f;
    [SerializeField] private float sprintMultiplier = 1.5f;
    [SerializeField] private Vector3 currentMovement;

    [Header("Dash")]
    [SerializeField] private float dashSpeed = 10f;
    [SerializeField] private float dashDuration = 0.15f;
    [SerializeField] private float dashCooldown = 0.75f;
    [SerializeField] private bool dashRequested;
    [SerializeField] private float dashTimer;
    [SerializeField] private float dashCooldownTimer;
    [SerializeField] private Vector3 dashDirection;
    [SerializeField] private Slider dashCooldownSlider;

    [Header("Jump")]
    [SerializeField] private float jumpForce = 5f;
    [SerializeField] private float gravityMultiplier = 1f;
    [SerializeField] private float coyoteTime = 0.1f;
    [SerializeField] private float coyoteTimeCounter;
    [SerializeField] private bool jumpRequested;

    [Header("Camera")]
    [SerializeField] private float mouseSensitivity = 20f;
    [SerializeField] private float upDownLookRange = 80f;
    [SerializeField] private float cockpitYawRange = 60f;
    [SerializeField] private float cockpitPitchRange = 60f;
    [SerializeField] private float cameraReturnSpeed = 8f;

    [Header("Arms")]
    [SerializeField] private Transform armsRoot;
    [SerializeField] private float armsFollowSpeed = 12f;
    [SerializeField, Range(0f, 1f)] private float armsFollowWeight = 1f;

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
    [SerializeField] public bool isInvincible = false;
    [SerializeField] private Slider healthSlider;
    [SerializeField] private GameObject hitEffect;

    [Header("Audio")]
    [SerializeField] private AudioSource FootstepAudioSource;
    [SerializeField] private AudioSource DashAudioSource;
    [SerializeField] private AudioSource JumpAudioSource;
    [SerializeField] private AudioSource HitAudioSource;

    [Header("References")]
    [SerializeField] private CharacterController characterController;
    [SerializeField] private CinemachineCamera mainCam;
    [SerializeField] private Transform ogCockpitCamRotTransform;
    [SerializeField] private CinemachineCamera cockpitCam;
    [SerializeField] private InputHandler playerInputHandler;
    [SerializeField] private GameObject deathCanvas;
    [SerializeField] private GameObject healthCanvas;
    [SerializeField] private Animator[] animators;

    private CinemachineImpulseListener cockpitImpulseListener;
    private Quaternion mainCamRestRotation;
    private Quaternion armsRestRotation;
    private Quaternion cockpitCamRestRotation;
    private Vector3 cockpitCamRestPosition;
    private float mainCameraPitch;
    private float cockpitCameraPitch;
    private float cockpitCameraYaw;
    private Vector3 cockpitCameraPositionOffset;
    private Vector3 cockpitCameraRotationOffset;

    private Vector3 previousHorizontalVelocity;
    private float movementImpulseTimer;
    private float previousVerticalVelocity;
    private bool wasGrounded;

    private void Awake()
    {
        if (deathCanvas != null)
        {
            deathCanvas.SetActive(false);
        }
    }

    private void Start()
    {   
        if (hitEffect != null)
        {
            hitEffect.SetActive(false);
        }
        
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

        if (armsRoot != null)
            armsRestRotation = armsRoot.localRotation;

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
        if (isInvincible || isDead)
            return;

        health -= damage;
        StartCoroutine(IHitEffect());
        HitAudioSource?.Play();

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

    private IEnumerator IHitEffect()
    {
        hitEffect.SetActive(true);
        yield return new WaitForSeconds(0.5f);
        hitEffect.SetActive(false);
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
        //animators[0].SetBool(AnimStrings.IsWalking, false);
        //animators[1].SetBool(AnimStrings.IsWalking, false);
        
        dashCooldownTimer = Mathf.Min(dashCooldown, dashCooldownTimer + Time.deltaTime);

        if (dashCooldownSlider != null)
        {
            dashCooldownSlider.value = dashCooldownTimer;
        }

        bool groundedBeforeMove = characterController.isGrounded;
        //have to change this because its messing up the idle animation
        //animators[0].SetBool(AnimStrings.IsWalking, groundedBeforeMove); // right hand animator
        //animators[1].SetBool(AnimStrings.IsWalking, groundedBeforeMove); // left hand animator

        Vector2 input = playerInputHandler.MovementInput;
        bool sprinting = playerInputHandler.SprintTriggered;

        Vector3 inputDirection = new Vector3(input.x, 0f, input.y);
        Vector3 worldDirection = transform.TransformDirection(inputDirection);

        if (worldDirection.sqrMagnitude > 1f)
            worldDirection.Normalize();

        Vector3 horizontalVelocity;
        float effectiveMoveSpeed = moveSpeed * (sprinting ? sprintMultiplier : 1f);

        if (dashRequested)
        {
            dashRequested = false;

            if (dashCooldownTimer >= dashCooldown)
            {
                dashTimer = dashDuration;
                dashCooldownTimer = 0f;

                dashDirection = worldDirection.sqrMagnitude > 0.001f
                    ? worldDirection.normalized
                    : transform.forward;
            }
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
            animators[0].SetTrigger(AnimStrings.Jump); // right hand animator
            animators[1].SetTrigger(AnimStrings.Jump); // left hand animator
            JumpAudioSource?.Play();
            currentMovement.y = jumpForce;
            coyoteTimeCounter = 0f;
            jumpRequested = false;
        }

        Vector3 currentHorizontalVelocity =
            new Vector3(currentMovement.x, 0f, currentMovement.z);

        float fallingSpeed = Mathf.Max(0f, -currentMovement.y);

        characterController.Move(currentMovement * Time.deltaTime);
        //better place to put the anim strings but still has a massive delay
        animators[0].SetBool(AnimStrings.IsWalking, currentHorizontalVelocity.sqrMagnitude > 0.1f && groundedBeforeMove); // right hand animator
        animators[1].SetBool(AnimStrings.IsWalking, currentHorizontalVelocity.sqrMagnitude > 0.1f && groundedBeforeMove); // left hand animator

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
            cockpitCameraYaw = Mathf.Clamp
            (cockpitCameraYaw + mouseX, -cockpitYawRange, cockpitYawRange);
            cockpitCameraPitch = Mathf.Clamp
            (cockpitCameraPitch - mouseY, -cockpitPitchRange, cockpitPitchRange);

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

        UpdateArmsRotation();
    }

    private void UpdateArmsRotation()
    {
        if (armsRoot == null || mainCam == null)
        {
            return;
        }

        Quaternion cameraLookOffset = mainCam.transform.localRotation * Quaternion.Inverse(mainCamRestRotation);

        if (armsFollowWeight < 1f)
        {
            cameraLookOffset = Quaternion.Slerp(Quaternion.identity, cameraLookOffset, armsFollowWeight);
        }

        Quaternion targetRotation = cameraLookOffset * armsRestRotation;

        armsRoot.localRotation = armsFollowSpeed > 0f
            ? Quaternion.Slerp(armsRoot.localRotation, targetRotation, armsFollowSpeed * Time.deltaTime)
            : targetRotation;
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
