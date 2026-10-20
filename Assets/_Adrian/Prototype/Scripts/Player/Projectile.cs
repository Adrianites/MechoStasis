using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Projectile : MonoBehaviour
{
    [SerializeField] int damage = 10;
    [SerializeField] float life = 4f;

    private ObjectPool objectPool;
    private Coroutine returnCoroutine;

    public void SetPool(ObjectPool pool)
    {
        objectPool = pool;
    }

    public void Launch(Vector3 velocity)
    {
        GetComponent<Rigidbody>().linearVelocity = velocity;

        if (returnCoroutine != null)
        {
            StopCoroutine(returnCoroutine);
        }

        returnCoroutine = StartCoroutine(ReturnAfterLife());
    }

    private System.Collections.IEnumerator ReturnAfterLife()
    {
        yield return new WaitForSeconds(life);

        if (objectPool != null)
        {
            objectPool.ReturnObject(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }

        returnCoroutine = null;
    }

    void OnTriggerEnter(Collider other)
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