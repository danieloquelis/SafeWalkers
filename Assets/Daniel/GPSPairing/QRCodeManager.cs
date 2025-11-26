using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.Events;

public class QRCodeManager : MonoBehaviour
{
    public UnityEvent<string> OnQRDetected;
    public void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable.TrackableType == OVRAnchor.TrackableType.QRCode && trackable.MarkerPayloadString != null)
        {
            var payload = trackable.MarkerPayloadString;
            Debug.Log($"[QRPayload]: {trackable.MarkerPayloadString}");
            OnQRDetected?.Invoke(payload);
        }
    }
}
