using System.Collections;
using UnityEngine;
using TMPro;  

public class CompanionOrb : MonoBehaviour
{
    [Header("References")]
    public OpenAIClient openAIClient;

    [Tooltip("Optional: a TextMeshPro above the orb to show last message.")]
    public TextMeshPro worldText;

    [Tooltip("Renderer of the orb to tint while thinking.")]
    public Renderer orbRenderer;

    [Header("Vision / Object Detection")]
    public Camera captureCamera;
    public RenderTexture captureRT;

    [Header("Input (for testing in Editor)")]
    [TextArea(1, 4)]
    public string testTextMessage = "Hi, how are you?";

    private Color _idleColor = Color.cyan;
    private Color _thinkingColor = Color.magenta;
    private bool _busy = false;

    private void Start()
    {
        if (orbRenderer != null)
        {
            _idleColor = orbRenderer.material.color;
        }

        SayLocally("Hi! I'm your AR companion sphere.");
    }

    private void Update()
    {
        
        // T = send test text message to OpenAI
        // O = capture camera and ask for scene / object description
        if (Input.GetKeyDown(KeyCode.T) && !_busy)
        {
            StartCoroutine(SendTextToOpenAI(testTextMessage));
        }

        if (Input.GetKeyDown(KeyCode.O) && !_busy)
        {
            StartCoroutine(AnalyzeCurrentView());
        }
    }

    // TEXT CHAT 

    public IEnumerator SendTextToOpenAI(string message)
    {
        _busy = true;
        SetThinking(true);
        SayLocally("You: " + message);

        yield return openAIClient.SendTextChat(
            message,
            onSuccess: resp =>
            {
                SayLocally("Companion: " + resp);
                
            },
            onError: error =>
            {
                SayLocally("Error: " + error);
            });

        SetThinking(false);
        _busy = false;
    }

    // VISION / OBJECT DETECTION

    public IEnumerator AnalyzeCurrentView()
    {
        if (captureCamera == null || captureRT == null)
        {
            SayLocally("No capture camera / RenderTexture set.");
            yield break;
        }

        _busy = true;
        SetThinking(true);
        SayLocally("Companion: Let me check what I see...");

        // Grab one frame from RenderTexture
        Texture2D tex = new Texture2D(captureRT.width, captureRT.height, TextureFormat.RGB24, false);

        RenderTexture currentActive = RenderTexture.active;
        RenderTexture.active = captureRT;

        captureCamera.Render(); // ensure latest view
        tex.ReadPixels(new Rect(0, 0, captureRT.width, captureRT.height), 0, 0);
        tex.Apply();

        RenderTexture.active = currentActive;

        string prompt =
            "Describe what you see in this image in 1-3 short sentences. " +
            "Mention important objects and any obvious potential hazards for a person walking here. " +
            "Be clear that you are only describing what you see.";

        yield return openAIClient.AnalyzeImage(
            tex,
            prompt,
            onSuccess: resp =>
            {
                SayLocally("Companion (vision): " + resp);
            },
            onError: error =>
            {
                SayLocally("Vision error: " + error);
            });

        Destroy(tex);

        SetThinking(false);
        _busy = false;
    }


    private void SayLocally(string text)
    {
        Debug.Log("[CompanionOrb] " + text);
        if (worldText != null)
        {
            worldText.text = text;
        }

    }

    private void SetThinking(bool thinking)
    {
        if (orbRenderer == null) return;
        orbRenderer.material.color = thinking ? _thinkingColor : _idleColor;
    }
}
