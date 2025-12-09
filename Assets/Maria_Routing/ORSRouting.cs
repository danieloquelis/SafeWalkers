using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
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
    [SerializeField] private bool autoRequestOnStart = true;
    [SerializeField] private bool snapPlayerOnAutoRoute = true;

    [Header("Visualization")] 
    public LineRenderer lineRenderer;

    [Header("XR Rig")]
    public Transform xrRig;

    [Header("Line Styling")]
    [SerializeField] private Material flowLineMaterial;
    [SerializeField, Range(0.01f, 0.5f)] private float lineWidth = 0.12f;
    [SerializeField, Range(0.25f, 4f)] private float arrowWorldLength = 1.25f;
    [SerializeField, Range(0.05f, 5f)] private float textureAnimationSpeed = 0.8f;
    [SerializeField, Range(0f, 1f)] private float lineAlpha = 0.65f;
    [SerializeField] private Color lineTint = Color.white;
    [Tooltip("Vertical offset (meters) applied to every route point. Use negative values to place the path near the floor.")]
    [SerializeField, Range(-2f, 0.5f)] private float routeHeightOffset = 0.02f;
    [SerializeField] private bool useRaycastForFloorDetection = true;
    [SerializeField] private LayerMask floorLayerMask = ~0;

    [Header("Visibility")]
    [SerializeField] private bool revealRouteFromPlayer = true;
    [SerializeField, Tooltip("Meters of line shown ahead of the player. Set to 0 to show the full route.")]
    [Min(0f)] private float maxVisibleDistance = 35f;

    private List<Vector3> cachedRoute = new List<Vector3>();
    private bool routeReady = false;
    private bool routeRequestInProgress = false;
    private readonly List<Vector3> visibleRouteBuffer = new List<Vector3>();
    private Material runtimeLineMaterial;
    private float textureOffset;

    public const float EarthRadius = 6371000f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

    void Start()
    {
        InitializeLineRenderer();

        if (xrRig == null)
            Debug.LogWarning("XR Rig not assigned. Route will not snap automatically.");

        if (!EnsureOrsApiKey())
            return;

        if (autoRequestOnStart)
        {
            BeginRoute(startLatLon, endLatLon, snapPlayerOnAutoRoute);
        }
    }

    void OnDestroy()
    {
        if (runtimeLineMaterial == null) return;

        if (Application.isPlaying)
            Destroy(runtimeLineMaterial);
        else
            DestroyImmediate(runtimeLineMaterial);
    }

    void Update()
    {
        AnimateLineTexture();

        if (!routeReady || routeRequestInProgress) return;

        Vector3 playerPos = GetPlayerPosition();
        float dist = DistanceToRoute(playerPos, cachedRoute);

        if (dist > offRouteThreshold)
        {
            Debug.LogWarning("You are off the planned route!");
        }

        if (revealRouteFromPlayer)
            UpdateVisibleRoute(playerPos);
    }

    public bool BeginRoute(Vector2 start, Vector2 end, bool snapPlayer = false)
    {
        if (!EnsureOrsApiKey())
            return false;

        if (routeRequestInProgress)
        {
            Debug.LogWarning("ORS_Routing: Route request already in progress. Ignoring new request.");
            return false;
        }

        startLatLon = start;
        endLatLon = end;
        routeReady = false;
        cachedRoute.Clear();
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 0;
        }

        StartCoroutine(RequestRoute(startLatLon, endLatLon, snapPlayer));
        return true;
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
            {
                // Only snap XZ position, preserve Y to avoid disrupting avatar positioning
                Vector3 snapTarget = cachedRoute[0];
                snapTarget.y = xrRig.position.y;
                xrRig.position = snapTarget;
            }
            else if (Camera.main != null)
            {
                Vector3 snapTarget = cachedRoute[0];
                snapTarget.y = Camera.main.transform.position.y;
                Camera.main.transform.position = snapTarget;
            }
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

        float yHeight = routeHeightOffset;

        // Optionally raycast down to find actual floor
        if (useRaycastForFloorDetection)
        {
            Vector3 testPos = new Vector3(x, 10f, z); // Start raycast from above
            if (Physics.Raycast(testPos, Vector3.down, out RaycastHit hit, 20f, floorLayerMask))
            {
                yHeight = hit.point.y + routeHeightOffset;
            }
        }

        return new Vector3(x, yHeight, z);
    }

    void DrawRoute()
    {
        if (lineRenderer == null) return;

        if (cachedRoute.Count < 2)
        {
            lineRenderer.positionCount = 0;
            return;
        }

        if (revealRouteFromPlayer)
            UpdateVisibleRoute(GetPlayerPosition());
        else
            ApplyRouteToRenderer(cachedRoute);
    }

    void InitializeLineRenderer()
    {
        if (lineRenderer == null) return;

        lineRenderer.loop = false;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.textureMode = LineTextureMode.Tile;
        lineRenderer.generateLightingData = false;
        lineRenderer.widthMultiplier = lineWidth;

        if (flowLineMaterial != null)
            runtimeLineMaterial = new Material(flowLineMaterial);
        else if (lineRenderer.sharedMaterial != null)
            runtimeLineMaterial = new Material(lineRenderer.sharedMaterial);

        if (runtimeLineMaterial != null)
        {
            runtimeLineMaterial.name += " (Runtime)";
            ApplyLineTint();
            lineRenderer.material = runtimeLineMaterial;
        }

        Color tinted = new Color(lineTint.r, lineTint.g, lineTint.b, lineAlpha);
        lineRenderer.startColor = tinted;
        lineRenderer.endColor = tinted;
    }

    void ApplyLineTint()
    {
        if (runtimeLineMaterial == null) return;

        Color tinted = new Color(lineTint.r, lineTint.g, lineTint.b, lineAlpha);

        if (runtimeLineMaterial.HasProperty(BaseColorId))
            runtimeLineMaterial.SetColor(BaseColorId, tinted);
        if (runtimeLineMaterial.HasProperty(ColorId))
            runtimeLineMaterial.SetColor(ColorId, tinted);
        if (runtimeLineMaterial.HasProperty(EmissionColorId))
            runtimeLineMaterial.SetColor(EmissionColorId, tinted * 0.25f);

        if (runtimeLineMaterial.HasProperty("_Surface"))
            runtimeLineMaterial.SetFloat("_Surface", 1f);
        if (runtimeLineMaterial.HasProperty("_ZWrite"))
            runtimeLineMaterial.SetFloat("_ZWrite", 0f);
        if (runtimeLineMaterial.HasProperty("_DstBlend"))
            runtimeLineMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (runtimeLineMaterial.HasProperty("_DstBlendAlpha"))
            runtimeLineMaterial.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);

        runtimeLineMaterial.renderQueue = (int)RenderQueue.Transparent;
    }

    void AnimateLineTexture()
    {
        if (runtimeLineMaterial == null) return;
        if (Mathf.Approximately(textureAnimationSpeed, 0f)) return;

        textureOffset = Mathf.Repeat(textureOffset + textureAnimationSpeed * Time.deltaTime, 1f);
        Vector2 offset = new Vector2(textureOffset, 0f);

        if (runtimeLineMaterial.HasProperty(BaseMapId))
            runtimeLineMaterial.SetTextureOffset(BaseMapId, offset);
        if (runtimeLineMaterial.HasProperty(MainTexId))
            runtimeLineMaterial.SetTextureOffset(MainTexId, offset);
    }

    Vector3 GetPlayerPosition()
    {
        if (xrRig != null)
            return xrRig.position;

        if (Camera.main != null)
            return Camera.main.transform.position;

        if (cachedRoute.Count > 0)
            return cachedRoute[0];

        return Vector3.zero;
    }

    bool EnsureOrsApiKey()
    {
        if (!string.IsNullOrEmpty(orsApiKey))
            return true;

        string loadedKey = ORSConfig.GetApiKey();
        if (string.IsNullOrEmpty(loadedKey))
        {
            Debug.LogError("ORS_Routing: ORS API key could not be loaded.");
            return false;
        }

        orsApiKey = loadedKey;
        return true;
    }

    void UpdateVisibleRoute(Vector3 playerPos)
    {
        if (lineRenderer == null || cachedRoute.Count < 2) return;

        if (!TryProjectOnRoute(playerPos, out Vector3 projection, out int segmentIndex, out _))
        {
            ApplyRouteToRenderer(cachedRoute);
            return;
        }

        visibleRouteBuffer.Clear();
        visibleRouteBuffer.Add(SetHeight(projection));

        bool limitLength = maxVisibleDistance > Mathf.Epsilon;
        float remainingDistance = maxVisibleDistance;
        Vector3 previousSourcePoint = projection;

        for (int i = segmentIndex + 1; i < cachedRoute.Count; i++)
        {
            Vector3 nextSourcePoint = cachedRoute[i];
            float segmentLength = Vector3.Distance(previousSourcePoint, nextSourcePoint);

            if (limitLength && segmentLength >= remainingDistance && remainingDistance > Mathf.Epsilon)
            {
                Vector3 clampedPoint = Vector3.Lerp(previousSourcePoint, nextSourcePoint,
                    Mathf.Clamp01(remainingDistance / Mathf.Max(segmentLength, 0.0001f)));
                visibleRouteBuffer.Add(SetHeight(clampedPoint));
                break;
            }

            visibleRouteBuffer.Add(SetHeight(nextSourcePoint));
            previousSourcePoint = nextSourcePoint;

            if (limitLength)
            {
                remainingDistance -= segmentLength;
                if (remainingDistance <= Mathf.Epsilon)
                    break;
            }
        }

        ApplyRouteToRenderer(visibleRouteBuffer);
    }

    void ApplyRouteToRenderer(IList<Vector3> points)
    {
        if (lineRenderer == null || points == null) return;

        if (points.Count < 2)
        {
            lineRenderer.positionCount = 0;
            return;
        }

        lineRenderer.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++)
            lineRenderer.SetPosition(i, points[i]);

        UpdateTextureTiling(CalculateLength(points));
    }

    float CalculateLength(IList<Vector3> points)
    {
        if (points == null || points.Count < 2) return 0f;

        float length = 0f;
        for (int i = 0; i < points.Count - 1; i++)
            length += Vector3.Distance(points[i], points[i + 1]);

        return length;
    }

    void UpdateTextureTiling(float length)
    {
        if (lineRenderer == null || length <= 0f) return;

        float repeatCount = Mathf.Max(1f, length / Mathf.Max(arrowWorldLength, 0.01f));
        lineRenderer.textureScale = new Vector2(repeatCount, 1f);
    }

    bool TryProjectOnRoute(Vector3 point, out Vector3 projection, out int segmentIndex, out float segmentT)
    {
        projection = Vector3.zero;
        segmentIndex = -1;
        segmentT = 0f;

        if (cachedRoute.Count < 2) return false;

        float minSqr = float.MaxValue;
        for (int i = 0; i < cachedRoute.Count - 1; i++)
        {
            Vector3 candidate = ProjectPointOnSegment(point, cachedRoute[i], cachedRoute[i + 1], out float t);
            float sqr = (candidate - point).sqrMagnitude;
            if (sqr < minSqr)
            {
                minSqr = sqr;
                projection = candidate;
                segmentIndex = i;
                segmentT = t;
            }
        }

        return segmentIndex >= 0;
    }

    Vector3 ProjectPointOnSegment(Vector3 point, Vector3 a, Vector3 b, out float t)
    {
        Vector3 ab = b - a;
        float denom = ab.sqrMagnitude;
        if (denom < Mathf.Epsilon)
        {
            t = 0f;
            return a;
        }

        t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / denom);
        return a + ab * t;
    }

    Vector3 SetHeight(Vector3 source)
    {
        source.y = routeHeightOffset;
        return source;
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
