using UnityEngine;
using System.Collections;
using UnityEngine.InputSystem;

public class GameManager : MonoBehaviour
{
    public ObjectPool bulletPool;
    public float bulletSpeed = 10f;

    private InputAction shootInput;

    void Awake()
    {
        shootInput = InputSystem.actions.FindAction("Attack");

        SubscribeMePls();
    }
    
    public void SubscribeMePls()
    {
        shootInput.performed += ctx => Shoot();
    }


    void Shoot()
    {
         SpawnBullet();
         Debug.Log("BANG!");

    }

    void SpawnBullet()
    {
        GameObject bullet = bulletPool.GetObject();
        bullet.transform.position = transform.position;
        bullet.transform.rotation = transform.rotation;

        Rigidbody rb = bullet.GetComponent<Rigidbody>();

        if (rb != null)
            rb.linearVelocity = bullet.transform.forward * bulletSpeed;

        StartCoroutine(DeactivateBullet(bullet));
    }

    IEnumerator DeactivateBullet(GameObject bullet)
    {
        yield return new WaitForSeconds(2f);
        bulletPool.ReturnObject(bullet);
    }

    
}
