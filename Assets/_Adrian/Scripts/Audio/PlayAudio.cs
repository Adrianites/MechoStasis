using UnityEngine;

public class PlayAudio : MonoBehaviour
{
    public AudioSource audioSource;
    public AudioClip attackAudioClip;
    public AudioClip deathAudioClip;
    public AudioClip attack2AudioClip;
 

    public void PlayDeathAudio()
    {
        if (audioSource != null && deathAudioClip != null)
        {
            audioSource.PlayOneShot(deathAudioClip);
        }
    }

    public void PlayAttackAudio()
    {
        if (audioSource != null && attackAudioClip != null)
        {
            audioSource.PlayOneShot(attackAudioClip);
        }
    }

    public void PlayAttack2Audio()
    {
        if (audioSource != null && attack2AudioClip != null)
        {
            audioSource.PlayOneShot(attack2AudioClip);
        }
    }
}
