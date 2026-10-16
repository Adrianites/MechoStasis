using UnityEngine;
using System.Collections;

public class Explosive : MonoBehaviour, IDamageable
{
    public int health = 2;
    public int damage = 5;
    public float explosionRadius = 5f;
    public GameObject explosionEffect;
    public float delayBeforeDestroy = 0.1f;
    public bool exploded = false;

    public void TakeDamage(int damage)
    {
        health -= damage;
        if (health <= 0)
        {
            Explode();
        }
    }

    public void Explode()
    {
        if (exploded)
        {
            return;
        }

        exploded = true;
        
        if (explosionEffect != null)
        {
            Instantiate(explosionEffect, transform.position, Quaternion.identity);
        }
            // add knovkback

            Collider[] explosionCollision = Physics.OverlapSphere(transform.position, explosionRadius);
            foreach (Collider nearbyObject in explosionCollision)
            {
                if (nearbyObject.CompareTag("Enemy"))
                {
                    nearbyObject.GetComponent<enemyAI>().TakeDamage(damage);
                }

                if (nearbyObject.CompareTag("Player"))
                {
                    nearbyObject.GetComponent<MechController>().TakeDamage(damage);
                }

                if (nearbyObject.CompareTag("Explosive"))
                {
                    nearbyObject.GetComponent<Explosive>().TakeDamage(damage);
                }
            }

        StartCoroutine(DestroyAfterDelay());
    }

    private IEnumerator DestroyAfterDelay()
    {
        yield return new WaitForSeconds(delayBeforeDestroy);
        Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
}
