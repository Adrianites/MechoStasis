using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Audio;

public class GrapplingHook : MonoBehaviour
{
    [Header("Grapple Mechanics")]
    [SerializeField] private MechController PlayerController;
    [SerializeField] private InputHandler playerInputHandler;
    [SerializeField] private GameObject grapplePrefab;
    [SerializeField] private GameObject grapplingGunPrefab;
    [SerializeField] private Transform grapplingGunShootPoint;
    [SerializeField] private Transform handPosition;
    [SerializeField] private Transform playerBody;
    [SerializeField] private Camera centerCamera;
    [SerializeField] private LayerMask grappleLayer;
    [SerializeField] private LayerMask grappleVisibilityMask = Physics.DefaultRaycastLayers;
    [SerializeField] private CinemachineImpulseSource swingImpulseSource;

    [Header("Rope Visuals")]
    [SerializeField] private LineRenderer ropeRenderer;
    [SerializeField] private int ropeSegments = 18;
    [SerializeField] private float ropeWidth = 0.04f;
    [SerializeField] private float ropeSagAmount = 0.15f;

    [Header("Hook Parameters")]
    [SerializeField] private float maxGrappleDistance = 50f;
    [SerializeField] private float hookTravelSpeed = 60f;
    [SerializeField] private float grappleSearchRadius = 20f;
    [SerializeField] private float grappleSearchConeAngle = 45f;

    [Header("Rope Parameters")]
    [SerializeField] private float minRopeLength = 3f;
    [SerializeField] private float reelInSpeed = 8f;
    [SerializeField] private float ropeTensionStrength = 28f;
    [SerializeField] private float ropeStiffness = 1.5f;

    [Header("Swing Physics")]
    [SerializeField] private float swingGravityMultiplier = 1.0f;
    [SerializeField] private float tangentialInputForce = 12f;
    [SerializeField] private float swingDragWithInput = 0.2f;
    [SerializeField] private float swingDragNoInput = 0.9f;
    [SerializeField] private float maxSwingSpeed = 30f;
    [SerializeField] private float initialForwardBoost = 6f;
    [SerializeField] private float ropeAutoShortenSpeed = 16f;
    [SerializeField] private float maxRopeCorrectionSpeed = 35f;
    [SerializeField] private bool useStrictRopeConstraint = true;

    [Header("Control")]
    [SerializeField] private bool holdToSwing = false;
    //[SerializeField] private float velocityLerpSmoothing = 0.15f;

    [Header("Audio")]
    [SerializeField] private AudioSource grappleAudioSource;
    [SerializeField] private AudioClip grappleShootClip;
    [SerializeField] private AudioClip grappleAttachClip;
    [SerializeField] private AudioClip grappleReleaseClip;
    [SerializeField] private AudioSource swingLoopAudioSource;
    [SerializeField] private float swingLoopMinSpeed = 3f;
    [SerializeField] private float swingLoopMinMovementSpeed = 0.15f;
    [SerializeField] private float swingLoopMaxSpeed = 22f;
    [SerializeField] private Vector2 swingLoopPitchRange = new Vector2(0.9f, 1.2f);
    [SerializeField] private float swingLoopMaxVolume = 0.5f;
    [SerializeField, Min(1f)] private float swingLoopVolumeRampPower = 2f;
    [SerializeField] private float swingLoopFadeSpeed = 4f;
    [SerializeField, Min(0f)] private float swingLoopReleaseHoldTime = 0.2f;
    [SerializeField] private float swingLoopReleaseFadeSpeed = 1.4f;
    [SerializeField] private float swingLoopGroundedReleaseFadeSpeed = 6f;
    [SerializeField] private float swingLoopGroundedFadeBlendSpeed = 10f;
    [SerializeField] private float swingLoopSilentStopThreshold = 0.01f;

    [Header("Audio Mixer")]
    [SerializeField] private AudioMixer swingAudioMixer;
    [SerializeField] private string SwingingLowPassParameter = "SwingingLowPass";
    [SerializeField, Range(0.9f, 1.1f)] private float swingingResonanceValue = 1f;
    [SerializeField] private string swingingCutoffParameter = "SwingingCutoffFreq";
    [SerializeField] private float swingingCutoffMin = 800f;
    [SerializeField] private float swingingCutoffMax = 12000f;
    [SerializeField] private float swingingCutoffLerpSpeed = 4f;

