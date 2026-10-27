using UnityEngine;
using UnityEngine.AI;
using System.Collections;

[RequireComponent(typeof(NavMeshAgent))]
public class enemyAI : MonoBehaviour, IDamageable
{
    [SerializeField] Transform target;
    [SerializeField] float repathInterval = 0.25f;
    [SerializeField] int health = 2;
    [SerializeField] Animator animator;
    [SerializeField] float deathDuration = 3f;

    ObjectPool objectPool;
    NavMeshAgent agent;
    Collider[] colliders;
    int currentHealth;
    float nextRepath;
    bool isDead;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        colliders = GetComponents<Collider>();
    }

    void OnEnable()
    {
        currentHealth = health;
        isDead = false;
        agent.enabled = true;
        SetCollidersEnabled(true);
    }

    public void SetPool(ObjectPool pool)
    {
        objectPool = pool;
    }

    public void TakeDamage(int damage)
    {
        if (isDead)
            return;

        currentHealth -= damage;
        if (currentHealth <= 0)
        {
            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(Die());
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }

    IEnumerator Die()
    {
        isDead = true;

        agent.enabled = false;
        SetCollidersEnabled(false);
        animator.SetBool("Walking", false);
        animator.SetBool("Dead", true);

        yield return new WaitForSeconds(deathDuration);

        if (objectPool != null)
        {
            objectPool.ReturnObject(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void SetCollidersEnabled(bool enabled)
    {
        foreach (Collider col in colliders)
        {
            col.enabled = enabled;
        }
    }

    void Update()
    {
        if (isDead) return;

        animator.SetBool("Walking", agent.velocity.sqrMagnitude > 0.01f);

        target = GameObject.FindGameObjectWithTag("Player").transform;
        if (target == null) return;

        if (Time.time >= nextRepath)
        {
            nextRepath = Time.time + repathInterval;
            agent.SetDestination(target.position);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            animator.SetTrigger("Attack");
            other.GetComponent<IDamageable>().TakeDamage(1);
            Debug.Log("Player hit by enemy");
        }
    }
}
