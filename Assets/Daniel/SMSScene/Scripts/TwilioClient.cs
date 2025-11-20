using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public sealed class TwilioClient
{
    private static TwilioClient _instance;
    public static TwilioClient Instance => _instance ??= new TwilioClient();

	private string _accountSid;
	private const string FromPhoneNumber = "+12394755462";
    private string _authToken;

    private readonly MonoBehaviour _coroutineRunner;

    private TwilioClient()
    {
        // Ensure there is a hidden runner to execute coroutines
        var runnerObject = new GameObject("TwilioCoroutineRunner");
        runnerObject.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(runnerObject);
        _coroutineRunner = runnerObject.AddComponent<CoroutineRunner>();

		LoadCredentials();
    }

	private void LoadCredentials()
    {
        // Loads from Assets/Resources/twilio_token.txt (do not include extension)
        var tokenAsset = Resources.Load<TextAsset>("twilio_token");
        if (tokenAsset == null || string.IsNullOrWhiteSpace(tokenAsset.text))
        {
            Debug.LogError("Twilio token not found. Create Assets/Resources/twilio_token.txt and add your token.");
            _authToken = null;
        }
		else
		{
			_authToken = tokenAsset.text.Trim();
		}

		// Loads from Assets/Resources/twilio_sid.txt (do not include extension)
		var sidAsset = Resources.Load<TextAsset>("twilio_sid");
		if (sidAsset == null || string.IsNullOrWhiteSpace(sidAsset.text))
		{
			Debug.LogError("Twilio SID not found. Create Assets/Resources/twilio_sid.txt and add your Account SID.");
			_accountSid = null;
		}
		else
		{
			_accountSid = sidAsset.text.Trim();
		}
    }

	public void SendMessage(
		string toPhoneNumber,
		string messageBody,
		Action<bool, string> onComplete = null)
    {
        if (string.IsNullOrWhiteSpace(toPhoneNumber) || string.IsNullOrWhiteSpace(messageBody))
        {
            onComplete?.Invoke(false, "Missing required 'to' number or message body.");
            return;
        }

		if (string.IsNullOrEmpty(_authToken) || string.IsNullOrEmpty(_accountSid))
        {
			LoadCredentials();
			if (string.IsNullOrEmpty(_authToken) || string.IsNullOrEmpty(_accountSid))
            {
				onComplete?.Invoke(false, "Twilio credentials not loaded (SID or token missing).");
                return;
            }
        }

		_coroutineRunner.StartCoroutine(SendMessageCoroutine(toPhoneNumber, messageBody, onComplete));
    }

    private IEnumerator SendMessageCoroutine(
        string toPhoneNumber,
		string messageBody,
		Action<bool, string> onComplete)
    {
		var url = $"https://api.twilio.com/2010-04-01/Accounts/{_accountSid}/Messages.json";

        var form = new WWWForm();
        form.AddField("To", toPhoneNumber);
        form.AddField("Body", messageBody);
		form.AddField("From", FromPhoneNumber);

        using (var request = UnityWebRequest.Post(url, form))
        {
			var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_accountSid}:{_authToken}"));
            request.SetRequestHeader("Authorization", $"Basic {credentials}");

            yield return request.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool success = request.result == UnityWebRequest.Result.Success;
#else
            bool success = !request.isNetworkError && !request.isHttpError;
#endif
            var responseText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            if (!success)
            {
                var error = string.IsNullOrEmpty(request.error) ? "Unknown error" : request.error;
                onComplete?.Invoke(false, $"HTTP Error: {error}\n{responseText}");
            }
            else
            {
                onComplete?.Invoke(true, responseText);
            }
        }
    }

    private class CoroutineRunner : MonoBehaviour { }
}