    private bool isShooting;
    private bool isSwinging;
    private bool hookReachedTarget;
    private Vector3 hookPoint;
    private Vector3 swingVelocity;
    private Vector3 lastPlayerPosition;
    private Vector3 lastFixedPlayerPosition;
    private float ropeLength;
    private float swingStartTime;
    private bool playedAttachSound;
    private float currentSwingingCutoff;
    private float swingLoopReleaseTimer;
    private float currentSwingLoopReleaseFadeSpeed;

    void Start()
    {
        grapplingGunPrefab.SetActive(false);
        grapplePrefab.SetActive(false);
        InitializeRopeRenderer();
        isShooting = false;
        isSwinging = false;
        hookReachedTarget = false;
        lastPlayerPosition = GetPlayerPosition();
        lastFixedPlayerPosition = lastPlayerPosition;
        swingStartTime = 0f;
        playedAttachSound = false;
        swingLoopReleaseTimer = 0f;
        currentSwingLoopReleaseFadeSpeed = Mathf.Max(0f, swingLoopReleaseFadeSpeed);

        if (swingLoopAudioSource != null)
        {
            swingLoopAudioSource.loop = true;
            swingLoopAudioSource.playOnAwake = false;
            swingLoopAudioSource.volume = 0f;
        }

        currentSwingingCutoff = swingingCutoffMin;
        ApplySwingingCutoff(currentSwingingCutoff);
    }

    void Update()
    {
        if (!isSwinging)
        {
            ResetHookToHand();
            FadeOutSwingLoopAudio(Time.deltaTime);
        }

        if (isSwinging && (PlayerController == null))
        {
            EndSwing();
            return;
        }

        if (isSwinging)
        {

            if (!hookReachedTarget)
            {
                grapplePrefab.transform.position = Vector3.MoveTowards(grapplePrefab.transform.position, hookPoint, hookTravelSpeed * Time.deltaTime);
                if (Vector3.Distance(grapplePrefab.transform.position, hookPoint) <= 0.05f)
                {
                    hookReachedTarget = true;
                    grapplePrefab.transform.position = hookPoint;

                    if (!playedAttachSound)
                    {
                        PlayGrappleClip(grappleAttachClip);
                        playedAttachSound = true;
                    }
                }
            }

            if (holdToSwing && !playerInputHandler.GrappleTriggered)
            {
                EndSwing();
            }
        }

        lastPlayerPosition = GetPlayerPosition();
    }

    void LateUpdate()
    {
        UpdateRopeVisual();
    }

