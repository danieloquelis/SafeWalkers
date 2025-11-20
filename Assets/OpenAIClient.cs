using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class OpenAIClient : MonoBehaviour
{
    [Header("OpenAI Settings")]
    [SerializeField] private string apiKey = "OPENAI_API_KEY"; 
    [SerializeField] private string model = "gpt-4.1-mini";

    [TextArea(2, 6)]
    [SerializeField]
    private string baseSystemPrompt =
        "You are a friendly in-game companion. " +
        "You answer briefly and conversationally.";

    private const string apiUrl = "https://api.openai.com/v1/chat/completions";

    // PUBLIC API

    // 1) REQUEST
    public IEnumerator SendTextChat(string userMessage,
                                    Action<string> onSuccess,
                                    Action<string> onError = null)
    {
        if (!CheckApiKey(onError)) yield break;

        string escapedUser = EscapeForJson(userMessage);
        string escapedSystem = EscapeForJson(baseSystemPrompt);

        string jsonBody = $@"{{
  ""model"": ""{model}"",
  ""messages"": [
    {{""role"": ""system"", ""content"": ""{escapedSystem}""}},
    {{""role"": ""user"", ""content"": ""{escapedUser}""}}
  ],
  ""max_tokens"": 200,
  ""temperature"": 0.7
}}";

        yield return SendRequest(jsonBody, onSuccess, onError);
    }

    // 2) VISION REQUEST: IMAGE + PROMPT (object / scene analysis)
    public IEnumerator AnalyzeImage(Texture2D image,
                                    string userPrompt,
                                    Action<string> onSuccess,
                                    Action<string> onError = null)
    {
        if (!CheckApiKey(onError)) yield break;

        if (image == null)
        {
            onError?.Invoke("Image is null.");
            yield break;
        }

        byte[] pngData = image.EncodeToPNG();
        string base64 = Convert.ToBase64String(pngData);

        string escapedPrompt = EscapeForJson(userPrompt);
        string escapedSystem = EscapeForJson(
            "You are a visual assistant inside an AR headset. " +
            "You describe what you see in the image and mention possible hazards, " +
            "but you are not a safety system."
        );

        // Vision format: text + image in content array
        string jsonBody = $@"{{
  ""model"": ""{model}"",
  ""messages"": [
    {{
      ""role"": ""system"",
      ""content"": ""{escapedSystem}""
    }},
    {{
      ""role"": ""user"",
      ""content"": [
        {{""type"": ""input_text"", ""text"": ""{escapedPrompt}""}},
        {{
          ""type"": ""input_image"",
          ""image_url"": {{
            ""url"": ""data:image/png;base64,{base64}""
          }}
        }}
      ]
    }}
  ],
  ""max_tokens"": 200,
  ""temperature"": 0.3
}}";

        yield return SendRequest(jsonBody, onSuccess, onError);
    }

    // INTERNAL HELPERS

    private bool CheckApiKey(Action<string> onError)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "YOUR_OPENAI_API_KEY")
        {
            string msg = "OpenAI API key is missing. Set it in OpenAIClient.";
            Debug.LogError(msg);
            onError?.Invoke(msg);
            return false;
        }
        return true;
    }

    private IEnumerator SendRequest(string jsonBody,
                                    Action<string> onSuccess,
                                    Action<string> onError)
    {
        using (var request = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError)
            {
                string errorText = $"HTTP Error: {request.result} - {request.error}\n{request.downloadHandler.text}";
                Debug.LogError(errorText);
                onError?.Invoke(errorText);
            }
            else
            {
                string responseText = request.downloadHandler.text;
                // Debug.Log("OpenAI raw response: " + responseText);
                string assistant = ParseAssistantMessage(responseText);
                if (string.IsNullOrEmpty(assistant))
                    assistant = "[No response or parsing error]";
                onSuccess?.Invoke(assistant);
            }
        }
    }

    // Parsing: grabs first content
    private string ParseAssistantMessage(string json)
    {
        try
        {
            const string needle = "\"content\":\"";
            int idx = json.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0) return null;
            idx += needle.Length;
            int end = json.IndexOf("\"", idx, StringComparison.Ordinal);
            if (end < 0) return null;
            string content = json.Substring(idx, end - idx);
            content = content.Replace("\\n", "\n").Replace("\\\"", "\"");
            return content;
        }
        catch (Exception e)
        {
            Debug.LogError("Parse error: " + e);
            return null;
        }
    }

    private string EscapeForJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "");
    }
}
