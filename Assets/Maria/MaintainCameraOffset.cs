using UnityEngine;

public class MaintainCameraOffset : MonoBehaviour
{
    public Transform cameraTransform;
    public float distance = 5f;  // how far from the camera on the XZ plane

    void LateUpdate()
    {
        // Project the camera->object direction onto the XZ plane
        Vector3 dir = transform.position - cameraTransform.position;
        dir.y = 0f;

        // If the object is exactly above/below the camera, default to its forward
        if (dir.sqrMagnitude < 0.0001f)
        {
            dir = cameraTransform.forward;
            dir.y = 0f;
        }

        dir.Normalize();

        // Keep fixed XZ distance
        Vector3 targetPos = cameraTransform.position + dir * distance;

        // Keep the object's current Y position
        targetPos.y = transform.position.y;

        transform.position = targetPos;
    }
}