    void FixedUpdate()
    {
        if (!isSwinging)
        {
            return;
        }

        float deltaTime = Time.fixedDeltaTime;

        if (playerInputHandler.SprintTriggered)
        {
            ropeLength = Mathf.Max(minRopeLength, ropeLength - reelInSpeed * deltaTime);
        }

        Vector3 playerPosition = GetPlayerPosition();
        float actualMovementSpeed = (playerPosition - lastFixedPlayerPosition).magnitude / Mathf.Max(deltaTime, 0.0001f);
        Vector3 anchorToPlayer = playerPosition - hookPoint;
        float distanceToAnchor = anchorToPlayer.magnitude;

        if (distanceToAnchor < 0.001f)
        {
            lastFixedPlayerPosition = playerPosition;
            return;
        }

        Vector3 ropeDirection = anchorToPlayer / distanceToAnchor;

        if (distanceToAnchor < ropeLength)
        {
            float targetLength = Mathf.Max(minRopeLength, distanceToAnchor);
            ropeLength = Mathf.MoveTowards(ropeLength, targetLength, ropeAutoShortenSpeed * deltaTime);
        }

        float radialSpeed = Vector3.Dot(swingVelocity, ropeDirection);
        swingVelocity -= ropeDirection * radialSpeed;

        Vector3 gravityTangent = Vector3.ProjectOnPlane(Physics.gravity, ropeDirection);
        swingVelocity += gravityTangent * swingGravityMultiplier * deltaTime;

        Vector2 moveInput = playerInputHandler.MovementInput;
        float moveInputMagnitude = Mathf.Clamp01(moveInput.magnitude);
        Vector3 desiredDirection = transform.forward * moveInput.y + transform.right * moveInput.x;
        Vector3 tangentialDirection = Vector3.ProjectOnPlane(desiredDirection, ropeDirection);

        if (tangentialDirection.sqrMagnitude > 0.001f && moveInputMagnitude > 0.001f)
        {
            swingVelocity += tangentialDirection.normalized * tangentialInputForce * moveInputMagnitude * deltaTime;
        }

        float swingDrag = Mathf.Lerp(swingDragNoInput, swingDragWithInput, moveInputMagnitude);
        swingVelocity *= 1f / (1f + swingDrag * deltaTime);

        float ropeError = distanceToAnchor - ropeLength;

        if (useStrictRopeConstraint)
        {
            if (ropeError > 0f)
            {
                float correctionVelocity = Mathf.Clamp(ropeError / Mathf.Max(deltaTime, 0.0001f), 0f, maxRopeCorrectionSpeed);
                swingVelocity += -ropeDirection * correctionVelocity;
            }
        }
        else if (distanceToAnchor > ropeLength)
        {
            float stretchAmount = ropeError;
            float tensionForce = stretchAmount * ropeTensionStrength * ropeStiffness;
            swingVelocity += -ropeDirection * tensionForce * deltaTime;
        }

        float swingSpeed = swingVelocity.magnitude;
        if (swingSpeed > maxSwingSpeed)
        {
            swingVelocity = swingVelocity.normalized * maxSwingSpeed;
            swingSpeed = maxSwingSpeed;
        }

        UpdateSwingingCutoff(swingSpeed, deltaTime);

        float loopSwingSpeed = hookReachedTarget ? swingSpeed : 0f;
        float loopMovementSpeed = hookReachedTarget ? actualMovementSpeed : 0f;
        UpdateSwingLoopAudio(loopSwingSpeed, loopMovementSpeed, deltaTime);

        PlayerController.SetExternalVelocity(swingVelocity);
        lastFixedPlayerPosition = playerPosition;
    }

    private void ShootHook()
    {
        if (PlayerController == null)
        {
            return;
        }

        if (isShooting)
        {
            return;
        }

        if (isSwinging)
        {
            EndSwing();
            return;
        }

        isShooting = true;
        Ray ray = centerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        Debug.DrawRay(ray.origin, ray.direction * maxGrappleDistance, Color.green, 2f);

        Vector3 targetPoint = FindClosestGrappleTarget(ray);
        
        if (targetPoint != Vector3.zero)
        {
            Vector3 playerPosition = GetPlayerPosition();
            Vector3 playerVelocity = (playerPosition - lastPlayerPosition) / Mathf.Max(Time.deltaTime, 0.0001f);

            hookPoint = targetPoint;
            ropeLength = Mathf.Max(minRopeLength, Vector3.Distance(playerPosition, hookPoint));
            
            swingVelocity = playerVelocity;
            
            Vector3 ropeDirection = (playerPosition - hookPoint).normalized;
            swingVelocity += ropeDirection * initialForwardBoost * 0.4f;

            isSwinging = true;
            hookReachedTarget = false;
            playedAttachSound = false;
            swingStartTime = Time.time;
            lastFixedPlayerPosition = playerPosition;

            PlayGrappleClip(grappleShootClip);

            grapplingGunPrefab.SetActive(true);
            grapplePrefab.SetActive(true);
            grapplePrefab.transform.parent = null;
            grapplePrefab.transform.position = playerPosition;
            grapplePrefab.transform.LookAt(hookPoint);
            lastPlayerPosition = playerPosition;
        }
        //swingImpulseSource.GenerateImpulse();
        isShooting = false;
    }

