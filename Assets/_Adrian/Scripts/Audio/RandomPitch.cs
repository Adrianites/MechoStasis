using UnityEngine;

public class RandomPitch : MonoBehaviour
{
    public float minPitch = 0.8f;
    public float maxPitch = 1.2f;

    [SerializeField] private AudioSource audioSource;

    void OnEnable()
    {
        if (audioSource != null)
        {
            audioSource.pitch = Random.Range(minPitch, maxPitch);
        }
    }
}
