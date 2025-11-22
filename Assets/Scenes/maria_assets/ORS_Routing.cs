using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;

public class ORS_Routing : MonoBehaviour
{
    [Header("ORS Settings")]
    public string orsApiKey = "YOUR_ORS_API_KEY";
    public string profile = "foot-walking";
    public bool simplifyRoute = true;

    [Header("Routing Settings")]
    public Vector2 startLatLon;    // lat, lon
    public Vector2 endLatLon;      // lat, lon
    public float offRouteThreshold = 2f; // meters
    public float requestCooldown = 1f;  

    [Header("Visualization")]
    public LineRenderer lineRenderer;

    [Header("XR Rig")]
    public Transform xrRig; // assign your XR Origin prefab here

    private List<Vector3> cachedRoute = new List<Vector3>();
    private bool routeReady = false;
    private bool routeRequestInProgress = false;
    private float lastRequestTime = 0f;
    private bool offRouteTriggered = false;

    private const float EarthRadius = 6371000f; // meters

    void Start()
    {
        if (xrRig == null)
            Debug.LogWarning("XR Rig not assigned. Route will not snap automatically.");

        StartCoroutine(RequestRoute(startLatLon, endLatLon, snapPlayer: true));
    }

    void Update()
    {
        if (!routeReady || routeRequestInProgress || Time.time - lastRequestTime < requestCooldown)
            return;
        if (Camera.main == null) return;

        Vector3 playerPos = xrRig != null ? xrRig.position : Camera.main.transform.position;
        float dist = DistanceToRoute(playerPos, cachedRoute);

        if (dist > offRouteThreshold && !offRouteTriggered)
        {
            offRouteTriggered = true;
            Vector3 nearestPoint = FindNearestPointOnRoute(playerPos, cachedRoute);
            Vector2 newStartLatLon = UnityToLatLon(nearestPoint);
            lastRequestTime = Time.time;
            StartCoroutine(RequestRoute(newStartLatLon, endLatLon));
        }
        else if (dist <= offRouteThreshold)
        {
            offRouteTriggered = false;
        }
    }

    IEnumerator RequestRoute(Vector2 start, Vector2 end, bool snapPlayer = false)
    {
        routeRequestInProgress = true;

        string url = $"https://api.openrouteservice.org/v2/directions/{profile}";
        string body = $@"{{
            ""coordinates"": [[{start.y},{start.x}],[{end.y},{end.x}]],
            ""instructions"": false,
            ""geometry_simplify"": {(simplifyRoute ? "true" : "false")}
        }}";

        UnityWebRequest req = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(body);
        req.uploadHandler = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Authorization", orsApiKey);

        Debug.Log($"Requesting ORS route: start=[{start.x},{start.y}] end=[{end.x},{end.y}]");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"ORS Routing Error ({req.responseCode}): {req.error}");
            routeRequestInProgress = false;
            yield break;
        }

        if (req.responseCode == 429)
        {
            Debug.LogWarning("ORS limit reached: Too many requests.");
            routeRequestInProgress = false;
            yield break;
        }

        ParseRoute(req.downloadHandler.text);
        DrawRoute();
        routeReady = true;
        routeRequestInProgress = false;

        // Snap XR Rig or Main Camera to start of route
        if (snapPlayer && cachedRoute.Count > 0)
        {
            if (xrRig != null)
                xrRig.position = cachedRoute[0];
            else if (Camera.main != null)
                Camera.main.transform.position = cachedRoute[0];
        }
    }

    void ParseRoute(string json)
    {
        cachedRoute.Clear();
        JObject obj = JObject.Parse(json);

        if (obj["error"] != null)
        {
            Debug.LogError("ORS Error: " + obj["error"]["message"]);
            return;
        }

        string encoded = obj["routes"]?[0]?["geometry"]?.ToString();
        if (string.IsNullOrEmpty(encoded))
        {
            Debug.LogError("ORS: No geometry found in response.");
            return;
        }

        List<Vector2> latLonList = DecodePolyline(encoded);
        foreach (var ll in latLonList)
            cachedRoute.Add(LatLonToUnity(ll.x, ll.y));

        Debug.Log("Decoded " + cachedRoute.Count + " route points.");
    }

    public static List<Vector2> DecodePolyline(string encoded)
    {
        List<Vector2> polyline = new List<Vector2>();
        int index = 0, lat = 0, lon = 0;

        while (index < encoded.Length)
        {
            int b, shift = 0, result = 0;
            do { b = encoded[index++] - 63; result |= (b & 0x1f) << shift; shift += 5; } while (b >= 0x20);
            int dlat = (result & 1) != 0 ? ~(result >> 1) : (result >> 1); lat += dlat;

            shift = 0; result = 0;
            do { b = encoded[index++] - 63; result |= (b & 0x1f) << shift; shift += 5; } while (b >= 0x20);
            int dlon = (result & 1) != 0 ? ~(result >> 1) : (result >> 1); lon += dlon;

            polyline.Add(new Vector2(lat * 1e-5f, lon * 1e-5f));
        }

        return polyline;
    }

    Vector3 LatLonToUnity(float lat, float lon)
    {
        float lat0 = startLatLon.x * Mathf.Deg2Rad;
        float lon0 = startLatLon.y * Mathf.Deg2Rad;
        float latRad = lat * Mathf.Deg2Rad;
        float lonRad = lon * Mathf.Deg2Rad;

        float x = (lonRad - lon0) * Mathf.Cos(lat0) * EarthRadius;
        float z = (latRad - lat0) * EarthRadius;
        return new Vector3(x, 0.05f, z); // slightly above ground
    }

    Vector2 UnityToLatLon(Vector3 pos)
    {
        float lat0 = startLatLon.x * Mathf.Deg2Rad;
        float lon0 = startLatLon.y * Mathf.Deg2Rad;

        float latRad = pos.z / EarthRadius + lat0;
        float lonRad = pos.x / (EarthRadius * Mathf.Cos(lat0)) + lon0;

        return new Vector2(latRad * Mathf.Rad2Deg, lonRad * Mathf.Rad2Deg);
    }

    void DrawRoute()
    {
        if (lineRenderer == null) return;
        lineRenderer.positionCount = cachedRoute.Count;
        lineRenderer.SetPositions(cachedRoute.ToArray());
    }

    float DistanceToRoute(Vector3 point, List<Vector3> route)
    {
        if (route.Count < 2) return Mathf.Infinity;
        float minDist = float.MaxValue;

        for (int i = 0; i < route.Count - 1; i++)
        {
            float dist = DistancePointToSegment(point, route[i], route[i + 1]);
            if (dist < minDist) minDist = dist;
        }
        return minDist;
    }

    float DistancePointToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ap = p - a;
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / ab.sqrMagnitude);
        return Vector3.Distance(p, a + ab * t);
    }

    Vector3 FindNearestPointOnRoute(Vector3 point, List<Vector3> route)
    {
        if (route.Count == 0) return point;

        float minDist = float.MaxValue;
        Vector3 nearest = route[0];

        for (int i = 0; i < route.Count - 1; i++)
        {
            Vector3 closest = ClosestPointOnSegment(point, route[i], route[i + 1]);
            float dist = Vector3.Distance(point, closest);
            if (dist < minDist) { minDist = dist; nearest = closest; }
        }
        return nearest;
    }

    Vector3 ClosestPointOnSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ap = p - a;
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / ab.sqrMagnitude);
        return a + ab * t;
    }
}
