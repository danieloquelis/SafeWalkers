using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MouthAnimator : MonoBehaviour
{
    [Header("Audio Input")]
    [Tooltip("Static AudioClip: Pre-processes entire clip. Leave empty for real-time mode.")]
    public AudioClip audioClip;

    [Tooltip("AudioSource: For static mode (plays audioClip) or real-time mode (monitors external audio).")]
    public AudioSource audioSource;

    [Tooltip("When ON: audio plays and mouth animates. When OFF: audio stops and mouth closes.")]
    public bool lipsyncEnabled = true;

    [Header("Mouth Animation")]
    public SpriteRenderer mouthRenderer;
    public Sprite[] mouthSprites;
    public float amplitudeLerpSpeed = 10f;

    [Header("Eye Animation")]
    public EyeAnimator eyeAnimator;

    [Header("Amplitude Settings")]
    public float updateInterval = 0.05f;
    public int smoothingWindow = 3;
    public float responseCurveExponent = 0.8f;

    [Header("Real-time Mode Settings")]
    [Tooltip("Sample count for GetOutputData() in real-time mode (higher = smoother but more processing)")]
    public int realTimeSampleCount = 1024;

    private enum LipsyncMode { Static, RealTime }
    private Queue<float> amplitudeHistory = new Queue<float>();
    private float currentNormalizedAmplitude = 0f;
    private float displayedAmplitude = 0f;
    private Coroutine lipsyncRoutine;
    private LipsyncMode currentMode = LipsyncMode.Static;

    void Start()
    {
        // Ensure audio source exists
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.clip = audioClip;
        audioSource.playOnAwake = false;

        // Trigger initial state
        ApplyLipsyncToggle();
    }

    void Update()
    {
        ApplyLipsyncToggle();

        displayedAmplitude = Mathf.Lerp(
            displayedAmplitude,
            lipsyncEnabled ? currentNormalizedAmplitude : 0f,
            Time.deltaTime * amplitudeLerpSpeed
        );

        float curved = Mathf.Pow(Mathf.Clamp01(displayedAmplitude), responseCurveExponent);
        UpdateMouthSprite(curved);

        if (eyeAnimator != null)
            eyeAnimator.UpdateEyeByAmplitude(currentNormalizedAmplitude);
    }

    private void ApplyLipsyncToggle()
    {
        // Determine mode based on configuration
        DetermineMode();

        // If enabled and not running → start everything
        if (lipsyncEnabled)
        {
            if (currentMode == LipsyncMode.Static)
            {
                // Static mode: control AudioSource playback and use pre-processed clip
                if (!audioSource.isPlaying)
                    audioSource.Play();

                if (lipsyncRoutine == null && audioClip != null)
                    lipsyncRoutine = StartCoroutine(AnimateMouthFromAudio(audioClip));
            }
            else if (currentMode == LipsyncMode.RealTime)
            {
                // Real-time mode: monitor external AudioSource, don't control playback
                if (lipsyncRoutine == null)
                    lipsyncRoutine = StartCoroutine(AnimateMouthFromAudioSource());
            }
        }
        else
        {
            // Disable lipsync
            if (currentMode == LipsyncMode.Static && audioSource.isPlaying)
            {
                // Only stop audio in static mode (we control it)
                audioSource.Stop();
            }

            if (lipsyncRoutine != null)
            {
                StopCoroutine(lipsyncRoutine);
                lipsyncRoutine = null;
            }

            currentNormalizedAmplitude = 0f;
            UpdateMouthSprite(0f); // closed mouth
        }
    }

    private void DetermineMode()
    {
        // Static mode: AudioClip is assigned (original behavior)
        // Real-time mode: No AudioClip, but AudioSource exists (monitor external audio)
        if (audioClip != null)
        {
            currentMode = LipsyncMode.Static;
        }
        else if (audioSource != null)
        {
            currentMode = LipsyncMode.RealTime;
        }
        else
        {
            currentMode = LipsyncMode.Static; // fallback
        }
    }

    IEnumerator AnimateMouthFromAudio(AudioClip clip)
    {
        float[] samples = new float[clip.samples];
        clip.GetData(samples, 0);

        int sampleRate = clip.frequency;
        int stepSize = Mathf.FloorToInt(sampleRate * updateInterval);

        amplitudeHistory.Clear();
        List<float> averages = new List<float>();

        // Precompute slices
        int pos = 0;
        while (pos < samples.Length)
        {
            float sum = 0f;
            int count = 0;

            for (int i = 0; i < stepSize && pos + i < samples.Length; i++)
            {
                sum += Mathf.Abs(samples[pos + i]);
                count++;
            }

            averages.Add(sum / Mathf.Max(count, 1));
            pos += stepSize;
        }

        // Get normalization peak
        float peak = 0.01f;
        foreach (float a in averages)
            peak = Mathf.Max(peak, a);

        // Emit amplitude values
        for (int i = 0; i < averages.Count; i++)
        {
            if (!lipsyncEnabled) yield break;

            amplitudeHistory.Enqueue(averages[i]);
            if (amplitudeHistory.Count > smoothingWindow)
                amplitudeHistory.Dequeue();

            float smoothed = 0f;
            foreach (var a in amplitudeHistory)
                smoothed += a;
            smoothed /= amplitudeHistory.Count;

            currentNormalizedAmplitude = smoothed / peak;

            yield return new WaitForSeconds(updateInterval);
        }

        // When clip ends
        currentNormalizedAmplitude = 0f;
        lipsyncRoutine = null;
    }

    /// <summary>
    /// Real-time mode: Continuously monitors the AudioSource and calculates amplitude on-the-fly.
    /// Works with streaming audio (e.g., PcmAudioPlayer swapping clips dynamically).
    /// </summary>
    IEnumerator AnimateMouthFromAudioSource()
    {
        float[] outputSamples = new float[realTimeSampleCount];
        amplitudeHistory.Clear();
        float peak = 0.01f; // minimum peak to avoid division by zero
        float peakDecay = 0.995f; // slowly decay peak for auto-normalization

        while (lipsyncEnabled)
        {
            // Check if AudioSource is currently playing
            if (audioSource != null && audioSource.isPlaying)
            {
                // Get current output data from AudioSource
                audioSource.GetOutputData(outputSamples, 0);

                // Calculate average amplitude from samples
                float sum = 0f;
                for (int i = 0; i < outputSamples.Length; i++)
                {
                    sum += Mathf.Abs(outputSamples[i]);
                }
                float avgAmplitude = sum / outputSamples.Length;

                // Update peak with decay (adaptive normalization)
                peak = Mathf.Max(peak * peakDecay, avgAmplitude);
                if (peak < 0.01f) peak = 0.01f;

                // Add to smoothing history
                amplitudeHistory.Enqueue(avgAmplitude);
                if (amplitudeHistory.Count > smoothingWindow)
                    amplitudeHistory.Dequeue();

                // Calculate smoothed amplitude
                float smoothed = 0f;
                foreach (var a in amplitudeHistory)
                    smoothed += a;
                smoothed /= amplitudeHistory.Count;

                // Normalize
                currentNormalizedAmplitude = Mathf.Clamp01(smoothed / peak);
            }
            else
            {
                // AudioSource not playing, gradually close mouth
                currentNormalizedAmplitude = Mathf.Lerp(currentNormalizedAmplitude, 0f, Time.deltaTime * amplitudeLerpSpeed);
                amplitudeHistory.Clear();
            }

            yield return new WaitForSeconds(updateInterval);
        }

        // Clean up when disabled
        currentNormalizedAmplitude = 0f;
        lipsyncRoutine = null;
    }

    void UpdateMouthSprite(float amp)
    {
        int index = Mathf.Clamp(
            Mathf.FloorToInt(amp * (mouthSprites.Length - 1)),
            0,
            mouthSprites.Length - 1
        );

        mouthRenderer.sprite = mouthSprites[index];
    }
}
