using UnityEngine;

public class SMSController : MonoBehaviour
{
	[Header("Test SMS Settings")]
	[Tooltip("Destination phone number in E.164 format, e.g. +15551234567")]
	public string toNumber;

	[Tooltip("Message body to send")]
	[TextArea(2, 4)]
	public string message;

	// Sends on Start if toNumber and message are provided (non-empty)
	void Start()
	{
		if (!string.IsNullOrWhiteSpace(toNumber) && !string.IsNullOrWhiteSpace(message))
		{
			SendMessage();
		}
	}

	// Public method to trigger sending from UI or other scripts
	public void SendMessage()
	{
		TwilioClient.Instance.SendMessage(
			toNumber,
			message, 
			(success, response) =>
			{
				if (success)
				{
					Debug.Log("Twilio SMS sent successfully.");
				}
				else
				{
					Debug.LogWarning($"Twilio SMS failed: {response}");
				}
			}
		);
	}
}

