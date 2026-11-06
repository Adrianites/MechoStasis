using UnityEngine;
using UnityEngine.InputSystem;

public class UserInputManager : MonoBehaviour
{
    [Header("Input Action Asset")]
    [SerializeField] private InputActionAsset playerInput;

    [Header("Action Map Name Reference")]
    [SerializeField] private string actionMapName = "Player";

    [Header("Action Name References")]
    [SerializeField] private string movement = "Move";
    [SerializeField] private string rotation = "Look";
    [SerializeField] private string freeLook = "FreeLook";
    [SerializeField] private string jump = "Jump";
    [SerializeField] private string dash = "Dash";
    [SerializeField] private string interact = "Interact";
    [SerializeField] private string pickup = "Pickup";
    [SerializeField] private string weaponSwap = "WeaponSwap";
    [SerializeField] private string meleeAttack = "MeleeAttack";
    [SerializeField] private string attack = "Attack";
    [SerializeField] private string sprint = "Sprint";

    private InputAction movementAction;
    private InputAction rotationAction;
    private InputAction freeLookAction;
    private InputAction jumpAction;
    private InputAction dashAction;
    private InputAction interactAction;
    private InputAction pickupAction;
    private InputAction weaponSwapAction;
    private InputAction meleeAttackAction;
    private InputAction attackAction;
    private InputAction sprintAction;

    public Vector2 MovementInput {get; private set;}
    public Vector2 RotationInput {get; private set;}
    public bool FreeLookTriggered {get; private set;}
    public bool JumpTriggered {get; private set;}
    public bool DashTriggered {get; private set;}
    public bool InteractTriggered {get; private set;}
    public bool PickupTriggered {get; private set;}
    public float WeaponSwapTriggered {get; private set;}
    public bool MeleeAttackTriggered {get; private set;}
    public bool AttackTriggered {get; private set;}
    public bool SprintTriggered {get; private set;}

    public event System.Action OnJumpPerformed;
    public event System.Action OnJumpCanceled;
    public event System.Action OnDashPerformed;
    public event System.Action OnDashCanceled;
    public event System.Action OnInteractPerformed;
    public event System.Action OnInteractCanceled;
    public event System.Action OnPickupPerformed;
    public event System.Action OnPickupCanceled;
    public event System.Action OnWeaponSwapPerformed;
    public event System.Action OnWeaponSwapCanceled;
    public event System.Action OnMeleeAttackPerformed;
    public event System.Action OnMeleeAttackCanceled;
    public event System.Action OnAttackPerformed;
    public event System.Action OnAttackCanceled;
    public event System.Action OnSprintPerformed;
    public event System.Action OnSprintCanceled;

    private void Awake()
    {
        InputActionMap actionMap = playerInput.FindActionMap(actionMapName);

        movementAction = playerInput.FindActionMap(actionMapName).FindAction(movement);
        rotationAction = playerInput.FindActionMap(actionMapName).FindAction(rotation);
        freeLookAction = playerInput.FindActionMap(actionMapName).FindAction(freeLook);
        jumpAction = playerInput.FindActionMap(actionMapName).FindAction(jump);
        dashAction = playerInput.FindActionMap(actionMapName).FindAction(dash);
        interactAction = playerInput.FindActionMap(actionMapName).FindAction(interact);
        pickupAction = playerInput.FindActionMap(actionMapName).FindAction(pickup);
        weaponSwapAction = playerInput.FindActionMap(actionMapName).FindAction(weaponSwap);
        meleeAttackAction = playerInput.FindActionMap(actionMapName).FindAction(meleeAttack);
        attackAction = playerInput.FindActionMap(actionMapName).FindAction(attack);
        sprintAction = playerInput.FindActionMap(actionMapName).FindAction(sprint);

        SubscribeActionValuesToInputEvents();

    }

    private void SubscribeActionValuesToInputEvents()
    {
        jumpAction.performed += ctx => { JumpTriggered = true; OnJumpPerformed?.Invoke(); };
        jumpAction.canceled += ctx => { JumpTriggered = false; OnJumpCanceled?.Invoke(); };

        dashAction.performed += ctx => { DashTriggered = true; OnDashPerformed?.Invoke(); };
        dashAction.canceled += ctx => { DashTriggered = false; OnDashCanceled?.Invoke(); };

        interactAction.performed += ctx => { InteractTriggered = true; OnInteractPerformed?.Invoke(); };
        interactAction.canceled += ctx => { InteractTriggered = false; OnInteractCanceled?.Invoke(); };

        pickupAction.performed += ctx => { PickupTriggered = true; OnPickupPerformed?.Invoke(); };
        pickupAction.canceled += ctx => { PickupTriggered = false; OnPickupCanceled?.Invoke(); };

        weaponSwapAction.performed += ctx => { WeaponSwapTriggered = ctx.ReadValue<float>(); OnWeaponSwapPerformed?.Invoke(); };
        weaponSwapAction.canceled += ctx => { WeaponSwapTriggered = 0f; OnWeaponSwapCanceled?.Invoke(); };

        meleeAttackAction.performed += ctx => { MeleeAttackTriggered = true; OnMeleeAttackPerformed?.Invoke(); };
        meleeAttackAction.canceled += ctx => { MeleeAttackTriggered = false; OnMeleeAttackCanceled?.Invoke(); };

    }

    private void OnEnable()
    {
        playerInput.FindActionMap(actionMapName).Enable();
    }

    private void OnDisable()
    {
        playerInput.FindActionMap(actionMapName).Disable();
    }

    





}
