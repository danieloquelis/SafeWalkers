using System;
using System.Collections;
using OpenAI;
using UnityEngine;
using UnityEngine.Events;

namespace SafeWalkers.WakeWord
{
    /// <summary>
    /// Main controller for wake word detection system.
    /// Provides two modes: Training (record and store wake word) and Detection (continuously listen for wake word).
    /// Uses OpenAI Whisper API for speech-to-text transcription.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class WakeWordController : MonoBehaviour
    {
        #region Enums

        /// <summary>
        /// State machine for wake word system.
        /// </summary>
        public enum WakeWordState
        {
            Idle,           // Not recording or detecting
            ReadyToHear,    // Microphone initialized, ready to record
            Hearing,        // Currently recording audio
            Processing,     // Sending audio to Whisper API
            Captured,       // Wake word successfully recorded and stored
            Armed,          // Detection mode active, listening for wake word
            Listening,      // Actively capturing audio for detection
            Transcribing,   // Transcribing captured audio
            Detected        // Wake word detected
        }

        #endregion

        #region Serialized Fields

        [Header("Configuration")]
        [SerializeField, Tooltip("OpenAI configuration asset containing API key for Whisper API")]
        private OpenAIConfig openAIConfig;

        [Header("Audio Settings")]
        [SerializeField] private int sampleRate = 16000;
        [SerializeField] private AudioSource audioSource;

        [Header("Recording Configuration")]
        [SerializeField, Range(1f, 10f)] private float recordingDuration = 3f;
        [SerializeField, Tooltip("Timeout for microphone initialization in seconds")]
        private float micInitTimeout = 5f;

        [Header("Detection Configuration")]
        [SerializeField, Range(0.5f, 5f), Tooltip("How often to check for wake word (in seconds)")]
        private float detectionInterval = 1f;
        [SerializeField, Range(2f, 10f), Tooltip("Audio window size for detection (should be longer than interval for overlap)")]
        private float audioWindowSize = 3f;
        [SerializeField, Range(0f, 1f)] private float matchingThreshold = 0.8f;
        [SerializeField, Range(0, 5)] private int maxLevenshteinDistance = 2;
        [SerializeField] private bool caseSensitive = false;
        [SerializeField, Tooltip("Cooldown period after detection to prevent multiple triggers")]
        private float detectionCooldownSeconds = 2f;

        [Header("Persistence")]
        [SerializeField] private string wakeWordPrefsKey = "WakeWord_StoredText";
        [SerializeField] private string settingsFileName = "wakeword_settings.json";
        [SerializeField, Tooltip("Load wake word from storage on start")]
        private bool loadOnStart = true;

        [Header("Training Events")]
        public UnityEvent onReadyToHear;
        public UnityEvent onProcessing;
        public UnityEvent<string> onWordCaptured;
        public UnityEvent<string> onTrainingError;

        [Header("Detection Events")]
        public UnityEvent onArmed;
        public UnityEvent onDisarmed;
        public UnityEvent<string, float> onWakeWordDetected;
        public UnityEvent<string> onDetectionError;

        [Header("Persistence Events")]
        public UnityEvent<string> onWakeWordLoaded;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging = false;

        #endregion

        #region C# Events

        /// <summary>
        /// C# event fired when ready to hear (alternative to UnityEvent).
        /// </summary>
        public event Action OnReadyToHear;

        /// <summary>
        /// C# event fired when processing audio.
        /// </summary>
        public event Action OnProcessing;

        /// <summary>
        /// C# event fired when wake word is captured.
        /// </summary>
        public event Action<string> OnWordCaptured;

        /// <summary>
        /// C# event fired when wake word is detected.
        /// </summary>
        public event Action<string, float> OnWakeWordDetected;

        #endregion

        #region Private Fields

        private WakeWordState _currentState = WakeWordState.Idle;
        private string _storedWakeWord;
        private AudioClip _recordingClip;
        private string _deviceName;
        private Coroutine _activeCoroutine;
        private bool _isArmed = false;
        private float _detectionCooldownTimer = 0f;
        private bool _detectionLocked = false;
        private WhisperClient _whisperClient;
        private WakeWordSettings _settings;

        private const string LogTag = "[WakeWordController]";

        #endregion

        #region Properties

        /// <summary>
        /// Current state of the wake word system.
        /// </summary>
        public WakeWordState CurrentState => _currentState;

        /// <summary>
        /// Whether the system is currently armed for detection.
        /// </summary>
        public bool IsArmed => _isArmed;

        /// <summary>
        /// The stored wake word text.
        /// </summary>
        public string StoredWakeWord => _storedWakeWord;

        /// <summary>
        /// Recording duration in seconds.
        /// </summary>
        public float RecordingDuration
        {
            get => recordingDuration;
            set => recordingDuration = Mathf.Clamp(value, 1f, 10f);
        }

        /// <summary>
        /// Detection interval in seconds.
        /// </summary>
        public float DetectionInterval
        {
            get => detectionInterval;
            set => detectionInterval = Mathf.Clamp(value, 0.5f, 5f);
        }

        /// <summary>
        /// Matching threshold for fuzzy matching.
        /// </summary>
        public float MatchingThreshold
        {
            get => matchingThreshold;
            set => matchingThreshold = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Maximum Levenshtein edit distance for matching.
        /// </summary>
        public int MaxLevenshteinDistance
        {
            get => maxLevenshteinDistance;
            set => maxLevenshteinDistance = Mathf.Clamp(value, 0, 5);
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // Get or add AudioSource component
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            // Get WhisperClient instance
            _whisperClient = WhisperClient.Instance;

            // Configure WhisperClient with OpenAI config
            if (openAIConfig != null)
            {
                _whisperClient.SetOpenAIConfig(openAIConfig);
            }
            else
            {
                Debug.LogError("[WakeWordController] OpenAIConfig not assigned! Please assign the OpenAIConfig asset in the Inspector.");
            }

            // Load settings
            LoadSettings();

            // Load stored wake word if configured
            if (loadOnStart)
            {
                LoadWakeWord();
            }

            Log("WakeWordController initialized");
        }

        private void Start()
        {
            // Request microphone permission on Android/Meta Quest
            #if UNITY_ANDROID
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                Log("Requesting microphone permission...");
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
            }
            else
            {
                Log("Microphone permission already granted");
            }
            #endif
        }

        private void Update()
        {
            // Handle detection cooldown
            if (_detectionLocked)
            {
                _detectionCooldownTimer -= Time.deltaTime;
                if (_detectionCooldownTimer <= 0f)
                {
                    _detectionLocked = false;
                    Log("Detection cooldown ended");
                }
            }
        }

        private void OnDestroy()
        {
            // Clean up
            if (_activeCoroutine != null)
            {
                StopCoroutine(_activeCoroutine);
            }

            if (Microphone.IsRecording(_deviceName))
            {
                Microphone.End(_deviceName);
            }
        }

        #endregion

        #region Public API - Training Mode

        /// <summary>
        /// Starts the wake word training/recording process.
        /// Records audio for the configured duration, transcribes it, and stores the result.
        /// </summary>
        public void StartHearing()
        {
            if (_currentState != WakeWordState.Idle)
            {
                LogWarning($"Cannot start hearing in state: {_currentState}");
                return;
            }

            // Check for microphone permission on Android/Meta Quest
            #if UNITY_ANDROID
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                string error = "Microphone permission not granted";
                LogError(error);
                onTrainingError?.Invoke(error);
                return;
            }
            #endif

            // Check for microphone availability
            if (Microphone.devices.Length == 0)
            {
                string error = "No microphone detected";
                LogError(error);
                onTrainingError?.Invoke(error);
                return;
            }

            _activeCoroutine = StartCoroutine(RecordAndTranscribeCoroutine());
        }

