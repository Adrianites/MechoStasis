using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

public class GunVarient1 : MonoBehaviour, IGun
{
    [Header("References")]
    [SerializeField] private InputHandler playerInputHandler;
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform muzzle;
    [SerializeField] private CinemachineImpulseSource shootingImpulseSource;

    [Header("Shooting")]
    [SerializeField] private float speed = 25f;
    [SerializeField] private float fireRate = 8f;

    private bool attackHeld;
    private bool canShoot = true;
    private Coroutine attackCoroutine;

    private void Awake()
    {
        if (playerInputHandler == null)
        {
            playerInputHandler = GetComponentInParent<InputHandler>();
        }
    }

    private void OnEnable()
    {
        if (playerInputHandler == null)
        {
            return;
        }

        playerInputHandler.OnAttackPerformed += OnAttackInput;
        playerInputHandler.OnAttackCanceled += OnAttackInputCanceled;
    }

    private void OnDisable()
    {
        if (playerInputHandler == null)
        {
            return;
        }

        playerInputHandler.OnAttackPerformed -= OnAttackInput;
        playerInputHandler.OnAttackCanceled -= OnAttackInputCanceled;
    }

    public void Shoot()
    {
        OnAttackInput();
    }

    private void OnAttackInput()
    {
        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        attackHeld = true;

        if (attackCoroutine == null)
        {
            attackCoroutine = StartCoroutine(AttackLoop());
        }
    }

    private void OnAttackInputCanceled()
    {
        attackHeld = false;
    }

    private IEnumerator AttackLoop()
    {
        while (attackHeld)
        {
            if (canShoot)
            {
                yield return StartCoroutine(GunShoot());
            }

            yield return null;
        }

        attackCoroutine = null;
    }

    private IEnumerator GunShoot()
    {
        if (projectilePrefab == null || muzzle == null)
        {
            yield break;
        }

        canShoot = false;

        if (CameraShakeManager.instance != null)
        {
            CameraShakeManager.instance.CameraShake(shootingImpulseSource);
        }

        SpawnProjectile(projectilePrefab, muzzle, speed);

        yield return new WaitForSeconds(1f / fireRate);
        canShoot = true;
    }

    private void SpawnProjectile(GameObject projectilePrefab, Transform spawnPoint, float projectileSpeed)
    {
        GameObject projectileInstance = Instantiate(projectilePrefab, spawnPoint.position, spawnPoint.rotation);

        Projectile projectile = projectileInstance.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.Launch(spawnPoint.up * projectileSpeed);
            return;
        }

        Rigidbody rigidbody = projectileInstance.GetComponent<Rigidbody>();
        if (rigidbody != null)
        {
            rigidbody.linearVelocity = spawnPoint.up * projectileSpeed;
        }
    }
}
