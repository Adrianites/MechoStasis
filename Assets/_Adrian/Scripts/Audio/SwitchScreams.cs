using UnityEngine;
using UnityEngine.Audio;
using System.Collections;

public class SwitchScreams : MonoBehaviour
{
    public AudioSource audioSource;
    public AudioClip[] screamClips;
    public float delayBetweenScreams = 10f;

    public void Start()
    {
        if (audioSource != null)
        {
            StartCoroutine(DelaySwitchScream(delayBetweenScreams));
        }
        else
        {
            Debug.LogWarning("AudioSource not assigned.");
        }
    }
    public void SwitchScream()
    {
        if (screamClips.Length == 0)
        {
            Debug.LogWarning("No scream clips assigned.");
            return;
        }

        int randomIndex = Random.Range(0, screamClips.Length);
        audioSource.clip = screamClips[randomIndex];
        audioSource.Play();
    }

    public IEnumerator DelaySwitchScream(float delay)
    {
        yield return new WaitForSeconds(delay);
        SwitchScream();
    }
}
