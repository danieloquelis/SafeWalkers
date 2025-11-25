using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class SplashManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text statusText;

    [Header("Timing")]
    [SerializeField] private float splashDurationSeconds = 5f;
    [SerializeField] private float wordChangeIntervalSeconds = 0.5f;

    [Header("Scene")]
    [SerializeField] private string nextSceneName;

    [Header("Messages")]
    [SerializeField] private string[] loadingMessages =
    {
        "Initializing systems...",
        "Loading assets...",
        "Configuring audio...",
        "Setting up environment...",
        "Warming up AI...",
        "Almost ready..."
    };

    private void Start()
    {
        // Start the splash sequence when the scene loads
        StartCoroutine(SplashRoutine());
    }

    private IEnumerator SplashRoutine()
    {
        if (statusText == null)
        {
            Debug.LogWarning("SplashManager: No TMP_Text assigned for statusText.");
        }

        float elapsed = 0f;

        // Ensure we have at least one message
        if (loadingMessages == null || loadingMessages.Length == 0)
        {
            loadingMessages = new[] { "Starting up..." };
        }

        // Main splash loop
        while (elapsed < splashDurationSeconds)
        {
            if (statusText != null && loadingMessages.Length > 0)
            {
                int index = Random.Range(0, loadingMessages.Length);
                statusText.text = loadingMessages[index];
            }

            float wait = Mathf.Max(0.01f, wordChangeIntervalSeconds);
            elapsed += wait;
            yield return new WaitForSeconds(wait);
        }

        // Load the next scene when done
        if (!string.IsNullOrEmpty(nextSceneName))
        {
            SceneManager.LoadScene(nextSceneName);
        }
        else
        {
            Debug.LogWarning("SplashManager: nextSceneName is empty, staying on splash scene.");
        }
    }
}

