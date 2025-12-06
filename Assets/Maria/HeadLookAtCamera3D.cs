using UnityEngine;

public class HeadLookAtCamera3D : MonoBehaviour
{
    public Transform cameraTransform; // Assign the camera's transform in the inspector
    public Transform childFaceObject; // Assign the child face object in the inspector
    public Canvas uiCanvas; // Assign the UI Canvas in the inspector
    public float rotationSmoothing = 5f; // Smoothing factor for rotation

    void Update()
    {
        if (cameraTransform == null) return;

        // Rotate the 3D object to face the camera
        if (childFaceObject != null)
        {
            Vector3 directionToCamera = cameraTransform.position - childFaceObject.position;
            Quaternion targetRotation = Quaternion.LookRotation(directionToCamera, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSmoothing);
        }

        // Rotate the UI Canvas to face the camera
        if (uiCanvas != null)
        {
            Vector3 directionToCamera = uiCanvas.transform.position - cameraTransform.position; // Flip the direction
            Quaternion targetRotation = Quaternion.LookRotation(directionToCamera, Vector3.up);
            uiCanvas.transform.rotation = targetRotation;
        }
    }
}