using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;

public class ORS_Routing : MonoBehaviour
{
    [Header("ORS Settings")]
    public string orsApiKey = "";
    public string profile = "foot-walking";
    public bool simplifyRoute = true;

    [Header("Routing Settings")]
    public Vector2 startLatLon;
    public Vector2 endLatLon;
    public float offRouteThreshold = 2f; // meters

    [Header("Visualization")]
    public LineRenderer lineRenderer;

    [Header("XR Rig")]
    public Transform xrRig;

    private List<Vector3> cachedRoute = new List<Vector3>();
    private bool routeReady = false;
    private bool routeRequestInProgress = false;

    public const float EarthRadius = 6371000f;

    void Start()
    {
        if (xrRig == null)
            Debug.LogWarning("XR Rig not assigned. Route will not snap automatically.");

        string loadedKey = ORSConfig.GetApiKey();
        if (string.IsNullOrEmpty(loadedKey))
        {
            Debug.LogError("ORS_Routing: ORS API key could not be loaded. Aborting initial route request.");
            return;
        }

        orsApiKey = loadedKey;

        StartCoroutine(RequestRoute(startLatLon, endLatLon, snapPlayer: true));
    }

    void Update()
    {
        if (!routeReady || routeRequestInProgress) return;
        if (Camera.main == null) return;

        Vector3 playerPos = xrRig != null ? xrRig.position : Camera.main.transform.position;
        float dist = DistanceToRoute(playerPos, cachedRoute);

        if (dist > offRouteThreshold)
        {
            Debug.LogWarning("You are off the planned route!");
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

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"ORS Routing Error ({req.responseCode}): {req.error}");
            routeRequestInProgress = false;
            yield break;
        }

        ParseRoute(req.downloadHandler.text);
        DrawRoute();
        routeReady = true;
        routeRequestInProgress = false;

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

    public Vector3 LatLonToUnity(float lat, float lon)
    {
        float lat0 = startLatLon.x * Mathf.Deg2Rad;
        float lon0 = startLatLon.y * Mathf.Deg2Rad;
        float latRad = lat * Mathf.Deg2Rad;
        float lonRad = lon * Mathf.Deg2Rad;

        float x = (lonRad - lon0) * Mathf.Cos(lat0) * EarthRadius;
        float z = (latRad - lat0) * EarthRadius;
        return new Vector3(x, 0.05f, z);
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

    // ------------------------------
    // PUBLIC GETTERS
    // ------------------------------
    public List<Vector3> GetRoutePoints() => cachedRoute;
    public bool RouteIsReady() => routeReady;

    public List<Vector2> GetRouteLatLonPoints()
    {
        List<Vector2> latLonList = new List<Vector2>();
        foreach (var worldPoint in cachedRoute)
        {
            latLonList.Add(UnityToLatLon(worldPoint));
        }
        return latLonList;
    }

    public Vector2 UnityToLatLon(Vector3 pos)
    {
        float lat0 = startLatLon.x * Mathf.Deg2Rad;
        float lon0 = startLatLon.y * Mathf.Deg2Rad;

        float latRad = pos.z / EarthRadius + lat0;
        float lonRad = pos.x / (EarthRadius * Mathf.Cos(lat0)) + lon0;

        return new Vector2(latRad * Mathf.Rad2Deg, lonRad * Mathf.Rad2Deg);
    }
}
