using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Networking;

public class ORS_Map : MonoBehaviour
{
    [Header("ORS Settings")]
    public string orsApiKey = "YOUR_API_KEY_HERE";

    [Header("References")]
    public ORS_Routing routingScript;  // assign your ORS_Routing component
    public Renderer mapRenderer;        // assign the Plane’s Renderer

    [Header("Map Settings")]
    public int width = 1024;
    public int height = 1024;

    private const double EarthRadius = 6371000.0;

    void Start()
    {
        StartCoroutine(WaitForRouteAndRequestMap());
    }

    IEnumerator WaitForRouteAndRequestMap()
    {
        // Wait until the route is ready
        while (routingScript == null || !routingScript.RouteIsReady())
            yield return null;

        List<Vector3> worldPoints = routingScript.GetRoutePoints();
        List<Vector2> latLonPoints = new List<Vector2>();
        foreach (var p in worldPoints)
            latLonPoints.Add(routingScript.UnityToLatLon(p));

        if (latLonPoints.Count == 0)
        {
            Debug.LogError("No route points available for ORS Map.");
            yield break;
        }

        // Compute bounding box
        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLon = double.MaxValue, maxLon = double.MinValue;
        foreach (var ll in latLonPoints)
        {
            if (ll.x < minLat) minLat = ll.x;
            if (ll.x > maxLat) maxLat = ll.x;
            if (ll.y < minLon) minLon = ll.y;
            if (ll.y > maxLon) maxLon = ll.y;
        }

        // Build ORS path string
        List<string> pathSegments = new List<string>();
        foreach (var ll in latLonPoints)
            pathSegments.Add(
                string.Format(CultureInfo.InvariantCulture, "{0},{1}", ll.y, ll.x) // lon,lat
            );

        string pathParam = "weight:3|color:0x00ff00|" + string.Join("|", pathSegments);

        // Build POST form
        WWWForm form = new WWWForm();
        form.AddField("api_key", orsApiKey);
        form.AddField("width", width);
        form.AddField("height", height);
        form.AddField("format", "png");
        form.AddField("path", pathParam);

        string url = "https://api.openrouteservice.org/maps/static";

        UnityWebRequest req = UnityWebRequest.Post(url, form);
        req.SetRequestHeader("Accept", "image/png");

        Debug.Log("Requesting ORS Static Map...");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("ORS Static Map Error: " + req.error +
                           " ResponseCode: " + req.responseCode);
            yield break;
        }

        Texture2D tex = DownloadHandlerTexture.GetContent(req);
        mapRenderer.material.mainTexture = tex;

        // Scale map to match real-world meters
        ScaleMap(minLat, maxLat, minLon, maxLon);

        Debug.Log("ORS Static Map loaded and applied.");
    }

    void ScaleMap(double minLat, double maxLat, double minLon, double maxLon)
    {
        double latCenterRad = ((minLat + maxLat) / 2.0) * Mathf.Deg2Rad;

        double heightMeters = (maxLat - minLat) * Mathf.Deg2Rad * EarthRadius;
        double widthMeters = (maxLon - minLon) * Mathf.Deg2Rad * EarthRadius * System.Math.Cos(latCenterRad);

        // Apply to plane
        mapRenderer.transform.localScale = new Vector3((float)widthMeters, 1f, (float)heightMeters);

        // Center plane at route midpoint
        double centerLat = (minLat + maxLat) / 2.0;
        double centerLon = (minLon + maxLon) / 2.0;

        mapRenderer.transform.position = routingScript.LatLonToUnity((float)centerLat, (float)centerLon);
    }
}
