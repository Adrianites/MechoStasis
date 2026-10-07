using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInput : MonoBehaviour
{
    public Vector3 MoveInput { get; private set; } = Vector3.zero;
    
    public bool MenuInput { get; private set; } = false;

    public bool ActionInput { get; private set; } = false;

    public Rigidbody rb;
    public float moveSpeed;
    private Vector3 moveDirection;

    public InputActionReference move;


    private void OnEnable() //this will subscribe to inputs (remeber to update each time)
    {

    }

    private void OnDisable() //this will unsubscribe to inputs
    {


    }

    private void DFixedUpdate()
    {
        moveDirection = move.action.ReadValue<Vector3>();
    }

    private void FixedUpdate()
    {
        MoveInput = new Vector3(moveDirection.x, moveDirection.y, moveDirection.z);
        rb.MovePosition(rb.position + MoveInput * moveSpeed * Time.deltaTime);
    }

    



}
