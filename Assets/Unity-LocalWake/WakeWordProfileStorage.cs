using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LocalWake.Unity
{
    /// <summary>
    /// Simple JSON-based persistence for wake-word reference embeddings.
    /// Stores per-user wake-word profiles under Application.persistentDataPath.
    /// </summary>
    public static class WakeWordProfileStorage
    {
        [Serializable]
        public class SerializedEmbedding
        {
            public int embeddingDim;
            public int timeSteps;
            public float[] data; // length = embeddingDim * timeSteps
        }

        [Serializable]
        public class WakeWordProfile
        {
            public string wakeWordName;
            public List<SerializedEmbedding> references = new();
        }

        const string FilePrefix = "wakeword_";
        const string FileExtension = ".json";

        static string GetPath(string profileId)
        {
            var safeId = string.IsNullOrEmpty(profileId) ? "default" : profileId;
            return Path.Combine(Application.persistentDataPath, FilePrefix + safeId + FileExtension);
        }

        public static void SaveProfile(string profileId, WakeWordProfile profile)
        {
            if (profile == null)
            {
                Debug.LogWarning("[WakeWord] WakeWordProfileStorage.SaveProfile: profile is null, skipping.");
                return;
            }

            try
            {
                string json = JsonUtility.ToJson(profile);
                string path = GetPath(profileId);
                File.WriteAllText(path, json);
                Debug.Log($"[WakeWord] WakeWordProfileStorage: Saved profile '{profileId}' to '{path}'.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WakeWord] WakeWordProfileStorage.SaveProfile: Failed to save profile '{profileId}'. {e}");
            }
        }

        public static bool TryLoadProfile(string profileId, out WakeWordProfile profile)
        {
            profile = null;
            try
            {
                string path = GetPath(profileId);
                if (!File.Exists(path))
                {
                    Debug.Log($"[WakeWord] WakeWordProfileStorage: No profile file at '{path}'.");
                    return false;
                }

                string json = File.ReadAllText(path);
                profile = JsonUtility.FromJson<WakeWordProfile>(json);
                if (profile == null)
                {
                    Debug.LogWarning($"[WakeWord] WakeWordProfileStorage: Failed to parse profile JSON at '{path}'.");
                    return false;
                }

                Debug.Log($"[WakeWord] WakeWordProfileStorage: Loaded profile '{profileId}' from '{path}'.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WakeWord] WakeWordProfileStorage.TryLoadProfile: Failed to load profile '{profileId}'. {e}");
                profile = null;
                return false;
            }
        }

        public static SerializedEmbedding FromMatrix(float[,] matrix)
        {
            if (matrix == null)
                return null;

            int embeddingDim = matrix.GetLength(0);
            int timeSteps = matrix.GetLength(1);
            int len = embeddingDim * timeSteps;

            var flat = new float[len];
            int idx = 0;
            for (int d = 0; d < embeddingDim; d++)
            {
                for (int t = 0; t < timeSteps; t++)
                {
                    flat[idx++] = matrix[d, t];
                }
            }

            return new SerializedEmbedding
            {
                embeddingDim = embeddingDim,
                timeSteps = timeSteps,
                data = flat
            };
        }

        public static float[,] ToMatrix(SerializedEmbedding e)
        {
            if (e == null || e.data == null)
                return null;

            int expectedLen = e.embeddingDim * e.timeSteps;
            if (e.data.Length != expectedLen)
            {
                Debug.LogWarning($"[WakeWord] WakeWordProfileStorage.ToMatrix: data length {e.data.Length} != expected {expectedLen}.");
                return null;
            }

            var matrix = new float[e.embeddingDim, e.timeSteps];
            int idx = 0;
            for (int d = 0; d < e.embeddingDim; d++)
            {
                for (int t = 0; t < e.timeSteps; t++)
                {
                    matrix[d, t] = e.data[idx++];
                }
            }
            return matrix;
        }
    }
}


