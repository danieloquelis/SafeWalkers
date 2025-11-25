using UnityEngine;

public class LeftHandMenu : MonoBehaviour
{
    [SerializeField] private GameObject menuRoot; // assign your hand menu root in Inspector
    
    void Update()
    {
        // Left controller menu button
        if (OVRInput.GetDown(OVRInput.Button.Start, OVRInput.Controller.LTouch))
        {
            ToggleMenu();
        }
    }

    // Your Unity function – can do whatever you want
    public void ToggleMenu()
    {
        if (menuRoot == null) return;

        bool newState = !menuRoot.activeSelf;
        menuRoot.SetActive(newState);
        Debug.Log("Left-hand menu toggled: " + newState);
    }
}
