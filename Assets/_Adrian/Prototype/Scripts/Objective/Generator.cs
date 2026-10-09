using UnityEngine;

public class Generator : MonoBehaviour
{
    public bool activated = false;
    public InputHandler playerInputHandler;
    public Renderer buttonRenderer;
    public Light generatorLight;

    private void Start()
    {
        if (playerInputHandler == null)
        {
            playerInputHandler = FindObjectsByType<InputHandler>(FindObjectsSortMode.None)[0];
        }
        
        buttonRenderer.material.color = Color.red;
        generatorLight.color = Color.red;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player") || activated)
        {
            return;
        }
        playerInputHandler = other.GetComponentInParent<InputHandler>();

        if (playerInputHandler != null)
        {
            playerInputHandler.OnInteractPerformed += ToggleInteract;
        }
    }


    public void ToggleInteract()
    {
        if (activated)
        {
            return;
        }
        buttonRenderer.material.color = Color.green;
        generatorLight.color = Color.green;
        activated = true;
        
        GameManager.Instance.IncrementObjectivesDone();
    }

    private void OnDisable()
    {
        if (playerInputHandler == null)
        {
            return;
        }

        playerInputHandler.OnInteractPerformed -= ToggleInteract;
    }
}
