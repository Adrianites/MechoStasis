using UnityEngine;

public class hand : MonoBehaviour
{
    [SerializeField] private Camera cam;
    [SerializeField] private InputHandler playerInputHandler;
    [SerializeField] private float range = 3f;
    [SerializeField] private float holdDistance = 2f;
    [SerializeField] private float springForce = 12f;
    [SerializeField] private LayerMask pickupLayerMask;

    private Rigidbody held;

    private void Awake()
    {
        if (playerInputHandler == null)
        {
            playerInputHandler = GetComponentInParent<InputHandler>();
        }

        if (pickupLayerMask.value == 0)
        {
            pickupLayerMask = LayerMask.GetMask("Pickup");
        }
    }

    private void OnEnable()
    {
        if (playerInputHandler == null)
        {
            return;
        }

        playerInputHandler.OnPickupPerformed += TogglePickup;
    }

    private void OnDisable()
    {
        if (playerInputHandler == null)
        {
            return;
        }

        playerInputHandler.OnPickupPerformed -= TogglePickup;
    }

    private void TogglePickup()
    {
        if (held)
        {
            held = null;
            return;
        }

        if (cam == null)
        {
            return;
        }

        var ray = new Ray(cam.transform.position, cam.transform.forward);

        if (Physics.Raycast(ray, out var hit, range, pickupLayerMask) && hit.rigidbody && !hit.rigidbody.isKinematic)
        {
            held = hit.rigidbody;
        }
    }

    private void FixedUpdate()
    {
        if (!held) return;
        Vector3 target = cam.transform.position + cam.transform.forward * holdDistance;
        held.linearVelocity = (target - held.position) * springForce;
    }
}