using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenAI;
using UnityEngine;
using UnityEngine.Networking;

namespace SafeWalkers.WakeWord
{
    /// <summary>
    /// Client for OpenAI Whisper API integration.
    /// Handles audio transcription requests with retry logic and rate limiting.
    /// Must call SetOpenAIConfig() before making transcription requests.
    /// </summary>
    public class WhisperClient : MonoBehaviour
    {
        private static WhisperClient _instance;

        /// <summary>
        /// Singleton instance of WhisperClient.
        /// Automatically creates an instance if one doesn't exist.
        /// </summary>
        public static WhisperClient Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("WhisperClient");
                    _instance = go.AddComponent<WhisperClient>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private string _apiKey;
        private const string WhisperEndpoint = "https://api.openai.com/v1/audio/transcriptions";
        private const float MinRequestInterval = 1.2f; // 50 requests/minute
        private float _lastRequestTime = 0f;

        [Header("Request Settings")]
        [SerializeField] private bool verboseLogging = false;
        [SerializeField] private int maxRetries = 3;
        [SerializeField] private float retryDelaySeconds = 1f;
        [SerializeField] private float requestTimeoutSeconds = 30f;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Sets the OpenAI configuration and extracts the API key.
        /// MUST be called before making any transcription requests.
        /// </summary>
        /// <param name="config">The OpenAI configuration asset</param>
        public void SetOpenAIConfig(OpenAIConfig config)
        {
            if (config == null)
            {
                Debug.LogError("[WhisperClient] Cannot set null OpenAIConfig");
                return;
            }

            if (string.IsNullOrEmpty(config.apiKey))
            {
                Debug.LogError("[WhisperClient] OpenAIConfig has empty API key");
                return;
            }

            _apiKey = config.apiKey;
            Log("API key configured successfully");
        }

        /// <summary>
        /// Transcribes audio data using OpenAI Whisper API.
        /// </summary>
        /// <param name="audioData">WAV audio file bytes</param>
        /// <param name="fileName">Name for the audio file</param>
        /// <param name="onComplete">Callback with (transcribedText, success)</param>
        public void TranscribeAudio(byte[] audioData, string fileName, Action<string, bool> onComplete)
        {
            // Validate audio data
            if (audioData == null || audioData.Length == 0)
            {
                Debug.LogError("[WhisperClient] Cannot transcribe empty audio data");
                onComplete?.Invoke(null, false);
                return;
            }

            // Validate API key is configured
            if (string.IsNullOrEmpty(_apiKey))
            {
                Debug.LogError("[WhisperClient] API key not configured. Call SetOpenAIConfig() first.");
                onComplete?.Invoke(null, false);
                return;
            }

            StartCoroutine(TranscribeAudioCoroutine(audioData, fileName, onComplete, 0));
        }

