using UnityEngine;

public class hand : MonoBehaviour
{
    [SerializeField] Camera cam;
    [SerializeField] float range = 3f;
    [SerializeField] float holdDistance = 2f;
    [SerializeField] float springForce = 12f;

    Rigidbody held;

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.E)) return;
        if (held)
        {
            held = null; return;
        }
        
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        
        if (Physics.Raycast(ray, out var hit, range) && hit.rigidbody && !hit.rigidbody.isKinematic)
        {
            held = hit.rigidbody;
        }
    }

    void FixedUpdate()
    {
        if (!held) return;
        Vector3 target = cam.transform.position + cam.transform.forward * holdDistance;
        held.linearVelocity = (target - held.position) * springForce;
    }
}