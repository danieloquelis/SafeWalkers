using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;          // For sending HTTP requests
using Mapbox.Unity.Map;               // Mapbox map classes
using Mapbox.Unity.Utilities;         // Mapbox utility functions
using Mapbox.Utils;                   // Vector2d for latitude/longitude
using Newtonsoft.Json.Linq;           // For parsing JSON responses from Mapbox

public class DirectionsRequest : MonoBehaviour
{
    // ---------- Inspector Fields ----------
    [Header("Mapbox Settings")]
    [Tooltip("Drag your Mapbox AbstractMap object here")]
    public AbstractMap map; // Reference to Mapbox map in the scene

    [TextArea]
    [Tooltip("Your Mapbox Access Token")]
    public string mapboxAccessToken = "YOUR_MAPBOX_ACCESS_TOKEN_HERE"; // API token for Mapbox

    [Header("Route Settings")]
    [Tooltip("End location (Latitude = X, Longitude = Y)")]
    public Vector2d endLocation = new Vector2d(51.503765, -0.381254);   // Ending point of route

    [Tooltip("Routing profile: walking / driving / cycling")]
    public string profile = "walking"; // Mapbox routing profile

    [Header("Camera Tracking")]
    [Tooltip("Camera or user transform to follow")]
    public Transform userCamera;         // Camera or user for dynamic start

    [Tooltip("Minimum movement (degrees) to request new route")]
    public double moveThreshold = 0.00001; // ~1 meter

    [Header("Visual Settings")]
    [Tooltip("Height above ground for the route line (meters)")]
    public float hoverHeight = 0.25f; // Hover height for MR passthrough

    [Tooltip("Width of the route line")]
    public float lineWidth = 0.05f; // LineRenderer width

    [Tooltip("Start color of the route line")]
    public Color startColor = Color.cyan; // Gradient start color

    [Tooltip("End color of the route line")]
    public Color endColor = Color.yellow; // Gradient end color

    [Tooltip("Material for the route line (e.g., your scrolling PNG)")]
    public Material lineMaterial;

    [Tooltip("Speed of the line animation (optional)")]
    public float flowSpeed = 1f; // Line animation speed

    // ---------- Private Fields ----------
    private LineRenderer lineRenderer;    // Component to draw the route line
    private Vector2d lastUserGeoPos;      // Stores last known camera geo position

    // ---------- Unity Start Method ----------
    void Start()
    {
        // Check if the map and camera are assigned
        if (map == null)
        {
            Debug.LogError("Map reference missing! Drag your AbstractMap into the Inspector.");
            return;
        }
        if (userCamera == null)
        {
            Debug.LogError("Camera reference missing! Drag your main XR camera into the Inspector.");
            return;
        }

        // Add and configure LineRenderer
        lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.positionCount = 0;
        lineRenderer.widthMultiplier = lineWidth;
        lineRenderer.material = lineMaterial != null ? lineMaterial : new Material(Shader.Find("Sprites/Default"));
        lineRenderer.colorGradient = CreateGradient(startColor, endColor);
        lineRenderer.useWorldSpace = true;

        // Initialize lastUserGeoPos so first route is requested
        lastUserGeoPos = map.WorldToGeoPosition(userCamera.position);

        // Request initial route
        StartCoroutine(GetRoute());
    }

    // ---------- Update Method ----------
    void Update()
    {
        if (userCamera == null) return;

        // Convert camera position to geo coordinates
        Vector2d currentGeoPos = map.WorldToGeoPosition(userCamera.position);

        // Calculate distance from last position
        Vector2d delta = currentGeoPos - lastUserGeoPos;
        double distance = Mathf.Sqrt((float)(delta.x * delta.x + delta.y * delta.y));

        // Only request a new route if the camera moved beyond threshold
        if (distance > moveThreshold)
        {
            lastUserGeoPos = currentGeoPos;
            StopAllCoroutines();
            StartCoroutine(GetRoute());
        }

        // Animate line texture for flow effect
        if (lineMaterial != null && flowSpeed != 0f)
        {
            float offset = Time.time * flowSpeed;
            lineMaterial.mainTextureOffset = new Vector2(-offset, 0);
        }
    }

    // ---------- Coroutine to Request Route from Mapbox ----------
    IEnumerator GetRoute()
    {
        Vector2d startLocation = lastUserGeoPos;

        // Build Mapbox Directions API URL
        string url = $"https://api.mapbox.com/directions/v5/mapbox/{profile}/" +
                     $"{startLocation.y},{startLocation.x};{endLocation.y},{endLocation.x}" +
                     $"?alternatives=true&continue_straight=true&geometries=geojson&language=en&overview=full&steps=true" +
                     $"&access_token={mapboxAccessToken}";

        // Log for debugging
        Debug.Log("Mapbox Directions Request URL: " + url);

        // Send HTTP GET request
        UnityWebRequest www = UnityWebRequest.Get(url);
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Mapbox Directions API Error: " + www.error);
            yield break;
        }

        // Parse JSON response
        string json = www.downloadHandler.text;
        var jsonObj = JObject.Parse(json);
        var route = jsonObj["routes"]?[0]?["geometry"]?["coordinates"];

        if (route == null)
        {
            Debug.LogError("Route geometry is null after JSON parsing.");
            yield break;
        }

        // Convert route coordinates to Unity world positions
        List<Vector3> worldPositions = new List<Vector3>();
        foreach (var coord in route)
        {
            double lon = (double)coord[0];
            double lat = (double)coord[1];

            Vector3 worldPos = Conversions.GeoToWorldPosition(
                new Vector2d(lat, lon),
                map.CenterMercator,
                map.WorldRelativeScale
            ).ToVector3xz();

            worldPos.y = hoverHeight; // Apply hover for MR

            worldPositions.Add(worldPos);
        }

        // Apply to LineRenderer
        lineRenderer.positionCount = worldPositions.Count;
        lineRenderer.SetPositions(worldPositions.ToArray());

        Debug.Log($"Route updated with {worldPositions.Count} points at hover height {hoverHeight}m.");
    }

    // ---------- Gradient Helper ----------
    private Gradient CreateGradient(Color start, Color end)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(start, 0f),
                new GradientColorKey(end, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            }
        );
        return gradient;
    }
}
