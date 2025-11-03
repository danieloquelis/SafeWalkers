using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Mapbox.Unity.Map;
using Mapbox.Unity.Utilities;
using Mapbox.Utils;
using Newtonsoft.Json.Linq;

public class DirectionsRequest : MonoBehaviour
{
    [Header("Mapbox Settings")]
    public AbstractMap map; // Drag your AbstractMap here
    [TextArea]
    public string mapboxAccessToken = "YOUR_MAPBOX_ACCESS_TOKEN_HERE";

    [Header("Route Settings")]
    [Tooltip("Start location (Latitude = X, Longitude = Y)")]
    public Vector2d startLocation = new Vector2d(51.504497, -0.372668); 
    [Tooltip("End location (Latitude = X, Longitude = Y)")]
    public Vector2d endLocation = new Vector2d(51.503765, -0.381254); 
    [Tooltip("Routing profile: walking / driving / cycling")]
    public string profile = "walking"; 

    private LineRenderer lineRenderer;

    void Start()
    {
        // Ensure map is assigned
        if (map == null)
        {
            Debug.LogError("Map reference missing! Drag your AbstractMap into the Inspector.");
            return;
        }

        // Set up the LineRenderer
        lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.positionCount = 0;
        lineRenderer.widthMultiplier = 5f;
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startColor = Color.cyan;
        lineRenderer.endColor = Color.blue;

        StartCoroutine(GetRoute());
    }

    IEnumerator GetRoute()
    {
        string url = $"https://api.mapbox.com/directions/v5/mapbox/{profile}/" +
                     $"{startLocation.y},{startLocation.x};{endLocation.y},{endLocation.x}" +
                     $"?alternatives=true&continue_straight=true&geometries=geojson&language=en&overview=full&steps=true" +
                     $"&access_token={mapboxAccessToken}";

        Debug.Log("Mapbox Directions Request URL: " + url);

        UnityWebRequest www = UnityWebRequest.Get(url);
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Mapbox Directions API Error: " + www.error);
            yield break;
        }

        string json = www.downloadHandler.text;
        Debug.Log("Raw Mapbox response (truncated): " + json.Substring(0, Mathf.Min(json.Length, 500)) + "...");

        // Parse using Newtonsoft.Json
        var jsonObj = JObject.Parse(json);
        var route = jsonObj["routes"]?[0]?["geometry"]?["coordinates"];
        if (route == null)
        {
            Debug.LogError("Route geometry is null after JSON parsing.");
            yield break;
        }

        List<Vector3> worldPositions = new List<Vector3>();

        foreach (var coord in route)
        {
            double lon = (double)coord[0];
            double lat = (double)coord[1];

            Vector3 worldPos = Conversions.GeoToWorldPosition(
                new Vector2d(lat, lon),
                map.CenterMercator,
                map.WorldRelativeScale).ToVector3xz();

            worldPositions.Add(worldPos);
        }

        lineRenderer.positionCount = worldPositions.Count;
        lineRenderer.SetPositions(worldPositions.ToArray());

        Debug.Log($"Route drawn with {worldPositions.Count} points.");
    }
}
