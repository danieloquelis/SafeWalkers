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
    [Tooltip("Start location (Latitude = X, Longitude = Y)")]
    public Vector2d startLocation = new Vector2d(51.504497, -0.372668); // Starting point of route

    [Tooltip("End location (Latitude = X, Longitude = Y)")]
    public Vector2d endLocation = new Vector2d(51.503765, -0.381254);   // Ending point of route

    [Tooltip("Routing profile: walking / driving / cycling")]
    public string profile = "walking"; // Mapbox routing profile

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
    private LineRenderer lineRenderer; // Component to draw the route line

    // ---------- Unity Start Method ----------
    void Start()
    {
        // Check if the map is assigned
        if (map == null)
        {
            Debug.LogError("Map reference missing! Drag your AbstractMap into the Inspector.");
            return; // Stop if no map is assigned
        }

        // Add LineRenderer component dynamically
        lineRenderer = gameObject.AddComponent<LineRenderer>();

        // Initialize LineRenderer properties
        lineRenderer.positionCount = 0;                     // Start with zero points
        lineRenderer.widthMultiplier = lineWidth;          // Set the width
        lineRenderer.material = new Material(Shader.Find("Sprites/Default")); // Simple material
        lineRenderer.colorGradient = CreateGradient(startColor, endColor);    // Set gradient from start to end
        lineRenderer.useWorldSpace = true;                 // Use world positions

        // Store reference to material for animation
        lineMaterial = lineRenderer.material;

        // Start the coroutine that requests route from Mapbox
        StartCoroutine(GetRoute());
    }

    // ---------- Coroutine to Request Route from Mapbox ----------
    IEnumerator GetRoute()
    {
        // Build the Mapbox Directions API URL
        string url = $"https://api.mapbox.com/directions/v5/mapbox/{profile}/" +
                     $"{startLocation.y},{startLocation.x};{endLocation.y},{endLocation.x}" +
                     $"?alternatives=true&continue_straight=true&geometries=geojson&language=en&overview=full&steps=true" +
                     $"&access_token={mapboxAccessToken}";

        // Log the URL for debugging
        Debug.Log("Mapbox Directions Request URL: " + url);

        // Send HTTP GET request
        UnityWebRequest www = UnityWebRequest.Get(url);
        yield return www.SendWebRequest(); // Wait until response is received

        // Check for request errors
        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Mapbox Directions API Error: " + www.error);
            yield break; // Stop coroutine if request fails
        }

        // Get the JSON text from the response
        string json = www.downloadHandler.text;

        // Log first 500 characters to prevent huge console spam
        Debug.Log("Raw Mapbox response (truncated): " + json.Substring(0, Mathf.Min(json.Length, 500)) + "...");

        // ---------- Parse JSON ----------
        var jsonObj = JObject.Parse(json);                  // Parse JSON into JObject
        var route = jsonObj["routes"]?[0]?["geometry"]?["coordinates"]; // Get the first route's coordinates

        // Check if route is valid
        if (route == null)
        {
            Debug.LogError("Route geometry is null after JSON parsing.");
            yield break;
        }

        // ---------- Convert Geo Coordinates to Unity World Positions ----------
        List<Vector3> worldPositions = new List<Vector3>(); // List to store positions
        foreach (var coord in route)
        {
            double lon = (double)coord[0];                 // Longitude
            double lat = (double)coord[1];                 // Latitude

            // Convert Mapbox coordinates to Unity world position
            Vector3 worldPos = Conversions.GeoToWorldPosition(
                new Vector2d(lat, lon),                    // Lat/lon
                map.CenterMercator,                         // Map center for conversion
                map.WorldRelativeScale                     // Scale factor
            ).ToVector3xz();

            // Flatten and hover slightly above the ground for MR passthrough
            worldPos.y = hoverHeight;

            // Add to the list of points
            worldPositions.Add(worldPos);
        }

        // ---------- Draw the Route ----------
        lineRenderer.positionCount = worldPositions.Count;      // Set number of points
        lineRenderer.SetPositions(worldPositions.ToArray());    // Apply positions to LineRenderer

        // Log confirmation
        Debug.Log($"Route drawn with {worldPositions.Count} points at hover height {hoverHeight}m.");
    }

    // ---------- Flow Animation ----------
    void Update()
    {
        // Animate line texture for a "moving" effect if flowSpeed > 0
        if (lineMaterial != null && flowSpeed != 0f)
        {
            float offset = Time.time * flowSpeed;             // Calculate texture offset
            lineMaterial.mainTextureOffset = new Vector2(0, -offset); // Scroll horizontally
        }
    }

    // ---------- Line Gradient ----------
    private Gradient CreateGradient(Color start, Color end)
    {
        Gradient gradient = new Gradient();                  // New gradient object

        // Define color and alpha keys
        gradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(start, 0f),           // Start color at beginning of line
                new GradientColorKey(end, 1f)              // End color at end of line
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),              // Fully opaque at start
                new GradientAlphaKey(1f, 1f)               // Fully opaque at end
            }
        );

        return gradient;                                     // Return the configured gradient
    }
}
