using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
public class FistWeapon : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private InputHandler playerInputHandler;
    [SerializeField] private Animator animator;
    [SerializeField] private Collider fistCollider;

    [Header("Settings")]
    [SerializeField] private float speed = 25f;
    
    private bool attackHeld;

    private void Awake()
    {
        if (playerInputHandler == null)
        {
            playerInputHandler = GetComponentInParent<InputHandler>();
        }

        if (fistCollider != null)
        {
            fistCollider.enabled = false;
        }
    }

    private void OnEnable()
    {
        if (playerInputHandler == null)
        {
            return;
        }

        playerInputHandler.OnMeleeAttackPerformed += OnAttackInput;
        playerInputHandler.OnMeleeAttackCanceled += OnMeleeAttackCanceled;
    }

    private void OnDisable()
    {
        attackHeld = false;

        if (fistCollider != null)
        {
            fistCollider.enabled = false;
        }

        if (playerInputHandler == null)
        {
            return;
        }

        playerInputHandler.OnMeleeAttackPerformed -= OnAttackInput;
        playerInputHandler.OnMeleeAttackCanceled -= OnMeleeAttackCanceled;
    }

    private void OnAttackInput()
    {
        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        attackHeld = true;

        if (attackHeld)
        {
            StartCoroutine(MeleeAttackLoop());
        }
    }

    private void OnMeleeAttackCanceled()
    {
        attackHeld = false;
        if (fistCollider != null)
        {
            fistCollider.enabled = false;
        }
        Debug.Log("Melee attack canceled!");
    }

    private void MeleeAttack()
    {
        if (attackHeld)
        {
            Debug.Log("Melee attack performed!");
            /*animators[0].SetTrigger(AnimStrings.Punch); 
            animators[1].SetTrigger(AnimStrings.Punch); */
            animator.SetTrigger(AnimStrings.Punch);
            if (fistCollider != null)
            {
                fistCollider.enabled = true;
            }
        }
        else
        {
            Debug.Log("Melee attack canceled!");
            if (fistCollider != null)
            {
                fistCollider.enabled = false;
            }
        }
    }

     private IEnumerator MeleeAttackLoop()
    {
        while (attackHeld)
        {
            MeleeAttack();
            yield return new WaitForSeconds(1f / speed);
        }
    }

}
