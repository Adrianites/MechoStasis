using UnityEngine;

public class FistCollider : MonoBehaviour
{
    [SerializeField] private int damage = 20;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Enemy"))
        {
            other.GetComponent<enemyAI>().TakeDamage(damage);
        }

        if (other.CompareTag("Explosive"))
        {
            other.GetComponent<Explosive>().TakeDamage(damage);
        }
    }
}
