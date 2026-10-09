using System.Collections;
using UnityEngine;

public class WaveSpawner : MonoBehaviour
{
    [SerializeField] GameObject enemyPrefab;
    [SerializeField] Transform[] spawnPoints;
    [SerializeField] float timeBetweenWaves = 5f;
    [SerializeField] float timeBetweenSpawns = 0.5f;
    [SerializeField] int baseCount = 3;

    public int Wave { get; private set; }

    void Start() => StartCoroutine(RunWaves());

    IEnumerator RunWaves()
    {
        while (true)
        {
            Wave++;
            int count = baseCount + Wave * 2; // simple difficulty ramp
            Debug.Log($"Wave {Wave}: spawning {count}");

            for (int i = 0; i < count; i++)
            {
                var point = spawnPoints[Random.Range(0, spawnPoints.Length)];
                Instantiate(enemyPrefab, point.position, point.rotation);
                yield return new WaitForSeconds(timeBetweenSpawns);
            }
            yield return new WaitForSeconds(timeBetweenWaves);
        }
    }
}