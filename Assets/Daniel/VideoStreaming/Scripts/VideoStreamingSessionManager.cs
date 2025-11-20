using System;
using UnityEngine;

/// <summary>
/// High–level coordinator for starting an Agora video session from Unity
/// and notifying emergency contacts with a session URL.
///
/// Flow:
/// - Generate a short session id (used as the Agora channel name).
/// - Build a URL to the web client (hosted on Vercel) with ?sessionId=...
/// - Ask <see cref="EmergencyContactController"/> to SMS that URL.
/// - Tell <see cref="AgoraUnityVideoController"/> to join / publish to that channel.
/// </summary>
public class VideoStreamingSessionManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Controller that will actually connect to Agora from Unity.")]
    [SerializeField] private AgoraUnityVideoController agoraController;

    [Tooltip("Emergency contact controller used to send the SMS with the video URL.")]
    [SerializeField] private EmergencyContactController emergencyContactController;

    [Tooltip("Optional reference used for embedding a world position into the SMS payload.")]
    [SerializeField] private Transform worldPositionReference;

    [Header("Web App Routing")]
    [Tooltip("Base URL of the web client (deployed on Vercel).")]
    [SerializeField] private string webAppBaseUrl = "https://safe-walk-frontend.vercel.app/";

    [Tooltip("Query parameter name used for the session id on the web client.")]
    [SerializeField] private string sessionQueryParamName = "sessionId";

    [Header("Behaviour")]
    [Tooltip("If true, a session will be created automatically in Awake().")]
    [SerializeField] private bool autoStartOnAwake;

    /// <summary>Last generated session id (also used as the Agora channel name).</summary>
    public string CurrentSessionId { get; private set; }

    private void Awake()
    {
        if (worldPositionReference == null)
        {
            worldPositionReference = transform;
        }

        if (autoStartOnAwake)
        {
            StartVideoStreamingSession();
        }
    }

    /// <summary>
    /// Entry point you can wire to a button, gesture or other event.
    /// Creates a fresh session id, initializes Agora on Unity,
    /// and sends an SMS with a link to the web client.
    /// </summary>
    public void StartVideoStreamingSession()
    {
        // 1. Generate a new session id and remember it.
        CurrentSessionId = GenerateSessionId();

        // 2. Build the public URL for the web client.
        string url = BuildSessionUrl(CurrentSessionId);

        Debug.Log($"[VideoStreamingSessionManager] Starting session '{CurrentSessionId}' with URL: {url}");

        // 3. Ask Agora controller to connect to this channel.
        if (agoraController != null)
        {
            agoraController.StartOrJoinSession(CurrentSessionId);
        }
        else
        {
            Debug.LogWarning("[VideoStreamingSessionManager] No AgoraUnityVideoController assigned, Unity side will not join the video channel.");
        }

        // 4. Notify emergency contacts via SMS.
        if (emergencyContactController != null)
        {
            // Force reload contacts in case they weren't loaded properly in Awake
            emergencyContactController.LoadContacts();
            
            // Reset the idempotency flag so we can send SMS even if it was used before
            emergencyContactController.ResetEmergencyContact();
            
            Vector3? position = worldPositionReference != null ? worldPositionReference.position : (Vector3?)null;

            // This uses the overload that already knows how to embed a videoUrl and position
            // into the JSON payload it sends via SMS.
            emergencyContactController.StartEmergencyContact(
                videoUrl: url,
                imageBase64: null,
                position: position,
                overrideMessage: null);
        }
        else
        {
            Debug.LogWarning("[VideoStreamingSessionManager] No EmergencyContactController assigned, SMS notification will be skipped.");
        }
    }

    /// <summary>
    /// Builds the public URL for the web client based on the configured base URL
    /// and query parameter name, e.g. https://your-app.vercel.app/?sessionId=abcd1234
    /// </summary>
    private string BuildSessionUrl(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(webAppBaseUrl))
        {
            Debug.LogWarning("[VideoStreamingSessionManager] webAppBaseUrl is not configured. Returning bare session id.");
            return sessionId;
        }

        // Ensure trailing slash is optional and not duplicated.
        string trimmedBase = webAppBaseUrl.TrimEnd('/');

        return $"{trimmedBase}/?{sessionQueryParamName}={Uri.EscapeDataString(sessionId)}";
    }

    /// <summary>
    /// Generates a short, URL‑safe session id suitable for use as an Agora channel name.
    /// </summary>
    private string GenerateSessionId()
    {
        // 8 chars from a GUID is usually enough entropy for a human‑typed session.
        string guid = Guid.NewGuid().ToString("N");
        return guid.Substring(0, 8);
    }
}


