using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class enemyAI : MonoBehaviour, IDamageable
{
    [SerializeField] Transform target;
    [SerializeField] float repathInterval = 0.25f;
    [SerializeField] int health = 2;

    ObjectPool objectPool;
    NavMeshAgent agent;
    int currentHealth;
    float nextRepath;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    void OnEnable()
    {
        currentHealth = health;
    }

    public void SetPool(ObjectPool pool)
    {
        objectPool = pool;
    }

    public void TakeDamage(int damage)
    {
        currentHealth -= damage;
        if (currentHealth <= 0)
        {
            if (objectPool != null)
            {
                agent.ResetPath();
                objectPool.ReturnObject(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }

    void Update()
    {
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
            other.GetComponent<IDamageable>().TakeDamage(1);
            Debug.Log("Player hit by enemy");
        }
    }
}