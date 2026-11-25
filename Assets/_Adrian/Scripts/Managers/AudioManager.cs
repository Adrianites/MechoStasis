using UnityEngine;

public class AudioManager : MonoBehaviour
{
    void Awake()
    {   
        if (FindObjectsByType<AudioManager>(FindObjectsSortMode.None).Length > 1)
        {
            Destroy(gameObject);
        }
        else
        {
            DontDestroyOnLoad(gameObject);
        }
    }
}
