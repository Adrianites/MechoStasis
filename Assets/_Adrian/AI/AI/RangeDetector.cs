using UnityEngine;

public class RangeDetector : MonoBehaviour
{
    [SerializeField] private float detectionRadius = 25f;
    [SerializeField] private LayerMask playerMask;
    [SerializeField] private bool showGizmos = true;

    public GameObject detectedTarget
    {
        get;
        set;
    }

    public GameObject UpdateDetector()
    {
        Collider[] colliders = Physics.OverlapSphere(transform.position, detectionRadius, playerMask);

        if(colliders.Length > 0)
        {
            detectedTarget = colliders[0].gameObject;
        }
        else
        {
            detectedTarget = null;
        }
        return detectedTarget;
    }

    private void OnDrawGizmos()
    {
        if (showGizmos && enabled)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, detectionRadius);
        }
    }
}
