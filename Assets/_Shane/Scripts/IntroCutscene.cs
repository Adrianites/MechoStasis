using UnityEngine;
using UnityEngine.Playables;

public class IntroCutscene : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayableDirector director;
    [SerializeField] private GameObject cutsceneCamera;

    [Header("Player")]
    [SerializeField] private Behaviour[] disableDuringCutscene;

    private void Start()
    {
        if (director == null || director.playableAsset == null)
        {
            SetCutsceneActive(false);
            return;
        }

        director.stopped += OnDirectorStopped;
        SetCutsceneActive(true);
        director.Play();
        Time.timeScale = 0f;
    }

    private void OnDestroy()
    {
        if (director != null)
            director.stopped -= OnDirectorStopped;
    }

    private void OnDirectorStopped(PlayableDirector stoppedDirector)
    {
        director.stopped -= OnDirectorStopped;
        SetCutsceneActive(false);
    }

    private void SetCutsceneActive(bool active)
    {
        if (cutsceneCamera != null)
            cutsceneCamera.SetActive(active);

        foreach (Behaviour behaviour in disableDuringCutscene)
        {
            if (behaviour != null)
                behaviour.enabled = !active;
        }
    }

    public void ResetTime()
    {
        Time.timeScale = 1f;
    }
}
