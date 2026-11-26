using UnityEngine;

public class KnockBackTest : MonoBehaviour
{
    [SerializeField] private float knockBackForce = 10f;

    private void OnCollisionEnter(Collision collision)
    {
        Rigidbody rb = collision.rigidbody;

        if (rb == null)
            return;

        Vector3 direction = rb.transform.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
            return;

        rb.AddForce(direction.normalized * knockBackForce, ForceMode.Impulse);
    }
}