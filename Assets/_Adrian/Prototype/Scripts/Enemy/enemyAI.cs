using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class enemyAI : MonoBehaviour, IDamageable
{
    [SerializeField] Transform target;
    [SerializeField] float repathInterval = 0.25f;
    [SerializeField] int health = 2;

    NavMeshAgent agent;
    float nextRepath;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    public void TakeDamage(int damage)
    {
        health -= damage;
        if (health <= 0)
        {
            Destroy(gameObject);
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