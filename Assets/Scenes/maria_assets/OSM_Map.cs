using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class OSM_Map : MonoBehaviour
{
    [Header("References")]
    public ORS_Routing routingScript;  // Assign your ORS_Routing component
    public Renderer mapRenderer;       // Assign the Plane's Renderer

    [Header("Map Settings")]
    public int zoom = 18;              // Zoom level
    public int tileSize = 256;         // Tile size in pixels

    private const string tileUrlTemplate = "https://tile.openstreetmap.org/{0}/{1}/{2}.png";

    void Start()
    {
        StartCoroutine(WaitForRouteAndRenderMap());
    }

    IEnumerator WaitForRouteAndRenderMap()
    {
        // Wait until route is ready
        while (routingScript == null || routingScript.GetRoutePoints().Count == 0)
            yield return null;

        List<Vector3> routePoints = routingScript.GetRoutePoints();
        List<Vector2> latLonPoints = new List<Vector2>();
        foreach (var p in routePoints)
            latLonPoints.Add(routingScript.UnityToLatLon(p));

        // Calculate route bounding box
        float minLat = float.MaxValue, maxLat = float.MinValue;
        float minLon = float.MaxValue, maxLon = float.MinValue;

        foreach (var ll in latLonPoints)
        {
            if (ll.x < minLat) minLat = ll.x;
            if (ll.x > maxLat) maxLat = ll.x;
            if (ll.y < minLon) minLon = ll.y;
            if (ll.y > maxLon) maxLon = ll.y;
        }

        // Convert bounding box to tile numbers
        int xMinTile = LonToTileX(minLon, zoom);
        int xMaxTile = LonToTileX(maxLon, zoom);
        int yMinTile = LatToTileY(maxLat, zoom); // top
        int yMaxTile = LatToTileY(minLat, zoom); // bottom

        int widthTiles = xMaxTile - xMinTile + 1;
        int heightTiles = yMaxTile - yMinTile + 1;

        // Create map texture
        Texture2D mapTexture = new Texture2D(widthTiles * tileSize, heightTiles * tileSize);

        for (int x = 0; x < widthTiles; x++)
        {
            for (int y = 0; y < heightTiles; y++)
            {
                int tileX = xMinTile + x;
                int tileY = yMinTile + y;
                string url = string.Format(tileUrlTemplate, zoom, tileX, tileY);

                UnityWebRequest req = UnityWebRequestTexture.GetTexture(url);
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("Failed to load tile: " + url);
                    continue;
                }

                Texture2D tileTex = DownloadHandlerTexture.GetContent(req);
                mapTexture.SetPixels(x * tileSize, (heightTiles - 1 - y) * tileSize, tileSize, tileSize, tileTex.GetPixels());
            }
        }

        mapTexture.Apply();
        mapRenderer.material.mainTexture = mapTexture;

        // Scale plane to match tile bounding box in Unity units (meters)
        float lat0 = routingScript.startLatLon.x * Mathf.Deg2Rad;

        float mapWidthMeters = (TileXToLon(xMaxTile + 1, zoom) - TileXToLon(xMinTile, zoom)) * Mathf.Deg2Rad * ORS_Routing.EarthRadius * Mathf.Cos(lat0);
        float mapHeightMeters = (TileYToLat(yMinTile, zoom) - TileYToLat(yMaxTile + 1, zoom)) * Mathf.Deg2Rad * ORS_Routing.EarthRadius;

        mapRenderer.transform.localScale = new Vector3(mapWidthMeters / 10f, 1f, mapHeightMeters / 10f);

        // Center plane on route
        float centerLat = (minLat + maxLat) / 2f;
        float centerLon = (minLon + maxLon) / 2f;

        mapRenderer.transform.position = routingScript.LatLonToUnity(routingScript.startLatLon.x, routingScript.startLatLon.y);

        Debug.Log("OSM map rendered and aligned with route.");
    }

    int LonToTileX(float lon, int z)
    {
        return Mathf.FloorToInt((lon + 180f) / 360f * Mathf.Pow(2, z));
    }

    int LatToTileY(float lat, int z)
    {
        float latRad = lat * Mathf.Deg2Rad;
        return Mathf.FloorToInt((1f - Mathf.Log(Mathf.Tan(latRad) + 1f / Mathf.Cos(latRad)) / Mathf.PI) / 2f * Mathf.Pow(2, z));
    }

    float TileXToLon(int x, int z)
    {
        return x / Mathf.Pow(2, z) * 360f - 180f;
    }

    float TileYToLat(int y, int z)
    {
        float n = Mathf.PI - 2f * Mathf.PI * y / Mathf.Pow(2, z);
        return Mathf.Rad2Deg * Mathf.Atan(0.5f * (Mathf.Exp(n) - Mathf.Exp(-n)));
    }
}
