using UnityEngine;

public class PressurePlate : MonoBehaviour
{
    public bool isActivated = false;
    

    
    void OnCollisionEnter(Collision other)
    {
            isActivated = true;
            Debug.Log("Pressure plate activated!");
    }

    void OnCollisionExit(Collision other)
    {
        isActivated = false;
        Debug.Log("Pressure plate deactivated!");
    }






}