    private void EndSwing()
    {
        if (!isSwinging)
        {
            return;
        }

        PlayerController.SetExternalVelocity(swingVelocity);

        isSwinging = false;
        hookReachedTarget = false;
        grapplePrefab.SetActive(false);
        grapplingGunPrefab.SetActive(false);
        PlayGrappleClip(grappleReleaseClip);
        swingLoopReleaseTimer = Mathf.Max(0f, swingLoopReleaseHoldTime);
        ResetSwingingCutoff();
        UpdateRopeVisual();
    }

    private void PlayGrappleClip(AudioClip clip)
    {
        if (grappleAudioSource == null || clip == null)
        {
            return;
        }

        grappleAudioSource.PlayOneShot(clip);
    }

    private void StartSwingLoopAudio()
    {
        if (swingLoopAudioSource == null || swingLoopAudioSource.clip == null)
        {
            return;
        }

        if (!swingLoopAudioSource.isPlaying)
        {
            swingLoopAudioSource.volume = 0f;
            swingLoopAudioSource.pitch = swingLoopPitchRange.x;
            swingLoopAudioSource.Play();
        }
    }

    private void UpdateSwingLoopAudio(float speed, float movementSpeed, float deltaTime)
    {
        if (swingLoopAudioSource == null || swingLoopAudioSource.clip == null)
        {
            return;
        }

        StartSwingLoopAudio();
        swingLoopReleaseTimer = 0f;
        currentSwingLoopReleaseFadeSpeed = Mathf.Max(0f, swingLoopReleaseFadeSpeed);

        float normalizedSwingSpeed = Mathf.InverseLerp(swingLoopMinSpeed, swingLoopMaxSpeed, speed);
        float normalizedMovementSpeed = Mathf.InverseLerp(swingLoopMinMovementSpeed, swingLoopMaxSpeed, movementSpeed);
        float loopIntensity = Mathf.Clamp01(Mathf.Min(normalizedSwingSpeed, normalizedMovementSpeed));
        loopIntensity = loopIntensity * loopIntensity * (3f - 2f * loopIntensity);
        float volumeIntensity = Mathf.Pow(loopIntensity, Mathf.Max(1f, swingLoopVolumeRampPower));

        float targetPitch = Mathf.Lerp(swingLoopPitchRange.x, swingLoopPitchRange.y, loopIntensity);
        float targetVolume = volumeIntensity * swingLoopMaxVolume;

        float audioStep = Mathf.Max(swingLoopFadeSpeed, 0f) * deltaTime;
        swingLoopAudioSource.pitch = Mathf.MoveTowards(swingLoopAudioSource.pitch, targetPitch, audioStep);
        swingLoopAudioSource.volume = Mathf.MoveTowards(swingLoopAudioSource.volume, targetVolume, audioStep);
    }

    private void FadeOutSwingLoopAudio(float deltaTime)
    {
        if (swingLoopAudioSource == null || !swingLoopAudioSource.isPlaying)
        {
            return;
        }

        bool isGrounded = PlayerController != null;

        if (swingLoopReleaseTimer > 0f)
        {
            if (!isGrounded)
            {
                swingLoopReleaseTimer = Mathf.Max(0f, swingLoopReleaseTimer - deltaTime);
                return;
            }

            swingLoopReleaseTimer = 0f;
        }

        float targetFadeSpeed = isGrounded ? swingLoopGroundedReleaseFadeSpeed : swingLoopReleaseFadeSpeed;
        float fadeBlendStep = Mathf.Max(swingLoopGroundedFadeBlendSpeed, 0f) * deltaTime;
        currentSwingLoopReleaseFadeSpeed = Mathf.MoveTowards(currentSwingLoopReleaseFadeSpeed, Mathf.Max(0f, targetFadeSpeed), fadeBlendStep);

        float audioStep = currentSwingLoopReleaseFadeSpeed * deltaTime;
        swingLoopAudioSource.pitch = Mathf.MoveTowards(swingLoopAudioSource.pitch, swingLoopPitchRange.x, audioStep);
        swingLoopAudioSource.volume = Mathf.MoveTowards(swingLoopAudioSource.volume, 0f, audioStep);

        if (swingLoopAudioSource.volume <= swingLoopSilentStopThreshold)
        {
            StopSwingLoopAudio();
        }
    }

