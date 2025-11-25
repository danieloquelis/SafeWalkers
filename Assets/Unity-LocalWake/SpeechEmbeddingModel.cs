using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LocalWake.Unity
{
    /// <summary>
    /// Thin C# wrapper around the native SpeechEmbeddingPlugin.
    /// Responsible for initializing the native engine and turning
    /// 1D audio windows into [embeddingDim, timeSteps] features.
    /// </summary>
    public sealed class SpeechEmbeddingModel : IDisposable
    {
        const string LibName = "wordwake";

        static class Native
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            [DllImport(LibName)]
            internal static extern int LW_Init(int sampleRate, int embeddingDim, int windowSamples, int timeSteps);

            [DllImport(LibName)]
            internal static extern int LW_InitFromOnnxBytes(byte[] modelData,
                                                            int   modelSize,
                                                            int   sampleRate,
                                                            int   embeddingDim,
                                                            int   windowSamples,
                                                            int   timeSteps);

            [DllImport(LibName)]
            internal static extern int LW_ComputeEmbedding(float[] audioSamples,
                                                           int    numSamples,
                                                           float[] outEmbedding,
                                                           int    outLength);

            [DllImport(LibName)]
            internal static extern void LW_Shutdown();
#else
            // Stub implementations for platforms where the native plugin is not available.
            internal static int LW_Init(int sampleRate, int embeddingDim, int windowSamples, int timeSteps) => 0;

            internal static int LW_InitFromOnnxBytes(byte[] modelData,
                                                     int   modelSize,
                                                     int   sampleRate,
                                                     int   embeddingDim,
                                                     int   windowSamples,
                                                     int   timeSteps) => 0;

            internal static int LW_ComputeEmbedding(float[] audioSamples,
                                                    int    numSamples,
                                                    float[] outEmbedding,
                                                    int    outLength) => 0;

            internal static void LW_Shutdown() { }
#endif
        }

        readonly int _embeddingDim;
        readonly int _timeSteps;
        readonly int _windowSamples;

        public SpeechEmbeddingModel(int sampleRate,
                                    int embeddingDim,
                                    int windowSamples,
                                    int timeSteps)
        {
            _embeddingDim   = embeddingDim;
            _timeSteps      = timeSteps;
            _windowSamples  = windowSamples;

            int ok = 0;

            // Try to initialize from the ONNX model in Resources/speech-embedding.onnx.
            try
            {
                // Use Unity Resources so it also works on device builds where the file
                // is packed inside the APK and not directly on the filesystem.
                var onnxAsset = Resources.Load<TextAsset>("speech-embedding");
                if (onnxAsset != null && onnxAsset.bytes != null && onnxAsset.bytes.Length > 0)
                {
                    var bytes = onnxAsset.bytes;
                    Debug.Log($"[WakeWord] SpeechEmbeddingModel: Initializing from ONNX bytes (size={bytes.Length}).");

                    ok = Native.LW_InitFromOnnxBytes(bytes,
                                                     onnxAsset.bytes.Length,
                                                     sampleRate,
                                                     embeddingDim,
                                                     windowSamples,
                                                     timeSteps);
                }
                else
                {
                    Debug.LogWarning("[WakeWord] SpeechEmbeddingModel: ONNX TextAsset 'speech-embedding' not found or empty. Falling back.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WakeWord] SpeechEmbeddingModel: Failed to init from ONNX bytes, falling back. {e}");
            }

            // Fallback to simple CPU placeholder if ONNX init failed or model not found.
            if (ok == 0)
            {
                try
                {
                    ok = Native.LW_Init(sampleRate, embeddingDim, windowSamples, timeSteps);
                    Debug.Log("[WakeWord] SpeechEmbeddingModel: Initialized fallback CPU embedding model.");
                }
                catch (DllNotFoundException e)
                {
                    Debug.LogError($"[WakeWord] SpeechEmbeddingModel: Failed to load native plugin '{LibName}'. " +
                                   $"Ensure lib{LibName}.so is present for this platform. Exception: {e}");
                }
            }

            if (ok == 0)
            {
                Debug.LogError("[WakeWord] SpeechEmbeddingModel: Initialization failed. Embeddings will be zero.");
            }
        }

        public float[,] ComputeEmbedding(float[] samples)
        {
            if (samples == null || samples.Length == 0)
                throw new ArgumentException("samples must be non-null and non-empty.", nameof(samples));

            if (samples.Length != _windowSamples)
            {
                Debug.LogWarning($"SpeechEmbeddingModel: Expected {_windowSamples} samples but got {samples.Length}. " +
                                 "Results may be inconsistent.");
            }

            int outLen = _embeddingDim * _timeSteps;
            var flat = new float[outLen];

            int ok = 0;
            try
            {
                ok = Native.LW_ComputeEmbedding(samples, samples.Length, flat, flat.Length);
            }
            catch (DllNotFoundException e)
            {
                Debug.LogError($"SpeechEmbeddingModel: Native LW_ComputeEmbedding call failed. Exception: {e}");
            }

            if (ok == 0)
            {
                Debug.LogError("SpeechEmbeddingModel: LW_ComputeEmbedding returned failure. Using zeros.");
                Array.Clear(flat, 0, flat.Length);
            }

            // Convert flat [d * t] buffer into [d, t] matrix.
            var features = new float[_embeddingDim, _timeSteps];
            for (int d = 0; d < _embeddingDim; d++)
            {
                for (int t = 0; t < _timeSteps; t++)
                {
                    int idx = d * _timeSteps + t;
                    features[d, t] = flat[idx];
                }
            }

            return features;
        }

        public void Dispose()
        {
            try
            {
                Native.LW_Shutdown();
            }
            catch (DllNotFoundException)
            {
                // Ignore: plugin not present for this platform.
            }
        }
    }
}
