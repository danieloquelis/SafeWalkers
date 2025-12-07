using System;
using System.IO;
using UnityEngine;

namespace SafeWalkers.WakeWord
{
    /// <summary>
    /// Serializable configuration data for wake word detection system.
    /// Persisted to JSON file for cross-session storage.
    /// </summary>
    [Serializable]
    public class WakeWordSettings
    {
        [Tooltip("The transcribed wake word text")]
        public string storedWakeWord = string.Empty;

        [Tooltip("Similarity threshold for fuzzy matching (0.0 to 1.0)")]
        [Range(0f, 1f)]
        public float matchingThreshold = 0.8f;

        [Tooltip("Maximum allowed edit distance for Levenshtein matching")]
        [Range(0, 5)]
        public int maxLevenshteinDistance = 2;

        [Tooltip("Duration of audio recording in seconds")]
        [Range(1f, 10f)]
        public float recordingDuration = 3f;

        [Tooltip("Interval between detection checks in seconds")]
        [Range(0.5f, 5f)]
        public float detectionInterval = 2f;

        [Tooltip("Whether wake word matching is case-sensitive")]
        public bool caseSensitive = false;

        [Tooltip("Timestamp of last settings update (Unix milliseconds)")]
        public long lastUpdatedTimestamp = 0;

        /// <summary>
        /// Saves settings to JSON file in persistent data path.
        /// </summary>
        /// <param name="fileName">Name of the JSON file (default: wakeword_settings.json)</param>
        /// <returns>True if save was successful</returns>
        public bool SaveToFile(string fileName = "wakeword_settings.json")
        {
            try
            {
                lastUpdatedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                string json = JsonUtility.ToJson(this, true);
                string path = Path.Combine(Application.persistentDataPath, fileName);
                File.WriteAllText(path, json);

                Debug.Log($"[WakeWordSettings] Saved settings to: {path}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WakeWordSettings] Failed to save settings: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Loads settings from JSON file in persistent data path.
        /// </summary>
        /// <param name="fileName">Name of the JSON file (default: wakeword_settings.json)</param>
        /// <returns>Loaded settings, or null if file doesn't exist or load failed</returns>
        public static WakeWordSettings LoadFromFile(string fileName = "wakeword_settings.json")
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, fileName);

                if (!File.Exists(path))
                {
                    Debug.Log($"[WakeWordSettings] Settings file not found at: {path}");
                    return null;
                }

                string json = File.ReadAllText(path);
                WakeWordSettings settings = JsonUtility.FromJson<WakeWordSettings>(json);

                if (settings != null)
                {
                    Debug.Log($"[WakeWordSettings] Loaded settings from: {path}");
                }

                return settings;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WakeWordSettings] Failed to load settings: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Checks if a settings file exists.
        /// </summary>
        /// <param name="fileName">Name of the JSON file (default: wakeword_settings.json)</param>
        /// <returns>True if file exists</returns>
        public static bool FileExists(string fileName = "wakeword_settings.json")
        {
            string path = Path.Combine(Application.persistentDataPath, fileName);
            return File.Exists(path);
        }

        /// <summary>
        /// Deletes the settings file.
        /// </summary>
        /// <param name="fileName">Name of the JSON file (default: wakeword_settings.json)</param>
        /// <returns>True if deletion was successful</returns>
        public static bool DeleteFile(string fileName = "wakeword_settings.json")
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, fileName);

                if (File.Exists(path))
                {
                    File.Delete(path);
                    Debug.Log($"[WakeWordSettings] Deleted settings file: {path}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WakeWordSettings] Failed to delete settings: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Creates default settings with recommended values.
        /// </summary>
        public static WakeWordSettings CreateDefault()
        {
            return new WakeWordSettings
            {
                storedWakeWord = string.Empty,
                matchingThreshold = 0.8f,
                maxLevenshteinDistance = 2,
                recordingDuration = 3f,
                detectionInterval = 2f,
                caseSensitive = false,
                lastUpdatedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        /// <summary>
        /// Validates settings and clamps values to acceptable ranges.
        /// </summary>
        public void Validate()
        {
            matchingThreshold = Mathf.Clamp01(matchingThreshold);
            maxLevenshteinDistance = Mathf.Clamp(maxLevenshteinDistance, 0, 5);
            recordingDuration = Mathf.Clamp(recordingDuration, 1f, 10f);
            detectionInterval = Mathf.Clamp(detectionInterval, 0.5f, 5f);
        }

        public override string ToString()
        {
            return $"WakeWordSettings:\n" +
                   $"  Wake Word: '{storedWakeWord}'\n" +
                   $"  Matching Threshold: {matchingThreshold:F2}\n" +
                   $"  Max Edit Distance: {maxLevenshteinDistance}\n" +
                   $"  Recording Duration: {recordingDuration}s\n" +
                   $"  Detection Interval: {detectionInterval}s\n" +
                   $"  Case Sensitive: {caseSensitive}\n" +
                   $"  Last Updated: {DateTimeOffset.FromUnixTimeMilliseconds(lastUpdatedTimestamp):yyyy-MM-dd HH:mm:ss}";
        }
    }
}