    private void StopSwingLoopAudio()
    {
        if (swingLoopAudioSource == null)
        {
            return;
        }

        if (swingLoopAudioSource.isPlaying)
        {
            swingLoopAudioSource.Stop();
        }

        swingLoopReleaseTimer = 0f;
        currentSwingLoopReleaseFadeSpeed = Mathf.Max(0f, swingLoopReleaseFadeSpeed);
        swingLoopAudioSource.volume = 0f;
    }

    private void UpdateSwingingCutoff(float speed, float deltaTime)
    {
        float normalizedSpeed = Mathf.InverseLerp(swingLoopMinSpeed, swingLoopMaxSpeed, speed);
        float targetCutoff = Mathf.Lerp(swingingCutoffMin, swingingCutoffMax, normalizedSpeed);
        float maxStep = Mathf.Abs(swingingCutoffMax - swingingCutoffMin) * Mathf.Max(swingingCutoffLerpSpeed, 0f) * deltaTime;
        currentSwingingCutoff = Mathf.MoveTowards(currentSwingingCutoff, targetCutoff, maxStep);
        ApplySwingingCutoff(currentSwingingCutoff);
    }

    private void ResetSwingingCutoff()
    {
        currentSwingingCutoff = swingingCutoffMin;
        ApplySwingingCutoff(currentSwingingCutoff);
    }

    private void ApplySwingingCutoff(float value)
    {
        AudioMixer mixer = swingAudioMixer;

        if (mixer == null && swingLoopAudioSource != null && swingLoopAudioSource.outputAudioMixerGroup != null)
        {
            mixer = swingLoopAudioSource.outputAudioMixerGroup.audioMixer;
        }

        if (mixer == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(SwingingLowPassParameter))
        {
            float clampedResonance = Mathf.Clamp(swingingResonanceValue, 0.9f, 1.1f);
            mixer.SetFloat(SwingingLowPassParameter, clampedResonance);
        }

        bool applied = false;

        if (!string.IsNullOrEmpty(SwingingLowPassParameter))
        {
            applied = mixer.SetFloat(SwingingLowPassParameter, value);
        }

        if (!applied && !string.IsNullOrEmpty(swingingCutoffParameter))
        {
            mixer.SetFloat(swingingCutoffParameter, value);
        }
    }

