using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace LocalWake.Unity
{
    [RequireComponent(typeof(AudioSource))]
    public class WakeWordManager : MonoBehaviour
    {
        [Header("Embedding")]
        [SerializeField] int embeddingDim = 96;
        [SerializeField, Min(1)] int timeSteps = 64;

        [Header("Audio")]
        [SerializeField] int sampleRate = 16000;
        [SerializeField, Min(0.5f)] float windowSeconds = 2f;
        [SerializeField, Min(0.05f)] float hopSeconds = 0.25f;

        [Header("Detection")]
        [SerializeField] float threshold = 0.15f;
        [SerializeField] string wakeWordName = "wake_word";
        [SerializeField] UnityEvent<string, float> onWakeWordDetected;

        [Header("Detection Timing")]
        [SerializeField, Min(0f)] float detectionCooldownSeconds = 2f;

        [Header("Lifecycle Events")]
        [SerializeField] UnityEvent onRecordingReady;

        [Header("Arming Events")]
        [SerializeField] UnityEvent onWakeWordArmed;
        [SerializeField] UnityEvent onWakeWordDetectionReset;

        [Header("Debugging")]
        [SerializeField] bool debugLogging = false;

        const string LogTag = "[WakeWord]";

        public event Action<string, float> OnWakeWordDetected;
        public event Action OnRecordingReady;
        public event Action OnWakeWordArmed;
        public event Action OnWakeWordDetectionReset;

        AudioSource _audioSource;
        SpeechEmbeddingModel _embeddingModel;

        AudioClip _micClip;
        string _deviceName;
        int _micLastPos;

        float[] _ringBuffer;
        int _ringWritePos;
        float _hopTimer;

        List<ReferenceSample> _references = new();

        Coroutine _micCoroutine;

        bool _detectionLocked;
        float _detectionLockTimer;

        float _nextAudioLogTime;

        struct ReferenceSample
        {
            public string Name;
            public float[,] Embedding;
        }

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

            Log($"Awake: sampleRate={sampleRate}, windowSeconds={windowSeconds}, bufferSamples={bufferSamples}, embeddingDim={embeddingDim}, timeSteps={timeSteps}");
        }

        void OnDestroy()
        {
            _embeddingModel?.Dispose();
            Log("OnDestroy: embedding model disposed.");
        }

        void OnEnable()
        {
            _micCoroutine = StartCoroutine(StartMicRoutine());
            Log("OnEnable: starting microphone coroutine.");
        }
 
        void OnDisable()
        {
            if (_micCoroutine != null)
            {
                StopCoroutine(_micCoroutine);
                _micCoroutine = null;
            }
            StopMic();
            Log("OnDisable: stopped microphone.");
        }

        void RestartMic()
        {
            Log("RestartMic: restarting microphone.");

            if (_micCoroutine != null)
            {
                StopCoroutine(_micCoroutine);
                _micCoroutine = null;
            }

            StopMic();
            _micCoroutine = StartCoroutine(StartMicRoutine());
        }

        System.Collections.IEnumerator StartMicRoutine()
        {
            if (Microphone.devices.Length == 0)
            {
                LogWarning("No microphone devices found.");
                yield break;
            }

            _deviceName = Microphone.devices[0];
            _micClip = Microphone.Start(_deviceName, true, 10, sampleRate);
            _audioSource.clip = _micClip;
            _audioSource.loop = true;

            Log($"StartMicRoutine: Microphone.Start(name='{_deviceName}', loop=true, lenSec=10, freq={sampleRate})");

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
                yield break;
            }

            _audioSource.Play();
            _micLastPos = Microphone.GetPosition(_deviceName);

            Log($"StartMicRoutine: microphone started, initial micPos={_micLastPos}, clipSamples={_micClip.samples}");

            // Signal that the wake-word pipeline is ready for recording and detection.
            onRecordingReady?.Invoke();
            OnRecordingReady?.Invoke();
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
            if (_micClip == null || _embeddingModel == null)
                return;

            // Handle cooldown / lockout timing for detections.
            if (_detectionLocked)
            {
                _detectionLockTimer -= Time.deltaTime;
                if (_detectionLockTimer <= 0f)
                {
                    _detectionLocked = false;
                    onWakeWordDetectionReset?.Invoke();
                    OnWakeWordDetectionReset?.Invoke();
                }
            }

            ReadMicIntoRingBuffer();

            _hopTimer += Time.deltaTime;
            if (_hopTimer >= hopSeconds)
            {
                _hopTimer = 0f;
                RunDetection();
            }
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

        public void SetReferences(string name, IList<float[,]> embeddings)
        {
            _references.Clear();
            wakeWordName = name;

            Log($"SetReferences: name='{name}', count={embeddings?.Count ?? 0}");

            if (embeddings == null)
                return;

            for (int i = 0; i < embeddings.Count; i++)
            {
                var emb = embeddings[i];
                if (emb == null)
                {
                    if (debugLogging)
                        Log($"SetReferences: embedding index {i} is null, skipping.");
                    continue;
                }

                _references.Add(new ReferenceSample
                {
                    Name = name,
                    Embedding = emb
                });
            }
            // If we have at least one valid reference, signal that the wake-word detector is armed.
            if (_references.Count > 0)
            {
                onWakeWordArmed?.Invoke();
                OnWakeWordArmed?.Invoke();

                // Clear recent audio and start a short cooldown so we don't immediately
                // fire a detection with stale audio right after arming.
                Array.Clear(_ringBuffer, 0, _ringBuffer.Length);
                _ringWritePos = 0;
                _detectionLocked = true;
                _detectionLockTimer = detectionCooldownSeconds;

                Log($"SetReferences: armed with {_references.Count} references, ring buffer cleared, cooldown={detectionCooldownSeconds}s.");

                // Restart microphone streaming after training phase so we have a fresh,
                // continuous audio stream for wake-word detection even if another
                // component (e.g., WakeWordRecorder) stopped the mic.
                RestartMic();
            }
        }

        void RunDetection()
        {
            if (_references.Count == 0)
                return;

            if (_detectionLocked)
                return;

            var audio = SnapshotWindow();
            if (debugLogging)
                Log($"RunDetection: snapshot length={audio.Length}");
            var currEmbedding = _embeddingModel.ComputeEmbedding(audio);

            foreach (var r in _references)
            {
                float dist = DtwDistance.DtwCosine(currEmbedding, r.Embedding);
                // Note: I am inverting this but real one before is dist < threshold
                if (dist >= threshold)
                {
                    OnWakeWordDetected?.Invoke(r.Name, dist);
                    onWakeWordDetected?.Invoke(r.Name, dist);
                    Array.Clear(_ringBuffer, 0, _ringBuffer.Length);
                    _ringWritePos = 0;

                    // Lock out further detections for a short cooldown period.
                    _detectionLocked = true;
                    _detectionLockTimer = detectionCooldownSeconds;

                    Log($"RunDetection: DETECTED name='{r.Name}', dist={dist}, threshold={threshold}, cooldown={detectionCooldownSeconds}s.");
                    break;
                }
            }
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

