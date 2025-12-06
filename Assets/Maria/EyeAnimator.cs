using System.Collections;
using UnityEngine;

public class EyeAnimator : MonoBehaviour
{
    public SpriteRenderer eyeRenderer;
    public Sprite[] openEyeSprites;   // Assume ordered from calm → energetic
    public Sprite closedEyeSprite;
    public Sprite heartEyeSprite; // Optional, for special expressions

    public float minBlinkInterval = 3f;
    public float maxBlinkInterval = 7f;
    public float blinkDuration = 0.1f;

    private int currentOpenIndex = 0;
    private float lastAmplitude = 0f;

    void Start()
    {
        if (openEyeSprites.Length > 0)
            eyeRenderer.sprite = openEyeSprites[0];

        StartCoroutine(BlinkRoutine());
    }

    IEnumerator BlinkRoutine()
    {
        while (true)
        {
            float waitTime = Random.Range(minBlinkInterval, maxBlinkInterval);
            yield return new WaitForSeconds(waitTime);

            // Blink
            eyeRenderer.sprite = closedEyeSprite;
            yield return new WaitForSeconds(blinkDuration);

            // Return to current open expression
            eyeRenderer.sprite = openEyeSprites[currentOpenIndex];
        }
    }

    public void UpdateEyeByAmplitude(float amplitude)
    {
        lastAmplitude = amplitude;

        // Manually tweak these to match your observed amplitude range
        float minAmp = 0.05f;
        float maxAmp = 0.15f;

        // Normalize amplitude between 0–1
        float normalized = Mathf.InverseLerp(minAmp, maxAmp, Mathf.Clamp(amplitude, minAmp, maxAmp));

        // Scale to sprite index
        int spriteIndex = Mathf.FloorToInt(normalized * (openEyeSprites.Length - 1));

        spriteIndex = Mathf.Clamp(spriteIndex, 0, openEyeSprites.Length - 1);

        if (spriteIndex != currentOpenIndex)
        {
            Debug.Log($"[EyeAnimator] Amplitude: {amplitude:F3}, Normalized: {normalized:F2}, Index: {spriteIndex}");
            currentOpenIndex = spriteIndex;
            eyeRenderer.sprite = openEyeSprites[spriteIndex];
        }
    }
    
    public void SetEyeSprite(int spriteIndex)
    {
        if (spriteIndex >= 0 && spriteIndex < openEyeSprites.Length)
        {
            currentOpenIndex = spriteIndex;
            eyeRenderer.sprite = openEyeSprites[spriteIndex];
        }
        else
        {
            Debug.LogWarning($"[EyeAnimator] Invalid sprite index: {spriteIndex}");
        }
    }
    
    public void SetHeartEyeSprite()
    {
        if (heartEyeSprite != null)
        {
            eyeRenderer.sprite = heartEyeSprite;
        }
        else
        {
            Debug.LogWarning("[EyeAnimator] Heart eye sprite is not assigned.");
        }
    }
}
