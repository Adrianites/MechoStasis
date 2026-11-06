using UnityEngine;
using System.Collections;
using Unity.Cinemachine;
using Unity.UI;

public class MechControls : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float currentMoveSpeed;
    
    [Header("Rotation Settings")]
    [SerializeField] private float rotationSpeed = 100f;
    
    [Header("References")]
    [SerializeField] private Transform mechTransform;
    [SerializeField] private Rigidbody mechRigidbody;
    [SerializeField] private UserInputManager playerInput;
    [SerializeField] private Camera mechCamera;


    Rigidbody rb;

   

    private void Start()

    {
        rb = gameObject.GetComponent<Rigidbody>();
        currentMoveSpeed = moveSpeed;
        playerInput = GetComponent<UserInputManager>();
    }

    private void Update() //read inputs
    {
        HandleMovement();
        HandleRotation();
    }

    private void FixedUpdate() //apply physics
    {
        rb.MovePosition(rb.position + new Vector3(1,0,0) * Time.fixedDeltaTime * currentMoveSpeed);
    }

    private void HandleMovement()
    {
      Vector2 input = playerInput.MovementInput;
      rb.AddForce(new Vector3(input.x, 0, input.y) * currentMoveSpeed);

    }

    private void HandleRotation()
    {
        
    }
}
