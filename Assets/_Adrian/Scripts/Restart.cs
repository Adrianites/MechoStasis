using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

public class Restart : MonoBehaviour
{
    public TMP_Text CountDownText;
    public int CountDownTime = 5;

    private void Start()
    {
        StartCoroutine(StartCountdownRestart());
    }

    private IEnumerator StartCountdownRestart()
    {
        for (int i = CountDownTime; i >= 0; i--)
        {
            CountDownText.text = "Restarting in: " + i;
            yield return new WaitForSeconds(1f);
        }

        CountDownText.text = "Restarting now...";
        RestartGame();
    }   

    public void RestartGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
