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
    public int zoom = 18;              
    public int tileSize = 256;         
    public float paddingDegrees = 0.0005f; 

    void Start()
    {
        StartCoroutine(WaitForRouteAndRenderMap());
    }

    IEnumerator WaitForRouteAndRenderMap()
    {
        while (routingScript == null || !routingScript.RouteIsReady())
            yield return null;

        List<Vector2> latLonPoints = routingScript.GetRouteLatLonPoints();

        float minLat = float.MaxValue, maxLat = float.MinValue;
        float minLon = float.MaxValue, maxLon = float.MinValue;

        foreach (var ll in latLonPoints)
        {
            if (ll.x < minLat) minLat = ll.x;
            if (ll.x > maxLat) maxLat = ll.x;
            if (ll.y < minLon) minLon = ll.y;
            if (ll.y > maxLon) maxLon = ll.y;
        }

        minLat -= paddingDegrees; maxLat += paddingDegrees;
        minLon -= paddingDegrees; maxLon += paddingDegrees;

        int xMinTile = LonToTileX(minLon, zoom);
        int xMaxTile = LonToTileX(maxLon, zoom);
        int yMinTile = LatToTileY(maxLat, zoom);
        int yMaxTile = LatToTileY(minLat, zoom);

        int widthTiles = xMaxTile - xMinTile + 1;
        int heightTiles = yMaxTile - yMinTile + 1;

        Texture2D mapTexture = new Texture2D(widthTiles * tileSize, heightTiles * tileSize);

        for (int x = 0; x < widthTiles; x++)
        {
            for (int y = 0; y < heightTiles; y++)
            {
                int tileX = xMinTile + x;
                int tileY = yMinTile + y;
                string url = $"https://tile.openstreetmap.org/{zoom}/{tileX}/{tileY}.png";

                UnityWebRequest req = UnityWebRequestTexture.GetTexture(url);
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("Failed to load tile: " + url);
                    continue;
                }

                Texture2D tileTex = DownloadHandlerTexture.GetContent(req);
                mapTexture.SetPixels(
                    x * tileSize,
                    (heightTiles - 1 - y) * tileSize,
                    tileSize,
                    tileSize,
                    tileTex.GetPixels()
                );
            }
        }

        mapTexture.Apply();
        mapRenderer.material.mainTexture = mapTexture;

        float centerLat = (minLat + maxLat) / 2f;
        float centerLon = (minLon + maxLon) / 2f;
        float lat0Rad = centerLat * Mathf.Deg2Rad;

        float mapWidthMeters = (TileXToLon(xMaxTile + 1, zoom) - TileXToLon(xMinTile, zoom)) * Mathf.Deg2Rad * ORS_Routing.EarthRadius * Mathf.Cos(lat0Rad);
        float mapHeightMeters = (TileYToLat(yMinTile, zoom) - TileYToLat(yMaxTile + 1, zoom)) * Mathf.Deg2Rad * ORS_Routing.EarthRadius;

        mapRenderer.transform.localScale = new Vector3(mapWidthMeters / 10f, 1f, mapHeightMeters / 10f);

        mapRenderer.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

        Vector3 mapCenterWorld = routingScript.LatLonToUnity(centerLat, centerLon);
        Vector3 bestFitOffset = ComputeBestFitOffset(latLonPoints, mapCenterWorld);
        mapRenderer.transform.position = mapCenterWorld + bestFitOffset;

        Debug.Log($"OSM map rendered, rotated, scaled, and aligned. Plane position: {mapRenderer.transform.position}");
    }

    Vector3 ComputeBestFitOffset(List<Vector2> latLonPoints, Vector3 mapCenterWorld)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (var ll in latLonPoints)
        {
            Vector3 pos = routingScript.LatLonToUnity(ll.x, ll.y);
            if (pos.x < minX) minX = pos.x;
            if (pos.x > maxX) maxX = pos.x;
            if (pos.z < minZ) minZ = pos.z;
            if (pos.z > maxZ) maxZ = pos.z;
        }

        Vector3 routeCenter = new Vector3((minX + maxX) / 2f, 0f, (minZ + maxZ) / 2f);
        return routeCenter - mapCenterWorld;
    }

    int LonToTileX(float lon, int z) => Mathf.FloorToInt((lon + 180f) / 360f * Mathf.Pow(2, z));
    int LatToTileY(float lat, int z)
    {
        float latRad = lat * Mathf.Deg2Rad;
        return Mathf.FloorToInt((1f - Mathf.Log(Mathf.Tan(latRad) + 1f / Mathf.Cos(latRad)) / Mathf.PI) / 2f * Mathf.Pow(2, z));
    }
    float TileXToLon(int x, int z) => x / Mathf.Pow(2, z) * 360f - 180f;
    float TileYToLat(int y, int z)
    {
        float n = Mathf.PI - 2f * Mathf.PI * y / Mathf.Pow(2, z);
        return Mathf.Rad2Deg * Mathf.Atan(0.5f * (Mathf.Exp(n) - Mathf.Exp(-n)));
    }
}