        /// <summary>
        /// Stops the current hearing/training process.
        /// </summary>
        public void StopHearing()
        {
            if (_currentState == WakeWordState.Hearing || _currentState == WakeWordState.ReadyToHear)
            {
                if (_activeCoroutine != null)
                {
                    StopCoroutine(_activeCoroutine);
                    _activeCoroutine = null;
                }

                StopMicrophone();
                _currentState = WakeWordState.Idle;
                Log("Hearing stopped");
            }
        }

        #endregion

        #region Public API - Detection Mode

        /// <summary>
        /// Arms the wake word detection system.
        /// Starts continuous listening and detection of the stored wake word.
        /// </summary>
        public void Arm()
        {
            if (_isArmed)
            {
                LogWarning("System already armed");
                return;
            }

            // Always reload wake word from storage to ensure we have the latest value
            Log("Reloading wake word from storage before arming...");
            if (!LoadWakeWord(triggerEvent: false))
            {
                string error = "No wake word trained. Please record a wake word first.";
                LogError(error);
                onDetectionError?.Invoke(error);
                return;
            }
            Log($"Wake word reloaded for detection: '{_storedWakeWord}'");

            // Check for microphone permission on Android/Meta Quest
            #if UNITY_ANDROID
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                string error = "Microphone permission not granted";
                LogError(error);
                onDetectionError?.Invoke(error);
                return;
            }
            #endif

