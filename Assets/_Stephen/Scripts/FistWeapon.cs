using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class FistWeapon : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private InputHandler playerInputHandler;

    [Header("Melee Attack")]
    [SerializeField] public int Damage = 10;


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

        playerInputHandler.OnMeleeAttackPerformed += OnMeleeAttackInput;
        playerInputHandler.OnMeleeAttackPerformed += OnMeleeAttackInputCanceled;
    }

    private void OnDisable()
    {
        if (playerInputHandler == null)
        {
            return;
        }

        playerInputHandler.OnMeleeAttackPerformed -= OnMeleeAttackInput;
        playerInputHandler.OnMeleeAttackPerformed -= OnMeleeAttackInputCanceled;
    }

    private void OnMeleeAttackInput()
    {
        if (!gameObject.activeInHierarchy)
        {
            return;
        }
        
        Attack();
        Debug.Log("Melee Attack Input Triggered");

    }

    private void OnMeleeAttackInputCanceled()
    {
        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        Debug.Log("Melee Attack Input Canceled");
    }

    private void Attack()
    {
        Debug.Log("Melee Attack Triggered");
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Enemy"))
        {
            Debug.Log("Hit Enemy with Fist! Damage: " + Damage);
        }
    }
}
