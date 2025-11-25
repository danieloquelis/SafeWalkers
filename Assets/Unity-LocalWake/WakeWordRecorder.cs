using System.Collections.Generic;
using UnityEngine;


namespace LocalWake.Unity
{
    /// <summary>
    /// Recorder flow: captures user wake-word samples and produces embeddings.
    /// Use this in a "setup" scene or phase; pass its samples to WakeWordManager.SetReferences.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class WakeWordRecorder : MonoBehaviour
    {
        [Header("Embedding")]
        [SerializeField] int embeddingDim = 96;
        [SerializeField, Min(1)] int timeSteps = 64;

        [Header("Audio")]
        [SerializeField] int sampleRate = 16000;
        [SerializeField, Min(0.5f)] float windowSeconds = 2f;

        [Header("Wake Word")]
        [SerializeField] string wakeWordName = "wake_word";

        [Header("Debugging")]
        [SerializeField] bool debugLogging = false;

        [Header("Lifecycle Events")]
        [SerializeField] UnityEngine.Events.UnityEvent onRecorderReady;

        [Header("Training Events")]
        [SerializeField] UnityEngine.Events.UnityEvent onEmbeddingCaptureStarted;
        [SerializeField] UnityEngine.Events.UnityEvent onEmbeddingCaptureFinished;

        public string WakeWordName => wakeWordName;

        public IReadOnlyList<float[,]> Samples => _samples;

        AudioSource _audioSource;
        SpeechEmbeddingModel _embeddingModel;

        AudioClip _micClip;
        string _deviceName;
        int _micLastPos;

        float[] _ringBuffer;
        int _ringWritePos;

        readonly List<float[,]> _samples = new();

        Coroutine _captureCoroutine;
        bool _isCapturing;

        float _nextAudioLogTime;
        const string DefaultProfileId = "default";

        const string LogTag = "[WakeWord]";

        void Awake()
        {
            _audioSource = GetComponent<AudioSource>();

            int bufferSamples = Mathf.RoundToInt(windowSeconds * sampleRate);
            _ringBuffer = new float[bufferSamples];

            _embeddingModel = new SpeechEmbeddingModel(
                sampleRate,
                embeddingDim,
                bufferSamples,
                timeSteps);

            Log($"Awake: sampleRate={sampleRate}, windowSeconds={windowSeconds}, bufferSamples={bufferSamples}");
        }

        void OnDestroy()
        {
            _embeddingModel?.Dispose();
            Log("OnDestroy: embedding model disposed.");
        }

        void OnDisable()
        {
            if (_captureCoroutine != null)
            {
                StopCoroutine(_captureCoroutine);
                _captureCoroutine = null;
            }
            _isCapturing = false;
            StopMic();
            Log("OnDisable: stopped capture and microphone.");
        }

        void StopMic()
        {
            if (_micClip != null)
            {
                Log($"StopMic: stopping mic '{_deviceName}'.");
                _audioSource.Stop();
                Microphone.End(_deviceName);
                _micClip = null;
            }
        }

        void Update()
        {
            if (!_isCapturing || _micClip == null || _embeddingModel == null)
                return;

            ReadMicIntoRingBuffer();
        }

        void ReadMicIntoRingBuffer()
        {
            int micPos = Microphone.GetPosition(_deviceName);
            int clipSamples = _micClip.samples;

            int delta = micPos - _micLastPos;
            if (delta < 0)
                delta += clipSamples;

            if (delta <= 0)
            {
                if (debugLogging && Time.realtimeSinceStartup >= _nextAudioLogTime)
                {
                    _nextAudioLogTime = Time.realtimeSinceStartup + 0.25f;
                    Log($"ReadMicIntoRingBuffer: micPos={micPos}, lastPos={_micLastPos}, delta={delta} (no new samples).");
                }
                return;
            }

            if (_micLastPos + delta <= clipSamples)
            {
                var temp = new float[delta];
                _micClip.GetData(temp, _micLastPos);
                PushSamples(temp);
            }
            else
            {
                int tail = clipSamples - _micLastPos;
                var tempTail = new float[tail];
                _micClip.GetData(tempTail, _micLastPos);
                PushSamples(tempTail);

                int head = delta - tail;
                var tempHead = new float[head];
                _micClip.GetData(tempHead, 0);
                PushSamples(tempHead);
            }

            _micLastPos = micPos;

            if (debugLogging && Time.realtimeSinceStartup >= _nextAudioLogTime)
            {
                _nextAudioLogTime = Time.realtimeSinceStartup + 0.25f;
                Log($"ReadMicIntoRingBuffer: micPos={micPos}, delta={delta}, ringWritePos={_ringWritePos}");
            }
        }

        void PushSamples(float[] samples)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                _ringBuffer[_ringWritePos] = samples[i];
                _ringWritePos++;
                if (_ringWritePos >= _ringBuffer.Length)
                    _ringWritePos = 0;
            }
        }

        float[] SnapshotWindow()
        {
            var snapshot = new float[_ringBuffer.Length];
            int idx = _ringWritePos;
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i] = _ringBuffer[idx];
                idx++;
                if (idx >= _ringBuffer.Length)
                    idx = 0;
            }
            return snapshot;
        }

        public void CaptureSample()
        {
            if (_embeddingModel == null)
            {
                LogWarning("Cannot capture sample, model not initialized.");
                return;
            }

            if (_captureCoroutine != null)
            {
                LogWarning("Capture already in progress.");
                return;
            }

            Log("CaptureSample: starting capture coroutine.");
            _captureCoroutine = StartCoroutine(CaptureSampleRoutine());
        }

        System.Collections.IEnumerator CaptureSampleRoutine()
        {
            if (Microphone.devices.Length == 0)
            {
                LogWarning("No microphone devices found.");
                _captureCoroutine = null;
                yield break;
            }

            Log($"CaptureSampleRoutine: devices={string.Join(",", Microphone.devices)}");

            onEmbeddingCaptureStarted?.Invoke();

            _deviceName = Microphone.devices[0];
            _micClip = Microphone.Start(_deviceName, false, Mathf.CeilToInt(windowSeconds), sampleRate);
            _audioSource.clip = _micClip;
            _audioSource.loop = false;

            Log($"CaptureSampleRoutine: Microphone.Start(name='{_deviceName}', lenSec={Mathf.CeilToInt(windowSeconds)}, freq={sampleRate})");

            // Wait asynchronously for the microphone to start, with a timeout to avoid hangs.
            const float timeoutSeconds = 5f;
            float startTime = Time.realtimeSinceStartup;
            while (Microphone.GetPosition(_deviceName) <= 0 &&
                   Time.realtimeSinceStartup - startTime < timeoutSeconds)
            {
                yield return null;
            }

            if (Microphone.GetPosition(_deviceName) <= 0)
            {
                LogWarning("Timeout while starting microphone.");
                StopMic();
                _captureCoroutine = null;
                yield break;
            }

            // Clear the ring buffer and begin capturing for a fixed window.
            System.Array.Clear(_ringBuffer, 0, _ringBuffer.Length);
            _ringWritePos = 0;
            _micLastPos = Microphone.GetPosition(_deviceName);
            _isCapturing = true;

            float elapsed = 0f;
            while (elapsed < windowSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            _isCapturing = false;

            var audio = SnapshotWindow();
            if (debugLogging)
                Log($"CaptureSampleRoutine: snapshot length={audio.Length}, firstSample={audio[0]}");

            var embedding = _embeddingModel.ComputeEmbedding(audio);
            _samples.Add(embedding);

            StopMic();

            onEmbeddingCaptureFinished?.Invoke();
            _captureCoroutine = null;
        }

        public void ClearSamples()
        {
            _samples.Clear();
        }

        public void SetWakeWordName(string name)
        {
            wakeWordName = name;
        }

        /// <summary>
        /// Serialize current recorded embeddings and save them to disk as a wake-word profile.
        /// Call this after recording samples; the profile will be loaded automatically by
        /// WakeWordManager (using the same profileId) on next app start.
        /// </summary>
        /// <param name="profileId">Identifier for the profile (e.g., per-user). If empty, 'default' is used.</param>
        public void SaveProfile(string profileId = DefaultProfileId)
        {
            if (_samples.Count == 0)
            {
                LogWarning("SaveProfile: No samples to save.");
                return;
            }

            var profile = new WakeWordProfileStorage.WakeWordProfile
            {
                wakeWordName = wakeWordName
            };

            foreach (var emb in _samples)
            {
                var ser = WakeWordProfileStorage.FromMatrix(emb);
                if (ser != null)
                    profile.references.Add(ser);
            }

            if (profile.references.Count == 0)
            {
                LogWarning("SaveProfile: All embeddings were null or invalid, nothing saved.");
                return;
            }

            WakeWordProfileStorage.SaveProfile(profileId, profile);
        }

        void Log(string message)
        {
            if (!debugLogging) return;
            Debug.Log($"{LogTag} {message}");
        }

        void LogWarning(string message)
        {
            Debug.LogWarning($"{LogTag} {message}");
        }
    }
}


