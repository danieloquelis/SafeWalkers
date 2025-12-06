using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MouthAnimator : MonoBehaviour
{
    [Header("Audio Input")]
    public AudioClip audioClip;
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

    private Queue<float> amplitudeHistory = new Queue<float>();
    private float currentNormalizedAmplitude = 0f;
    private float displayedAmplitude = 0f;
    private Coroutine lipsyncRoutine;

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
        // If enabled and not running → start everything
        if (lipsyncEnabled)
        {
            if (!audioSource.isPlaying)
                audioSource.Play();

            if (lipsyncRoutine == null && audioClip != null)
                lipsyncRoutine = StartCoroutine(AnimateMouthFromAudio(audioClip));
        }
        else
        {
            // Disable lipsync and stop audio
            if (audioSource.isPlaying)
                audioSource.Stop();

            if (lipsyncRoutine != null)
            {
                StopCoroutine(lipsyncRoutine);
                lipsyncRoutine = null;
            }

            currentNormalizedAmplitude = 0f;
            UpdateMouthSprite(0f); // closed mouth
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