        /// <summary>
        /// Coroutine for transcribing audio with retry logic.
        /// </summary>
        private IEnumerator TranscribeAudioCoroutine(
            byte[] audioData,
            string fileName,
            Action<string, bool> onComplete,
            int retryCount)
        {
            // Rate limiting
            float timeSinceLastRequest = Time.realtimeSinceStartup - _lastRequestTime;
            if (timeSinceLastRequest < MinRequestInterval)
            {
                float waitTime = MinRequestInterval - timeSinceLastRequest;
                Log($"Rate limiting: waiting {waitTime:F2}s");
                yield return new WaitForSeconds(waitTime);
            }

            _lastRequestTime = Time.realtimeSinceStartup;

            // Create multipart form data
            string boundary = "----WebKitFormBoundary" + DateTime.Now.Ticks.ToString("x");
            List<byte> formData = new List<byte>();

            // Add file field
            AddFormField(formData, boundary, "file", fileName, "audio/wav", audioData);

            // Add model field
            AddTextField(formData, boundary, "model", "whisper-1");

            // Add language field (improves accuracy)
            AddTextField(formData, boundary, "language", "en");

            // Add temperature field (deterministic output)
            AddTextField(formData, boundary, "temperature", "0.0");

            // Add closing boundary
            AddBoundary(formData, boundary, true);

            byte[] bodyRaw = formData.ToArray();

            Log($"Sending transcription request (attempt {retryCount + 1}/{maxRetries + 1}, size: {audioData.Length} bytes)");

            using (var request = new UnityWebRequest(WhisperEndpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                request.SetRequestHeader("Content-Type", $"multipart/form-data; boundary={boundary}");
                request.timeout = (int)requestTimeoutSeconds;

                yield return request.SendWebRequest();

                // Variables to track retry state
                bool shouldRetry = false;
                float retryDelay = 0f;
                bool isSuccess = false;

                if (request.result == UnityWebRequest.Result.Success)
                {
                    // Parse response outside of try-catch to avoid yield in catch
                    Exception parseException = null;
                    WhisperResponse response = null;

                    try
                    {
                        response = JsonUtility.FromJson<WhisperResponse>(request.downloadHandler.text);
                    }
                    catch (Exception ex)
                    {
                        parseException = ex;
                    }

                    if (parseException != null)
                    {
                        Debug.LogError($"[WhisperClient] Failed to parse response: {parseException.Message}");
                        Debug.LogError($"[WhisperClient] Response text: {request.downloadHandler.text}");

                        if (retryCount < maxRetries)
                        {
                            shouldRetry = true;
                            retryDelay = retryDelaySeconds * Mathf.Pow(2, retryCount);
                        }
                        else
                        {
                            onComplete?.Invoke(null, false);
                            isSuccess = true;
                        }
                    }
                    else if (response != null)
                    {
                        if (!string.IsNullOrEmpty(response.text))
                        {
                            Log($"Transcription successful: '{response.text}'");
                            onComplete?.Invoke(response.text, true);
                            isSuccess = true;
                        }
                        else
                        {
                            Debug.LogWarning("[WhisperClient] Received empty transcription");
                            onComplete?.Invoke(string.Empty, true);
                            isSuccess = true;
                        }
                    }
                }
                else
                {
                    // Handle errors
                    string errorMessage = $"Request failed: {request.error}";

                    if (request.responseCode == 401)
                    {
                        errorMessage = "Unauthorized: Invalid API key";
                        Debug.LogError($"[WhisperClient] {errorMessage}");
                        onComplete?.Invoke(null, false);
                        isSuccess = true;
                    }
                    else if (request.responseCode == 429)
                    {
                        errorMessage = "Rate limit exceeded";
                        Debug.LogWarning($"[WhisperClient] {errorMessage}");

                        if (retryCount < maxRetries)
                        {
                            shouldRetry = true;
                            retryDelay = retryDelaySeconds * Mathf.Pow(2, retryCount);
                            Log($"Retrying in {retryDelay}s...");
                        }
                        else
                        {
                            onComplete?.Invoke(null, false);
                            isSuccess = true;
                        }
                    }
                    else if (request.responseCode >= 500)
                    {
                        errorMessage = $"Server error: {request.responseCode}";
                        Debug.LogWarning($"[WhisperClient] {errorMessage}");

                        if (retryCount < maxRetries)
                        {
                            shouldRetry = true;
                            retryDelay = retryDelaySeconds * Mathf.Pow(2, retryCount);
                            Log($"Retrying in {retryDelay}s...");
                        }
                        else
                        {
                            onComplete?.Invoke(null, false);
                            isSuccess = true;
                        }
                    }
                    else
                    {
                        Debug.LogError($"[WhisperClient] {errorMessage}");
                        Debug.LogError($"[WhisperClient] Response: {request.downloadHandler?.text}");
                        onComplete?.Invoke(null, false);
                        isSuccess = true;
                    }
                }

                // Handle retry outside the try-catch and error handling blocks
                if (shouldRetry && !isSuccess)
                {
                    yield return new WaitForSeconds(retryDelay);
                    StartCoroutine(TranscribeAudioCoroutine(audioData, fileName, onComplete, retryCount + 1));
                }
            }
        }

        /// <summary>
        /// Adds a file field to the multipart form data.
        /// </summary>
        private void AddFormField(List<byte> formData, string boundary, string name, string fileName, string contentType, byte[] fileData)
        {
            formData.AddRange(Encoding.UTF8.GetBytes($"--{boundary}\r\n"));
            formData.AddRange(Encoding.UTF8.GetBytes($"Content-Disposition: form-data; name=\"{name}\"; filename=\"{fileName}\"\r\n"));
            formData.AddRange(Encoding.UTF8.GetBytes($"Content-Type: {contentType}\r\n\r\n"));
            formData.AddRange(fileData);
            formData.AddRange(Encoding.UTF8.GetBytes("\r\n"));
        }

        /// <summary>
        /// Adds a text field to the multipart form data.
        /// </summary>
        private void AddTextField(List<byte> formData, string boundary, string name, string value)
        {
            formData.AddRange(Encoding.UTF8.GetBytes($"--{boundary}\r\n"));
            formData.AddRange(Encoding.UTF8.GetBytes($"Content-Disposition: form-data; name=\"{name}\"\r\n\r\n"));
            formData.AddRange(Encoding.UTF8.GetBytes($"{value}\r\n"));
        }

        /// <summary>
        /// Adds a boundary to the multipart form data.
        /// </summary>
        private void AddBoundary(List<byte> formData, string boundary, bool isClosing)
        {
            if (isClosing)
            {
                formData.AddRange(Encoding.UTF8.GetBytes($"--{boundary}--\r\n"));
            }
            else
            {
                formData.AddRange(Encoding.UTF8.GetBytes($"--{boundary}\r\n"));
            }
        }

        /// <summary>
        /// Checks if the client can make a request (respects rate limiting).
        /// </summary>
        public bool CanMakeRequest()
        {
            float timeSinceLastRequest = Time.realtimeSinceStartup - _lastRequestTime;
            return timeSinceLastRequest >= MinRequestInterval;
        }

        private void Log(string message)
        {
            if (verboseLogging)
            {
                Debug.Log($"[WhisperClient] {message}");
            }
        }

        /// <summary>
        /// Response structure for Whisper API.
        /// </summary>
        [Serializable]
        private class WhisperResponse
        {
            public string text;
        }
    }
}
