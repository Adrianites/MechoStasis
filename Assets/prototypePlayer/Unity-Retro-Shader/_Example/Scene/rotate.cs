using UnityEngine;

public class rotate : MonoBehaviour
{
    float maxAngle = 30f;
    float speed = 2f;
    float baseY;

    void Start()
    {
        baseY = transform.localEulerAngles.y;
    }

    void Update()
    {
        float angleOffset = Mathf.Sin(Time.time * speed) * maxAngle;
        transform.localRotation = Quaternion.Euler(0f, baseY + angleOffset, 0f);
    }
}
