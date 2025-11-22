using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class OSM_Map : MonoBehaviour
{
    [Header("Map Settings")]
    public Renderer mapRenderer;   // Assign a plane or quad in scene
    public int zoom = 17;

    [Header("Route Start")]
    public float startLat = 51.51165f;
    public float startLon = -0.3768478f;

    // Earth radius in meters
    private const float EarthRadius = 6378137f;

    void Start()
    {
        if (mapRenderer == null)
        {
            Debug.LogError("Map Renderer not assigned.");
            return;
        }

        StartCoroutine(LoadTile(startLat, startLon, zoom));
    }

    IEnumerator LoadTile(float lat, float lon, int zoom)
    {
        Vector2 tile = LatLonToTile(lat, lon, zoom);
        int tileX = Mathf.FloorToInt(tile.x);
        int tileY = Mathf.FloorToInt(tile.y);

        string url = $"https://tile.openstreetmap.org/{zoom}/{tileX}/{tileY}.png";

        UnityWebRequest req = UnityWebRequestTexture.GetTexture(url);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Failed to load map tile: " + req.error);
            yield break;
        }

        Texture2D tex = DownloadHandlerTexture.GetContent(req);
        mapRenderer.material.mainTexture = tex;

        // Compute tile bounds in lat/lon using System.Math
        double n = System.Math.Pow(2.0, zoom);
        double lon_min = tileX / n * 360.0 - 180.0;
        double lat_max = System.Math.Atan(System.Math.Sinh(System.Math.PI * (1 - 2 * tileY / n))) * Mathf.Rad2Deg;
        double lat_min = System.Math.Atan(System.Math.Sinh(System.Math.PI * (1 - 2 * (tileY + 1) / n))) * Mathf.Rad2Deg;
        double lon_max = (tileX + 1) / n * 360.0 - 180.0;

        // Convert corners to Unity coordinates
        Vector3 bottomLeft = LatLonToUnity((float)lat_min, (float)lon_min);
        Vector3 topRight = LatLonToUnity((float)lat_max, (float)lon_max);

        // Center and scale plane
        mapRenderer.transform.position = (bottomLeft + topRight) / 2f;
        mapRenderer.transform.localScale = new Vector3(
            Mathf.Abs(topRight.x - bottomLeft.x),
            1f,
            Mathf.Abs(topRight.z - bottomLeft.z)
        );
    }

    // Convert lat/lon to Unity coordinates relative to start
    Vector3 LatLonToUnity(float lat, float lon)
    {
        float lat0 = startLat * Mathf.Deg2Rad;
        float lon0 = startLon * Mathf.Deg2Rad;
        float latRad = lat * Mathf.Deg2Rad;
        float lonRad = lon * Mathf.Deg2Rad;

        float x = (lonRad - lon0) * Mathf.Cos(lat0) * EarthRadius;
        float z = (latRad - lat0) * EarthRadius;

        return new Vector3(x, 0.05f, z); // slightly raised so route line is visible
    }

    // OSM tile calculation
    public static Vector2 LatLonToTile(float lat, float lon, int zoom)
    {
        double x = (lon + 180.0) / 360.0 * System.Math.Pow(2.0, zoom);
        double y = (1.0 - System.Math.Log(System.Math.Tan(lat * Mathf.Deg2Rad) + 1.0 / System.Math.Cos(lat * Mathf.Deg2Rad)) / System.Math.PI) / 2.0 * System.Math.Pow(2.0, zoom);
        return new Vector2((float)x, (float)y);
    }
}
