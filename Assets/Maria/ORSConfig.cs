using UnityEngine;

/// <summary>
/// Centralized loader for the OpenRouteService API key.
/// Expects the key to be stored as plain text in:
/// Assets/Resources/ors_api_key.txt
/// </summary>
public static class ORSConfig
{
    private const string ResourcePath = "ors_api_key";
    private static string _cachedApiKey;

    /// <summary>
    /// Returns the ORS API key loaded from Resources.
    /// The value is cached after first successful load.
    /// </summary>
    public static string GetApiKey()
    {
        if (!string.IsNullOrEmpty(_cachedApiKey))
        {
            return _cachedApiKey;
        }

        TextAsset keyAsset = Resources.Load<TextAsset>(ResourcePath);
        if (keyAsset == null)
        {
            Debug.LogError("ORSConfig: Could not load ORS API key. Ensure 'Assets/Resources/ors_api_key.txt' exists and is in a Resources folder.");
            return null;
        }

        _cachedApiKey = keyAsset.text.Trim();

        if (string.IsNullOrEmpty(_cachedApiKey))
        {
            Debug.LogError("ORSConfig: Loaded ORS API key is empty. Check the contents of 'Assets/Resources/ors_api_key.txt'.");
        }

        return _cachedApiKey;
    }
}


