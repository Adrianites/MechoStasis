using UnityEngine;

public class FistCollider : MonoBehaviour
{
    [SerializeField] private int damage = 20;

    [SerializeField] private AudioSource punchSound;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Enemy"))
        {
            Debug.Log("Hit Enemy");
            punchSound.Play();
            other.GetComponent<enemyAI>().TakeDamage(damage);
        }

        if (other.CompareTag("Explosive"))
        {
            other.GetComponent<Explosive>().TakeDamage(damage);
        }
    }
}
