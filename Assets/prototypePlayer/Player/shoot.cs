using UnityEngine;

public class shoot : MonoBehaviour
{
    [SerializeField] Projectile projectilePrefab;
    [SerializeField] Transform muzzle;
    [SerializeField] float speed = 25f;
    [SerializeField] float fireRate = 8f;

    float nextFire;

    void Update()
    {
        if (Input.GetButton("Fire1") && Time.time >= nextFire)
        {
            nextFire = Time.time + 1f / fireRate;
            var p = Instantiate(projectilePrefab, muzzle.position, muzzle.rotation);
            p.Launch(muzzle.forward * speed);
        }
    }
}