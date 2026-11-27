using UnityEngine;
using Unity.Behavior;
using System.Collections;

public class EnemyHealthHandler : MonoBehaviour, IDamageable
{
    [SerializeField] public int maxHealth = 10;
    [SerializeField] private int currentHealth;
    [SerializeField] private Animator animator;
    [SerializeField] private AudioSource damageAudioSource;
    [SerializeField] private bool isDead = false;
    [SerializeField] private bool isHit = false;
    [SerializeField] private BehaviorGraphAgent agent;
    [SerializeField] private Collider collider;
    [SerializeField] private GameObject deathVFXPrefab;
    [SerializeField] private Transform smokeSpawnPoint;

    private void Start()
    {
        currentHealth = maxHealth;
        agent = GetComponent<BehaviorGraphAgent>();
        collider = GetComponent<Collider>();
    }

    public void TakeDamage(int damage)
    {
        currentHealth -= damage;
        if (currentHealth <= 0 && !isDead)
        {
            Die();
        }
        else if (!isDead && !isHit)
        {
            StartCoroutine(Hit());
        }
    }

    private void Die()
    {
        animator.SetBool("Death", true);
        agent.enabled = false;
        collider.enabled = false;
        isDead = true;
        StartCoroutine(DeathCoroutine());
    }

    public void DeathVFX()
    {
        Instantiate(deathVFXPrefab, smokeSpawnPoint.position, Quaternion.identity);
    }

    public void Despawn()
    {
        Destroy(gameObject);
    }

    private IEnumerator Hit()
    {
        isHit = true;
        int hit = Random.Range(0, 2);
        if (hit == 0)
        {
            animator.SetTrigger("Hit1");
        }
        else if (hit == 1)
        {
            animator.SetTrigger("Hit2");
        }
        else
        {
            animator.SetTrigger("Hit3");
        }
        damageAudioSource.Play();
        yield return new WaitForSeconds(1f);
        isHit = false;
    }

    private IEnumerator DeathCoroutine()
    {
        yield return new WaitForSeconds(10f);
        Destroy(gameObject);
    }
}
