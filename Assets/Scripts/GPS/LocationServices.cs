using UnityEngine;

namespace GPSBridgeHeadless
{
    /// Subscribes to NmeaClient, parses sentences, exposes CurrentFix,
    /// and prints concise logs for testing
    public class LocationService : MonoBehaviour
    {
        public NmeaClient nmeaClient;
        [Range(0, 0.95f)]
        public float smoothing = GpsBridgeConfig.SMOOTHING;

        public NmeaParser.Fix CurrentFix => _fix;

        NmeaParser.Fix _fix;
        bool _hasPrev;
        double _prevLat, _prevLon;

        void OnEnable()
        {
            if (!nmeaClient) nmeaClient = GetComponent<NmeaClient>();
            if (nmeaClient == null)
            {
                Debug.LogError("[GPS] LocationService requires a NmeaClient on the same GameObject or assigned in Inspector.");
                return;
            }

            nmeaClient.OnStatus += s => Debug.Log($"[GPS] {s}");
            nmeaClient.OnError  += e => Debug.LogError($"[GPS] {e}");
            nmeaClient.OnNmeaSentence += HandleSentence;
        }

        void OnDisable()
        {
            if (nmeaClient != null)
                nmeaClient.OnNmeaSentence -= HandleSentence;
        }

        void HandleSentence(string line)
        {
            var parsed = NmeaParser.Parse(line, _fix);

            if (_hasPrev && smoothing > 0f && parsed.valid)
            {
                parsed.latitude  = _prevLat * (1 - smoothing) + parsed.latitude  * smoothing;
                parsed.longitude = _prevLon * (1 - smoothing) + parsed.longitude * smoothing;
            }

            _fix = parsed;

            if (parsed.valid)
            {
                _prevLat = parsed.latitude;
                _prevLon = parsed.longitude;
                _hasPrev = true;

                Debug.Log($"[GPS] Lat:{parsed.latitude:F6}  Lon:{parsed.longitude:F6}  Alt:{parsed.altitudeMeters:F1}m  " +
                          $"Sats:{parsed.satellites} HDOP:{parsed.hdop:F1}  Speed:{parsed.speedKnots:F1}kn  Head:{parsed.courseDeg:F1}°");
            }
        }
    }
}

