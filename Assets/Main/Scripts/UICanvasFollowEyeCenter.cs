using UnityEngine;

/// <summary>
/// Keeps a UI canvas (or any parent GameObject) centered in front of the user's view.
/// Attach this script to the parent object that holds your canvas, and assign the
/// eye/camera transform (e.g. CenterEyeAnchor or the main camera).
/// </summary>
public class UICanvasFollowEyeCenter : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Transform representing the user's eye center (e.g. XR camera or CenterEyeAnchor).")]
    [SerializeField] private Transform eyeCenter;

    [Header("Positioning")]
    [Tooltip("Distance in front of the eye center to place the UI.")]
    [SerializeField] private float distanceFromEye = 1.5f;

    [Tooltip("Additional local offset from the eye-forward position.")]
    [SerializeField] private Vector3 localOffset = Vector3.zero;

    [Header("Rotation")]
    [Tooltip("If true, the canvas will always rotate to face the eye center.")]
    [SerializeField] private bool faceEyeCenter = true;

    [Header("Smoothing")]
    [Tooltip("How quickly the UI follows the eye center position. Higher is snappier.")]
    [SerializeField] private float positionLerpSpeed = 8f;
    [Tooltip("How quickly the UI rotates to face the eye center.")]
    [SerializeField] private float rotationLerpSpeed = 10f;

    private void Awake()
    {
        // Fallback to the main camera if no eye transform is assigned.
        if (eyeCenter == null && Camera.main != null)
        {
            eyeCenter = Camera.main.transform;
        }
    }

    private void LateUpdate()
    {
        if (eyeCenter == null)
        {
            return;
        }

        // Position the parent directly in front of the eye center at the desired distance.
        Vector3 targetPosition = eyeCenter.position + eyeCenter.forward * distanceFromEye;
        targetPosition += eyeCenter.TransformVector(localOffset);

        // Smoothly move towards target position.
        if (positionLerpSpeed > 0f)
        {
            transform.position = Vector3.Lerp(
                transform.position,
                targetPosition,
                Time.deltaTime * positionLerpSpeed);
        }
        else
        {
            transform.position = targetPosition;
        }

        if (faceEyeCenter)
        {
            // Make the UI look back at the eye center (so its front faces the user).
            Vector3 lookDirection = transform.position - eyeCenter.position;
            if (lookDirection.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);

                if (rotationLerpSpeed > 0f)
                {
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        targetRotation,
                        Time.deltaTime * rotationLerpSpeed);
                }
                else
                {
                    transform.rotation = targetRotation;
                }
            }
        }
    }
}


