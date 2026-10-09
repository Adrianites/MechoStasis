using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    public int ObjectivesDone { get; private set; }
    public int TotalObjectives { get; private set; }
    public GameObject winCanvas;
    public MechController mechController;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (winCanvas == null)
        {
            winCanvas = GameObject.Find("WinCanvas");
        }
        winCanvas.SetActive(false);

        if (mechController == null)
        {
            mechController = FindObjectsByType<MechController>(FindObjectsSortMode.None)[0];
        }
    }

    public void Start()
    {
        ObjectivesDone = 0;
        TotalObjectives = FindObjectsByType<Generator>(FindObjectsSortMode.None).Length;
    }

    public void IncrementObjectivesDone()
    {
        ObjectivesDone++;

        if (ObjectivesDone >= TotalObjectives)
        {
            Debug.Log("All objectives completed!");
            winCanvas.SetActive(true);
            mechController.isInvincible = true;
        }
    }
}
