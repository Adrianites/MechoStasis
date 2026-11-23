using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.UI;
using System.Collections;
using Unity.Collections;

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
    [SerializeField] private AudioClip[] footstepClips;
    [SerializeField] private float footstepInterval = 0.6f;
    [SerializeField] private Vector2 footstepPitchRange = new Vector2(0.9f, 1.1f);
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
    private float footstepTimer;

    Rigidbody rb;
    private Collider bodyCollider;

    [SerializeField] private LayerMask groundLayer;
    private float groundDistance = 0.2f;

    private bool isGrounded()
    {
        Vector3 origin = bodyCollider != null ? bodyCollider.bounds.center : transform.position;
        float colliderExtent = bodyCollider != null ? bodyCollider.bounds.extents.y : 0f;
        return Physics.Raycast(origin, Vector3.down, colliderExtent + groundDistance, groundLayer, QueryTriggerInteraction.Ignore);
    }


    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<Collider>();

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

        wasGrounded = rb != null && isGrounded();
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

    private void Update() //read inputs
    {
        if (isDead)
            return;
        HandleMovement();
        HandleRotation();
    }

    private void FixedUpdate() //apply physics
    {
        if (isDead)
            return;
        
        if (rb == null)
            return;

        bool isDashing = UpdateDash();
        Vector3 velocity = currentMovement;
        if (isDashing)
        {
            velocity.x = dashDirection.x * dashSpeed;
            velocity.z = dashDirection.z * dashSpeed;
        }

        rb.linearVelocity = velocity;

        UpdateFootsteps(
            !isDashing && isGrounded() && new Vector2(currentMovement.x, currentMovement.z).magnitude > 0.1f,
            new Vector3(currentMovement.x, 0f, currentMovement.z).magnitude
        );
    }

    private bool UpdateDash()
    {
        dashCooldownTimer = Mathf.Min(dashCooldown, dashCooldownTimer + Time.fixedDeltaTime);
        if (dashCooldownSlider != null)
            dashCooldownSlider.value = dashCooldownTimer;

        if (dashRequested)
        {
            dashRequested = false;
            if (dashCooldownTimer >= dashCooldown)
            {
                Vector3 horizontalMovement = new Vector3(currentMovement.x, 0f, currentMovement.z);
                dashDirection = horizontalMovement.sqrMagnitude > 0.001f
                    ? horizontalMovement.normalized
                    : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

                dashTimer = dashDuration;
                dashCooldownTimer = 0f;
                if (dashCooldownSlider != null)
                    dashCooldownSlider.value = dashCooldownTimer;

                DashAudioSource?.Play();
            }
        }

        bool isDashing = dashTimer > 0f;
        if (isDashing)
            dashTimer = Mathf.Max(0f, dashTimer - Time.fixedDeltaTime);

        return isDashing;
    }

    private void HandleMovement()
    {
        Vector2 input = playerInputHandler.MovementInput;
        bool isSprinting = playerInputHandler.SprintTriggered;

        float currentSpeed = moveSpeed * (isSprinting ? sprintMultiplier : 1f);

        Vector3 inputDirection = transform.TransformDirection(new Vector3(input.x, 0f, input.y));
        inputDirection = Vector3.ClampMagnitude(inputDirection, 1f);

        bool grounded = isGrounded();
        float speedMultiplier = grounded ? 1f : airControlMultiplier;

        currentMovement.x = inputDirection.x * currentSpeed * speedMultiplier;
        currentMovement.z = inputDirection.z * currentSpeed * speedMultiplier;


        if (grounded)
        {
            coyoteTimeCounter = coyoteTime;
            if (currentMovement.y < 0f)
                currentMovement.y = 0f;

            if (jumpRequested)
            {
                currentMovement.y = jumpForce;
                JumpAudioSource?.Play();
                jumpRequested = false;
            }
        }
        else
        {
            coyoteTimeCounter -= Time.deltaTime;

            if (coyoteTimeCounter > 0f && jumpRequested)
            {
                currentMovement.y = jumpForce;
                JumpAudioSource?.Play();
                jumpRequested = false;
            }
            else
            {
                currentMovement.y += Physics.gravity.y * gravityMultiplier * Time.deltaTime;
            }
        }




    }

    private void UpdateFootsteps(bool isWalking, float horizontalSpeed)
    {
        if (FootstepAudioSource == null)
            return;

        if (!isWalking)
        {
            footstepTimer = Mathf.Max(0f, footstepTimer - Time.deltaTime);
            return;
        }

        float speedPercent = Mathf.Max(0.5f, horizontalSpeed / Mathf.Max(0.01f, moveSpeed));
        footstepTimer -= Time.deltaTime * speedPercent;

        if (footstepTimer <= 0f)
        {
            footstepTimer = footstepInterval;
            PlayFootstep();
        }
    }

    private void PlayFootstep()
    {
        FootstepAudioSource.pitch = Random.Range(footstepPitchRange.x, footstepPitchRange.y);

        if (footstepClips != null && footstepClips.Length > 0)
            FootstepAudioSource.PlayOneShot(footstepClips[Random.Range(0, footstepClips.Length)]);
        else
            FootstepAudioSource.Play();
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
