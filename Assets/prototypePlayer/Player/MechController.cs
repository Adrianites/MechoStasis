using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.UI;
using System.Collections;

public class MechController : MonoBehaviour, IDamageable
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float airControlMultiplier = 0.5f;

    [Header("Jump")]
    [SerializeField] private float jumpForce = 5f;
    [SerializeField] private float gravityMultiplier = 1f;
    [SerializeField] private float coyoteTime = 0.1f;

    [Header("Camera")]
    [SerializeField] private float mouseSensitivity = 20f;
    [SerializeField] private float upDownLookRange = 80f;
    [SerializeField] private float cameraReturnSpeed = 8f;

    [Header("Mech Camera Impulses")]
    [SerializeField] private CinemachineImpulseSource movementImpulseSource;
    [SerializeField] private CinemachineImpulseSource accelerationImpulseSource;
    [SerializeField] private CinemachineImpulseSource jumpImpulseSource;
    [SerializeField] private CinemachineImpulseSource landingImpulseSource;

    [SerializeField] private float movementImpulseInterval = 0.45f;

    [SerializeField] private float movementImpulseSpeedThreshold = 0.25f;

    [SerializeField] private float accelerationImpulseThreshold = 2f;

    [SerializeField] private float landingSpeedThreshold = 4f;

    [SerializeField] private float accelerationImpulseStrength = 1f;

    [SerializeField] private float landingImpulseStrength = 1f;

    [SerializeField] private int health = 10;
    [SerializeField] private bool isDead = false;

    [Header("References")]
    [SerializeField] private CharacterController characterController;
    [SerializeField] private CinemachineCamera mainCam;
    [SerializeField] private Transform ogCockpitCamRotTransform;
    [SerializeField] private CinemachineCamera cockpitCam;
    [SerializeField] private InputHandler playerInputHandler;
    [SerializeField] private GameObject deathCanvas;
    [SerializeField] private GameObject healthCanvas;
    [SerializeField] private Slider healthSlider;

    public bool CockpitRotate = false;

    private CinemachineImpulseListener cockpitImpulseListener;
    private Quaternion mainCamRestRotation;
    private Quaternion cockpitCamRestRotation;
    private float mainCameraPitch;
    private float cockpitCameraPitch;
    private float cockpitCameraYaw;

    private Vector3 currentMovement;
    private float coyoteTimeCounter;
    private bool jumpRequested;
    private bool returningMainCamera;

    private Vector3 previousHorizontalVelocity;
    private float movementImpulseTimer;
    private float previousVerticalVelocity;
    private bool wasGrounded;

    [Header ("Shooting")]
    private bool attackHeld;
    private bool canShoot;
    [SerializeField] GameObject projectilePrefab;
    [SerializeField] Transform muzzle;
    [SerializeField] float speed = 25f;
    [SerializeField] float fireRate = 8f;
    float nextFire;
    private Coroutine attackCoroutine;
    public CinemachineImpulseSource shootingImpulseSource;

    private void Start()
    {
        if (healthSlider != null)
        {
            healthSlider.maxValue = health;
            healthSlider.value = health;
        }
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
        bool groundedBeforeMove = characterController.isGrounded;

        Vector2 input = playerInputHandler.MovementInput;

        Vector3 inputDirection = new Vector3(input.x, 0f, input.y);
        Vector3 worldDirection = transform.TransformDirection(inputDirection);

        if (worldDirection.sqrMagnitude > 1f)
            worldDirection.Normalize();

        Vector3 horizontalVelocity;

        if (groundedBeforeMove)
        {
            coyoteTimeCounter = coyoteTime;
            horizontalVelocity = worldDirection * moveSpeed;

            if (currentMovement.y < 0f)
                currentMovement.y = -0.5f;
        }
        else
        {
            coyoteTimeCounter -= Time.deltaTime;

            if (worldDirection.sqrMagnitude > 0.001f)
            {
                horizontalVelocity =
                    worldDirection * moveSpeed * airControlMultiplier;
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
        bool cockpitLookHeld = playerInputHandler.SprintTriggered;

        if (cockpitLookHeld != CockpitRotate)
        {
            CockpitRotate = cockpitLookHeld;

            if (CockpitRotate)
            {
                returningMainCamera = false;

                if (cockpitCam != null)
                {
                    cockpitCam.transform.localRotation = cockpitCamRestRotation;
                    cockpitCameraPitch = NormalizePitch(cockpitCamRestRotation.eulerAngles.x);
                    cockpitCameraYaw = NormalizePitch(cockpitCamRestRotation.eulerAngles.y);
                }
            }
            else
            {
                returningMainCamera = true;
            }
        }

        if (returningMainCamera)
        {
            if (mainCam == null)
            {
                returningMainCamera = false;
                return;
            }

            mainCam.transform.localRotation = Quaternion.Slerp(
                mainCam.transform.localRotation,
                mainCamRestRotation,
                cameraReturnSpeed * Time.deltaTime
            );

            if (Quaternion.Angle(mainCam.transform.localRotation, mainCamRestRotation) <= 0.1f)
            {
                mainCam.transform.localRotation = mainCamRestRotation;
                mainCameraPitch = NormalizePitch(mainCamRestRotation.eulerAngles.x);
                returningMainCamera = false;
            }

            if (cockpitCam != null)
            {
                cockpitCam.transform.localRotation = cockpitCamRestRotation;
            }

            return;
        }

        Vector2 lookInput = playerInputHandler.RotationInput;

        float mouseX = lookInput.x * mouseSensitivity * Time.deltaTime;

        float mouseY = lookInput.y * mouseSensitivity * Time.deltaTime;

        if (!CockpitRotate)
            transform.Rotate(Vector3.up * mouseX);

        if (CockpitRotate)
        {
            cockpitCameraYaw += mouseX;
            cockpitCameraPitch = Mathf.Clamp
            (cockpitCameraPitch - mouseY, -upDownLookRange, upDownLookRange);
        }
        else
        {
            mainCameraPitch = Mathf.Clamp
            (mainCameraPitch - mouseY, -upDownLookRange, upDownLookRange);
        }

        if (mainCam != null && !CockpitRotate)
        {
            mainCam.transform.localRotation = mainCamRestRotation * Quaternion.Euler(mainCameraPitch, 0f, 0f);
        }
        
        if (cockpitCam != null && CockpitRotate)
        {
            cockpitCam.transform.localRotation = cockpitCamRestRotation * Quaternion.Euler(cockpitCameraPitch, cockpitCameraYaw, 0f);
        }
        else if (cockpitCam != null && !CockpitRotate)
        {
            cockpitCam.transform.localRotation = cockpitCamRestRotation;
        }
    }

    private float NormalizePitch(float pitch)
    {
        return Mathf.DeltaAngle(0f, pitch);
    }

    private void OnAttackInput()
    {
        Debug.Log("StartedShooting");
        if (isDead)
        {
            return;
        }

        attackHeld = true;
        if (attackCoroutine == null)
        {
            attackCoroutine = StartCoroutine(AttackLoop());
        }
    }

    private void OnAttackInputCanceled()
    {
        attackHeld = false;
        Debug.Log("StoppedShooting");
    }

    private IEnumerator AttackLoop()
    {
        while (attackHeld)
        {
            if (canShoot)
            {
                StartCoroutine(GunShoot(fireRate));
            }
            yield return null;
        }
        attackCoroutine = null;
    }

    private IEnumerator GunShoot(float time)
    {
        Debug.Log("GunShoot");
        canShoot = false;
        CameraShakeManager.instance.CameraShake(shootingImpulseSource);
        SpawnProjectile(projectilePrefab, muzzle, 1f);
        yield return new WaitForSeconds(time);
        canShoot = true;
    }

    private void SpawnProjectile(GameObject ProjectilePrefab, Transform spawnPoint, float speed)
    {
        Debug.Log("Shooting");
        if (ProjectilePrefab == null || spawnPoint == null)
        {
            return;
        }

        Projectile projectileSettings = ProjectilePrefab.GetComponent<Projectile>();

        GameObject projectileInstance = Instantiate(projectilePrefab, spawnPoint.position, spawnPoint.rotation);
        Rigidbody rb = projectileInstance.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = spawnPoint.up * speed;
        }
    }

    private void OnEnable()
    {
        if (playerInputHandler == null)
            return;

        playerInputHandler.OnJumpPerformed += OnJumpInput;
        playerInputHandler.OnJumpCanceled += OnJumpInputCanceled;
        playerInputHandler.OnAttackPerformed += OnAttackInput; 
        playerInputHandler.OnAttackCanceled += OnAttackInputCanceled;
    }

    private void OnDisable()
    {
        if (playerInputHandler == null)
            return;

        playerInputHandler.OnJumpPerformed -= OnJumpInput;
        playerInputHandler.OnJumpCanceled -= OnJumpInputCanceled;
        playerInputHandler.OnAttackPerformed -= OnAttackInput;
        playerInputHandler.OnAttackCanceled -= OnAttackInputCanceled;
    }

    private void OnJumpInput()
    {
        jumpRequested = true;
    }

    private void OnJumpInputCanceled()
    {
        jumpRequested = false;
    }
}
