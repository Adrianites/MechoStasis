using UnityEngine;
using UnityEngine.InputSystem;

public class InputHandler : MonoBehaviour
{
    [Header("Input Action Asset")]
    [SerializeField] private InputActionAsset playerInput;

    [Header("Action Map Name Reference")]
    [SerializeField] private string actionMapName = "Player";

    [Header("Action Name References")]
    [SerializeField] private string movement = "Move";
    [SerializeField] private string rotation = "Look";
    [SerializeField] private string jump = "Jump";
    [SerializeField] private string attack = "Attack";
    [SerializeField] private string sprint = "Sprint";
    [SerializeField] private string interact = "Interact";
    [SerializeField] private string switchWeapon = "SwitchWeapon";
    [SerializeField] private string pauseGame = "Pause";
    [SerializeField] private string dash = "Dash";
    [SerializeField] private string hammerAttack = "HammerAttack";
    [SerializeField] private string inventoryToggle = "Inventory";
    [SerializeField] private string grapple = "Grapple";

    private InputAction movementAction;
    private InputAction rotationAction;
    private InputAction jumpAction;
    private InputAction attackAction;
    private InputAction sprintAction;
    private InputAction interactAction;
    private InputAction switchWeaponAction;
    private InputAction pauseGameAction;
    private InputAction dashAction;
    private InputAction hammerAttackAction;
    private InputAction inventoryToggleAction;
    private InputAction grappleAction;

    public Vector2 MovementInput {get; private set;}
    public Vector2 RotationInput {get; private set;}
    public bool JumpTriggered {get; private set;}
    public bool AttackTriggered {get; private set;}
    public bool SprintTriggered {get; private set;}
    public bool InteractTriggered {get; private set;}
    public float SwitchWeaponTriggered {get; private set;}
    public bool PauseGameTriggered {get; private set;}
    public bool DashTriggered {get; private set;}
    public bool HammerAttackTriggered {get; private set;}
    public bool InventoryToggleTriggered {get; private set;}
    public bool GrappleTriggered {get; private set;}

    public event System.Action OnJumpPerformed;
    public event System.Action OnJumpCanceled;
    public event System.Action OnAttackPerformed;
    public event System.Action OnAttackCanceled;
    public event System.Action OnInteractPerformed;
    public event System.Action<float> OnSwitchWeaponPerformed;
    public event System.Action OnPausePerformed;
    public event System.Action OnDashPerformed;
    public event System.Action OnHammerAttackPerformed;
    public event System.Action OnInventoryTogglePerformed;
    public event System.Action OnGrapplePerformed;

    private void Awake()
    {
        InputActionMap mapReference = playerInput.FindActionMap(actionMapName);

        movementAction = mapReference.FindAction(movement);
        rotationAction = mapReference.FindAction(rotation);
        jumpAction = mapReference.FindAction(jump);
        attackAction = mapReference.FindAction(attack);
        sprintAction = mapReference.FindAction(sprint);
        interactAction = mapReference.FindAction(interact);
        pauseGameAction = mapReference.FindAction(pauseGame);
        switchWeaponAction = mapReference.FindAction(switchWeapon);
        dashAction = mapReference.FindAction(dash);
        hammerAttackAction = mapReference.FindAction(hammerAttack);
        inventoryToggleAction = mapReference.FindAction(inventoryToggle);
        grappleAction = mapReference.FindAction(grapple);

        SubscribeActionValuesToInputEvents();
    }

    private void SubscribeActionValuesToInputEvents()
    {
        movementAction.performed += inputInfo => MovementInput = inputInfo.ReadValue<Vector2>();
        movementAction.canceled += inputInfo => MovementInput = Vector2.zero;
    
        rotationAction.performed += inputInfo => RotationInput = inputInfo.ReadValue<Vector2>();
        rotationAction.canceled += inputInfo => RotationInput = Vector2.zero;

        jumpAction.performed += inputInfo => { JumpTriggered = true; OnJumpPerformed?.Invoke(); };
        jumpAction.canceled += inputInfo => { JumpTriggered = false; OnJumpCanceled?.Invoke(); };

        attackAction.performed += inputInfo => { AttackTriggered = true; OnAttackPerformed?.Invoke(); };
        attackAction.canceled += inputInfo => { AttackTriggered = false; OnAttackCanceled?.Invoke(); };

        sprintAction.performed += inputInfo => SprintTriggered = true;
        sprintAction.canceled += inputInfo => SprintTriggered = false;

        interactAction.performed += inputInfo => { InteractTriggered = true; OnInteractPerformed?.Invoke(); };
        interactAction.canceled += inputInfo => InteractTriggered = false;

        switchWeaponAction.performed += inputInfo => { float val = inputInfo.ReadValue<float>(); SwitchWeaponTriggered = val; OnSwitchWeaponPerformed?.Invoke(val); };
        switchWeaponAction.canceled += inputInfo => SwitchWeaponTriggered = 0f;

        pauseGameAction.performed += inputInfo => { PauseGameTriggered = true; OnPausePerformed?.Invoke(); };
        pauseGameAction.canceled += inputInfo => PauseGameTriggered = false;

        dashAction.performed += inputInfo => { DashTriggered = true; OnDashPerformed?.Invoke(); };
        dashAction.canceled += inputInfo => DashTriggered = false;

        hammerAttackAction.performed += inputInfo => { HammerAttackTriggered = true; OnHammerAttackPerformed?.Invoke(); };
        hammerAttackAction.canceled += inputInfo => HammerAttackTriggered = false;

        inventoryToggleAction.performed += inputInfo => { InventoryToggleTriggered = true; OnInventoryTogglePerformed?.Invoke(); };
        inventoryToggleAction.canceled += inputInfo => InventoryToggleTriggered = false;

        grappleAction.performed += inputInfo => { GrappleTriggered = true; OnGrapplePerformed?.Invoke(); };
        grappleAction.canceled += inputInfo => GrappleTriggered = false;
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