    private void InitializeRopeRenderer()
    {
        if (ropeRenderer == null)
        {
            ropeRenderer = GetComponent<LineRenderer>();
        }

        if (ropeRenderer == null)
        {
            ropeRenderer = gameObject.AddComponent<LineRenderer>();
        }

        ropeRenderer.enabled = false;
        ropeRenderer.useWorldSpace = true;
        ropeRenderer.positionCount = Mathf.Max(2, ropeSegments);
        ropeRenderer.startWidth = ropeWidth;
        ropeRenderer.endWidth = ropeWidth;
        ropeRenderer.numCapVertices = 4;
        ropeRenderer.numCornerVertices = 4;

        if (ropeRenderer.material == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                ropeRenderer.material = new Material(shader);
            }
        }
    }

    private void UpdateRopeVisual()
    {
        if (ropeRenderer == null)
        {
            return;
        }

        bool shouldShowRope = isSwinging || hookReachedTarget;
        ropeRenderer.enabled = shouldShowRope;

        if (!shouldShowRope)
        {
            return;
        }

        Vector3 startPoint = grapplingGunShootPoint != null ? grapplingGunShootPoint.position : GetPlayerPosition();
        Vector3 endPoint = hookReachedTarget ? hookPoint : grapplePrefab.transform.position;
        Vector3 midPoint = (startPoint + endPoint) * 0.5f;

        Vector3 downDirection = Vector3.down;
        float distance = Vector3.Distance(startPoint, endPoint);
        float sag = Mathf.Min(distance * ropeSagAmount, 2f);
        midPoint += downDirection * sag;

        int pointCount = Mathf.Max(ropeRenderer.positionCount, 2);
        ropeRenderer.positionCount = pointCount;

        for (int i = 0; i < pointCount; i++)
        {
            float t = i / (float)(pointCount - 1);
            Vector3 ropePoint = QuadraticBezier(startPoint, midPoint, endPoint, t);
            ropeRenderer.SetPosition(i, ropePoint);
        }

        ropeRenderer.SetPosition(0, startPoint);
        ropeRenderer.SetPosition(pointCount - 1, endPoint);
    }

    private Vector3 QuadraticBezier(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float oneMinusT = 1f - t;
        return oneMinusT * oneMinusT * a + 2f * oneMinusT * t * b + t * t * c;
    }

    private Vector3 FindClosestGrappleTarget(Ray ray)
    {
        RaycastHit directHit;

        Vector3 searchCenter = ray.origin + ray.direction * maxGrappleDistance * 0.5f;
        Collider[] nearbyColliders = Physics.OverlapSphere(searchCenter, grappleSearchRadius, grappleLayer);

        Vector3 closestPoint = Vector3.zero;
        float closestDistance = float.MaxValue;
        float closestAngle = float.MaxValue;

        void TryCandidate(Vector3 candidatePoint, Collider candidateCollider)
        {
            Vector3 toCandidate = candidatePoint - ray.origin;
            float distance = toCandidate.magnitude;

            if (distance <= 0.001f || distance > maxGrappleDistance)
            {
                return;
            }

            Vector3 directionToPoint = toCandidate / distance;
            float angle = Vector3.Angle(ray.direction, directionToPoint);

            if (angle > grappleSearchConeAngle)
            {
                return;
            }

            if (!HasClearLineOfSight(ray.origin, candidatePoint, candidateCollider))
            {
                return;
            }

            if (distance < closestDistance || (Mathf.Approximately(distance, closestDistance) && angle < closestAngle))
            {
                closestDistance = distance;
                closestAngle = angle;
                closestPoint = candidatePoint;
            }
        }

        if (Physics.Raycast(ray, out directHit, maxGrappleDistance, grappleLayer))
        {
            TryCandidate(directHit.point, directHit.collider);
        }

        foreach (Collider col in nearbyColliders)
        {
            Vector3 pointOnCollider = col.ClosestPoint(ray.origin);
            TryCandidate(pointOnCollider, col);
        }

        return closestPoint;
    }

    private bool HasClearLineOfSight(Vector3 origin, Vector3 targetPoint, Collider targetCollider)
    {
        if (targetCollider == null)
        {
            return false;
        }

        Vector3 toTarget = targetPoint - origin;
        float distance = toTarget.magnitude;

        if (distance <= 0.001f)
        {
            return false;
        }

        Vector3 direction = toTarget / distance;

        if (!Physics.Raycast(origin, direction, out RaycastHit hit, distance + 0.01f, grappleVisibilityMask, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        return hit.collider == targetCollider;
    }

    private Vector3 GetPlayerPosition()
    {
        if (PlayerController != null)
        {
            return PlayerController.transform.position;
        }

        if (playerBody != null)
        {
            return playerBody.position;
        }

        return transform.position;
    }

    private void OnGrapplePerformed()
    {
        if (PlayerController == null)
        {
            return;
        }

        ShootHook();
    }

    private void ResetHookToHand()
    {
        if (grapplePrefab.transform.parent != handPosition)
        {
            grapplePrefab.transform.SetParent(handPosition);
        }

        grapplePrefab.transform.localPosition = Vector3.zero;
        grapplePrefab.transform.localRotation = Quaternion.identity;

        if (ropeRenderer != null && !isSwinging)
        {
            ropeRenderer.enabled = false;
        }
    }

    private void OnEnable()
    {
        playerInputHandler.OnGrapplePerformed += OnGrapplePerformed;
    }

    private void OnDisable()
    {
        playerInputHandler.OnGrapplePerformed -= OnGrapplePerformed;

        if (isSwinging)
        {
            EndSwing();
        }

        StopSwingLoopAudio();
        ResetSwingingCutoff();
    }
}
