using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.Events;

public class QRCodeManager : MonoBehaviour
{
    [Header("Events")]
    public UnityEvent<string> OnQRDetected;
    public UnityEvent BeepSound;

    public void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable.TrackableType == OVRAnchor.TrackableType.QRCode && trackable.MarkerPayloadString != null)
        {
            var payload = trackable.MarkerPayloadString;
            Debug.Log($"[QRPayload]: {trackable.MarkerPayloadString}");
            BeepSound?.Invoke();
            StartCoroutine(InvokeWithDelay(payload));
        }
    }

    private System.Collections.IEnumerator InvokeWithDelay(string payload)
    {
        yield return new WaitForSeconds(2f);
        OnQRDetected?.Invoke(payload);
    }
}
