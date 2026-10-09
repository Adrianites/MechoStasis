using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Projectile : MonoBehaviour
{
    [SerializeField] int damage = 10;
    [SerializeField] float life = 4f;

    public void Launch(Vector3 velocity)
    {
        GetComponent<Rigidbody>().linearVelocity = velocity;
        Destroy(gameObject, life);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Enemy"))
        {
            other.GetComponent<enemyAI>().TakeDamage(damage);
        }
    }
}