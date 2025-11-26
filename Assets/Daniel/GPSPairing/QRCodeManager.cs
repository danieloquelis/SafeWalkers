using Meta.XR.MRUtilityKit;
using UnityEngine;

public class QRCodeManager : MonoBehaviour
{
    public void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable.TrackableType == OVRAnchor.TrackableType.QRCode && trackable.MarkerPayloadString != null)
        {
            var payload = trackable.MarkerPayloadString;
            Debug.Log($"[QRPayload]: {trackable.MarkerPayloadString}");
        }
    }
}
