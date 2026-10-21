using System.Collections.Generic;
using UnityEngine;

public class ObjectPool : MonoBehaviour
{
    [SerializeField] private GameObject prefab;
    private Queue<GameObject> pool = new Queue<GameObject>();

    public GameObject GetObject(Vector3 position, Quaternion rotation)
    {
        GameObject obj = null;
        while (pool.Count > 0 && obj == null)
        {
            obj = pool.Dequeue();
        }

        if (obj == null)
        {
            if (prefab == null)
            {
                Debug.LogError("ObjectPool has no prefab assigned.");
                return null;
            }

            obj = Instantiate(prefab);
        }

        Projectile projectile = obj.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.SetPool(this);
        }

        enemyAI enemy = obj.GetComponent<enemyAI>();
        if (enemy != null)
        {
            enemy.SetPool(this);
        }

        obj.transform.SetPositionAndRotation(position, rotation);
        obj.SetActive(true);
        return obj;
    }

    public void ReturnObject(GameObject obj)
    {
        if (obj == null)
        {
            return;
        }

        obj.SetActive(false);
        pool.Enqueue(obj);
    }
}