            // Check for microphone availability
            if (Microphone.devices.Length == 0)
            {
                string error = "No microphone detected";
                LogError(error);
                onDetectionError?.Invoke(error);
                return;
            }

            // Set armed state immediately for instant UI feedback
            _isArmed = true;
            _currentState = WakeWordState.Armed;
            onArmed?.Invoke();
            Log($"Arming system - initializing microphone...");

            _activeCoroutine = StartCoroutine(DetectionLoopCoroutine());
        }

        /// <summary>
        /// Disarms the wake word detection system.
        /// Stops listening for the wake word.
        /// </summary>
        public void Disarm()
        {
            if (!_isArmed)
            {
                return;
            }

            _isArmed = false;

            if (_activeCoroutine != null)
            {
                StopCoroutine(_activeCoroutine);
                _activeCoroutine = null;
            }

            StopMicrophone();

            _currentState = WakeWordState.Idle;
            onDisarmed?.Invoke();
            Log("System disarmed");
        }

        #endregion

        #region Public API - Persistence

        /// <summary>
        /// Clears the stored wake word from PlayerPrefs and settings file.
        /// </summary>
        public void ClearWakeWord()
        {
            PlayerPrefs.DeleteKey(wakeWordPrefsKey);
            PlayerPrefs.DeleteKey($"{wakeWordPrefsKey}_LastTraining");
            PlayerPrefs.Save();

            _storedWakeWord = string.Empty;

            if (_settings != null)
            {
                _settings.storedWakeWord = string.Empty;
                _settings.SaveToFile(settingsFileName);
            }

            Log("Wake word cleared");
        }

        /// <summary>
        /// Gets the stored wake word without loading from disk.
        /// </summary>
        public string GetStoredWakeWord()
        {
            return _storedWakeWord;
        }

        /// <summary>
        /// Checks if a wake word is currently stored.
        /// </summary>
        public bool HasStoredWakeWord()
        {
            return !string.IsNullOrEmpty(_storedWakeWord) ||
                   PlayerPrefs.HasKey(wakeWordPrefsKey);
        }

        /// <summary>
        /// Checks for and loads a stored wake word from PlayerPrefs or settings file.
        /// Triggers onWakeWordLoaded event only if a wake word exists.
        /// </summary>
        /// <returns>True if a wake word was loaded, false otherwise</returns>
        public bool CheckAndLoadStoredWakeWord()
        {
            return LoadWakeWord(triggerEvent: true);
        }

        #endregion

        #region Private Methods - Training Mode

        private IEnumerator RecordAndTranscribeCoroutine()
        {
            // Initialize microphone
            _deviceName = Microphone.devices[0];
            Log($"Using microphone: {_deviceName}");

            _recordingClip = Microphone.Start(_deviceName, false, (int)recordingDuration + 1, sampleRate);

            if (_recordingClip == null)
            {
                string error = "Failed to start microphone";
                LogError(error);
                onTrainingError?.Invoke(error);
                yield break;
            }

            // Transition to ReadyToHear
            _currentState = WakeWordState.ReadyToHear;
            onReadyToHear?.Invoke();
            OnReadyToHear?.Invoke();
            Log("Ready to hear - waiting for microphone initialization");

            // Wait for microphone to start (with timeout)
            float elapsed = 0f;
            while (Microphone.GetPosition(_deviceName) <= 0 && elapsed < micInitTimeout)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }

            if (Microphone.GetPosition(_deviceName) <= 0)
            {
                string error = "Microphone initialization timeout";
                LogError(error);
                StopMicrophone();
                onTrainingError?.Invoke(error);
                _currentState = WakeWordState.Idle;
                yield break;
            }

            // Start recording
            _currentState = WakeWordState.Hearing;
            Log($"Recording for {recordingDuration} seconds...");

            // Wait for recording duration
            yield return new WaitForSeconds(recordingDuration);

            // Save reference to recording clip before stopping microphone
            AudioClip recordedClip = _recordingClip;

            // Stop microphone
            StopMicrophone();

            // Transition to Processing
            _currentState = WakeWordState.Processing;
            onProcessing?.Invoke();
            OnProcessing?.Invoke();
            Log("Processing audio...");

            // Convert AudioClip to WAV
            byte[] wavData = AudioUtils.ConvertAudioClipToWav(recordedClip);

            if (wavData == null || wavData.Length == 0)
            {
                string error = "Failed to convert audio to WAV format";
                LogError(error);
                onTrainingError?.Invoke(error);
                _currentState = WakeWordState.Idle;
                yield break;
            }

            Log($"Audio converted to WAV ({wavData.Length} bytes)");

            // Send to Whisper API
            bool success = false;
            string transcription = null;

            _whisperClient.TranscribeAudio(wavData, "wakeword.wav", (text, succeeded) =>
            {
                transcription = text;
                success = succeeded;
            });

            // Wait for response (with timeout)
            float transcribeElapsed = 0f;
            while (transcription == null && !success && transcribeElapsed < 30f)
            {
                yield return null;
                transcribeElapsed += Time.deltaTime;
            }

            if (success && !string.IsNullOrEmpty(transcription))
            {
                // Store wake word
                SaveWakeWord(transcription);

                _currentState = WakeWordState.Captured;
                onWordCaptured?.Invoke(transcription);
                OnWordCaptured?.Invoke(transcription);
                Log($"Wake word captured: '{transcription}'");

                // Return to idle after a short delay
                yield return new WaitForSeconds(0.5f);
                _currentState = WakeWordState.Idle;
            }
            else
            {
                string error = success ? "Transcription returned empty text" : "Failed to transcribe audio";
                LogError(error);
                onTrainingError?.Invoke(error);
                _currentState = WakeWordState.Idle;
            }

            _activeCoroutine = null;
        }

        #endregion

        #region Private Methods - Detection Mode

        private IEnumerator DetectionLoopCoroutine()
        {
            _deviceName = Microphone.devices[0];
            Log($"Starting detection loop with microphone: {_deviceName}");

            // Start continuous microphone recording
            _recordingClip = Microphone.Start(_deviceName, true, 10, sampleRate);

            if (_recordingClip == null)
            {
                string error = "Failed to start microphone for detection";
                LogError(error);
                onDetectionError?.Invoke(error);
                yield break;
            }

            // Wait for microphone to initialize
            float elapsed = 0f;
            while (Microphone.GetPosition(_deviceName) <= 0 && elapsed < micInitTimeout)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }

            if (Microphone.GetPosition(_deviceName) <= 0)
            {
                string error = "Microphone initialization timeout";
                LogError(error);
                StopMicrophone();
                _isArmed = false;
                _currentState = WakeWordState.Idle;
                onDetectionError?.Invoke(error);
                yield break;
            }

            // Microphone initialized successfully
            Log($"Microphone initialized - now actively listening for wake word: '{_storedWakeWord}'");
            Log($"Detection parameters: interval={detectionInterval}s, window={audioWindowSize}s, overlap={audioWindowSize - detectionInterval}s");

            // Detection loop
            while (_isArmed)
            {
                // Wait for detection interval
                yield return new WaitForSeconds(detectionInterval);

                // Skip if in cooldown
                if (_detectionLocked)
                {
                    continue;
                }

                // Capture current audio window (larger than interval for overlap)
                _currentState = WakeWordState.Listening;
                int sampleCount = Mathf.RoundToInt(sampleRate * audioWindowSize);
                float[] samples = new float[sampleCount];

                int micPosition = Microphone.GetPosition(_deviceName);
                int startPosition = micPosition - sampleCount;

                // Handle ring buffer wraparound
                if (startPosition < 0)
                {
                    // If we wrapped around, we need to get data from the end of the buffer
                    int clipLength = _recordingClip.samples;
                    int samplesAtEnd = -startPosition;
                    int samplesAtStart = sampleCount - samplesAtEnd;

                    float[] tempSamples = new float[clipLength];
                    _recordingClip.GetData(tempSamples, 0);

                    // Copy from end of buffer
                    Array.Copy(tempSamples, clipLength - samplesAtEnd, samples, 0, samplesAtEnd);
                    // Copy from start of buffer
                    Array.Copy(tempSamples, 0, samples, samplesAtEnd, samplesAtStart);
                }
                else
                {
                    _recordingClip.GetData(samples, startPosition);
                }

                // Convert to AudioClip for WAV conversion
                AudioClip tempClip = AudioClip.Create("detection_temp", samples.Length, 1, sampleRate, false);
                tempClip.SetData(samples, 0);

                // Convert to WAV
                byte[] wavData = AudioUtils.ConvertAudioClipToWav(tempClip);

                if (wavData == null || wavData.Length == 0)
                {
                    LogWarning("Failed to convert detection audio to WAV");
                    continue;
                }

                // Transcribe
                _currentState = WakeWordState.Transcribing;
                bool transcribeSuccess = false;
                string transcription = null;

                _whisperClient.TranscribeAudio(wavData, "detection.wav", (text, succeeded) =>
                {
                    transcription = text;
                    transcribeSuccess = succeeded;
                });

                // Wait for transcription (with timeout)
                float transcribeElapsed = 0f;
                while (transcription == null && !transcribeSuccess && transcribeElapsed < 10f)
                {
                    yield return null;
                    transcribeElapsed += Time.deltaTime;
                }

                if (transcribeSuccess && !string.IsNullOrEmpty(transcription))
                {
                    Log($"Transcribed: '{transcription}' | Comparing against stored wake word: '{_storedWakeWord}'");

                    // Check for match
                    bool isMatch = FuzzyMatcher.FuzzyMatch(
                        transcription,
                        _storedWakeWord,
                        matchingThreshold,
                        caseSensitive,
                        maxLevenshteinDistance);

                    if (isMatch)
                    {
                        // Wake word detected!
                        _currentState = WakeWordState.Detected;
                        Log($"Wake word detected! Transcription: '{transcription}'");

                        // Calculate similarity for confidence score
                        float similarity = FuzzyMatcher.NormalizedSimilarity(
                            FuzzyMatcher.NormalizeText(transcription, caseSensitive),
                            FuzzyMatcher.NormalizeText(_storedWakeWord, caseSensitive));

                        onWakeWordDetected?.Invoke(transcription, similarity);
                        OnWakeWordDetected?.Invoke(transcription, similarity);

                        // Enter cooldown
                        _detectionLocked = true;
                        _detectionCooldownTimer = detectionCooldownSeconds;

                        // Auto-disarm after detection
                        Disarm();
                        yield break;
                    }
                }

                // Return to armed state for next iteration
                _currentState = WakeWordState.Armed;
            }

            _activeCoroutine = null;
        }

        #endregion

        #region Private Methods - Persistence

        private void SaveWakeWord(string text)
        {
            _storedWakeWord = text;

            // Save to PlayerPrefs
            PlayerPrefs.SetString(wakeWordPrefsKey, text);
            PlayerPrefs.SetString($"{wakeWordPrefsKey}_LastTraining", DateTime.UtcNow.ToString("o"));
            PlayerPrefs.Save();

            // Save to settings file
            if (_settings == null)
            {
                _settings = WakeWordSettings.CreateDefault();
            }

            _settings.storedWakeWord = text;
            _settings.matchingThreshold = matchingThreshold;
            _settings.maxLevenshteinDistance = maxLevenshteinDistance;
            _settings.recordingDuration = recordingDuration;
            _settings.detectionInterval = detectionInterval;
            _settings.caseSensitive = caseSensitive;
            _settings.SaveToFile(settingsFileName);

            Log($"Wake word saved: '{text}'");
        }

        private bool LoadWakeWord(bool triggerEvent = true)
        {
            // Try loading from PlayerPrefs first
            if (PlayerPrefs.HasKey(wakeWordPrefsKey))
            {
                _storedWakeWord = PlayerPrefs.GetString(wakeWordPrefsKey);

                if (!string.IsNullOrEmpty(_storedWakeWord))
                {
                    Log($"Wake word loaded from PlayerPrefs: '{_storedWakeWord}'");

                    if (triggerEvent)
                    {
                        onWakeWordLoaded?.Invoke(_storedWakeWord);
                    }

                    return true;
                }
            }

            // Try loading from settings file
            if (_settings != null && !string.IsNullOrEmpty(_settings.storedWakeWord))
            {
                _storedWakeWord = _settings.storedWakeWord;
                Log($"Wake word loaded from settings: '{_storedWakeWord}'");

                if (triggerEvent)
                {
                    onWakeWordLoaded?.Invoke(_storedWakeWord);
                }

                return true;
            }

            Log("No wake word found in storage");
            return false;
        }

        private void LoadSettings()
        {
            _settings = WakeWordSettings.LoadFromFile(settingsFileName);

            if (_settings != null)
            {
                // Apply loaded settings
                matchingThreshold = _settings.matchingThreshold;
                maxLevenshteinDistance = _settings.maxLevenshteinDistance;
                recordingDuration = _settings.recordingDuration;
                detectionInterval = _settings.detectionInterval;
                caseSensitive = _settings.caseSensitive;

                _settings.Validate();
                Log("Settings loaded and applied");
            }
            else
            {
                // Create default settings
                _settings = WakeWordSettings.CreateDefault();
                Log("Created default settings");
            }
        }

        #endregion

        #region Private Methods - Helpers

        private void StopMicrophone()
        {
            if (!string.IsNullOrEmpty(_deviceName) && Microphone.IsRecording(_deviceName))
            {
                Microphone.End(_deviceName);
                Log($"Microphone stopped: {_deviceName}");
            }

            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }

            _recordingClip = null;
        }

        private void Log(string message)
        {
            if (verboseLogging)
            {
                Debug.Log($"{LogTag} {message}");
            }
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"{LogTag} {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"{LogTag} {message}");
        }

        #endregion
    }
}
