using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    public int ObjectivesDone { get; private set; }
    public int TotalObjectives { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
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
        }
    }
}
