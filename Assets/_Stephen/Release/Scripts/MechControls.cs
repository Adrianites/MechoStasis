using UnityEngine;
using System.Collections;
using Unity.Cinemachine;
using Unity.UI;

public class MechControls : MonoBehaviour
{
   /* [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float currentMoveSpeed;

    [Header("Camera Settings")]
    [SerializeField] private float sensitivity = 1f;
    [SerializeField] private float mouseSensitivity = 100f;


    [Header("Rotation Settings")]
    [SerializeField] private float rotationSpeed = 100f;
    
    [Header("References")]
    [SerializeField] private Transform mechTransform;
    [SerializeField] private Rigidbody mechBody;
    [SerializeField] private UserInputManager playerInput;
    [SerializeField] private Camera mechCamera;

    Rigidbody rb;

    private Vector3 mechMovement;



   

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
        rb.MovePosition(rb.position + mechMovement * Time.fixedDeltaTime * currentMoveSpeed);
    }

    private void HandleMovement()
    {
      //Vector2 mechInput = playerInput.MovementInput;        
      mechMovement = new Vector3(playerInput.MovementInput.x, 0, playerInput.MovementInput.y);
      //mechMovement = new Vector3(input.x, 0, input.y);
      rb.AddForce(mechMovement * currentMoveSpeed);
    }

    private void HandleRotation()
    {
        Vector2 lookInput = playerInput.RotationInput;

        float mouseX = lookInput.x * mouseSensitivity * Time.deltaTime;

        float mouseY = lookInput.y * mouseSensitivity * Time.deltaTime;


    } */
        
    

}
