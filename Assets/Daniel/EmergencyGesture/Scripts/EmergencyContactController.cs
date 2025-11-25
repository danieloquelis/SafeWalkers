using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Newtonsoft.Json;

public class EmergencyContactController : MonoBehaviour
{
	[Header("Emergency Contacts")]
	[Tooltip("Key used in PlayerPrefs to store the full contacts list (name:number;name:number;...).")]
	[SerializeField] private string contactsPrefsKey = "EmergencyContacts";

	[Tooltip("PlayerPrefs key used to store the main selected contact phone number.")]
	[SerializeField] private string mainContactPrefsKey = "MainEmergencyContactNumber";

	[Tooltip("PlayerPrefs key used to store the secondary selected contact phone number.")]
	[SerializeField] private string secondaryContactPrefsKey = "SecondaryEmergencyContactNumber";

	[Tooltip("Fallback emergency contacts if none are stored in PlayerPrefs yet.")]
	[SerializeField] private List<string> defaultEmergencyContacts = new List<string>();

	[Header("Messaging")]
	[Tooltip("Base message sent to all emergency contacts.")]
	[TextArea(2, 4)]
	[SerializeField]
	private string baseMessage =
		"Your contact is in danger, please follow their steps and track on video call";

	[Tooltip("Prefab reference to the existing SMSController.")]
	[SerializeField] private SMSController smsControllerPrefab;

	[Header("Events")]
	[Tooltip("Invoked after the emergency contacts have been notified (SMS send triggered).")]
	public UnityEvent OnContacted;

	private readonly List<string> _activeContacts = new List<string>();
	private bool _hasStarted;

	private void Awake()
	{
		LoadContacts();
	}

	/// <summary>
	/// Parameterless entry point for Unity Events / gesture bindings.
	/// Uses only the configured base message and contacts.
	/// </summary>
	public void StartEmergencyContact()
	{
		StartEmergencyContact(null, null, null, null);
	}

	/// <summary>
	/// Starts the emergency contact flow.
	/// Idempotent: subsequent calls are ignored until ResetEmergencyContact is called.
	/// </summary>
	/// <param name="videoUrl">Optional URL to a live video or call.</param>
	/// <param name="imageBase64">Optional base64-encoded image string.</param>
	/// <param name="position">Optional world-space position associated with the emergency.</param>
	/// <param name="overrideMessage">
	/// Optional message overriding the configured base message.
	/// If null or empty, the serialized baseMessage is used.
	/// </param>
	public void StartEmergencyContact(
		string videoUrl = null,
		string imageBase64 = null,
		Vector3? position = null,
		string overrideMessage = null)
	{
		if (_hasStarted)
		{
			Debug.Log("[EmergencyContactController] StartEmergencyContact called but already started, ignoring.");
			return;
		}

		if (smsControllerPrefab == null)
		{
			Debug.LogError("[EmergencyContactController] smsControllerPrefab is not assigned.");
			return;
		}

		if (_activeContacts.Count == 0)
		{
			Debug.LogWarning("[EmergencyContactController] No emergency contacts configured.");
			return;
		}

		_hasStarted = true;

		string finalMessage = ComposeMessage(overrideMessage, videoUrl, imageBase64, position);

		foreach (string number in _activeContacts)
		{
			if (string.IsNullOrWhiteSpace(number))
				continue;

			SMSController instance = Instantiate(smsControllerPrefab, transform);
			instance.toNumber = number;
			instance.message = finalMessage;
			instance.SendMessage();

			// Clean up after a short delay once the SMS has had time to send.
			Destroy(instance.gameObject, 10f);
		}

		OnContacted?.Invoke();
	}

	/// <summary>
	/// Resets the idempotency flag so the emergency flow can be triggered again.
	/// </summary>
	public void ResetEmergencyContact()
	{
		_hasStarted = false;
	}

	/// <summary>
	/// Loads the active contact list from PlayerPrefs.
	/// Preference order:
	/// 1. Use explicitly selected Main/Secondary contact numbers (if any).
	/// 2. Otherwise, fall back to the full EmergencyContacts list.
	/// 3. Finally, fall back to serialized defaultEmergencyContacts.
	/// </summary>
	public void LoadContacts()
	{
		_activeContacts.Clear();

		// 1) Try to use the specifically selected main / secondary contacts.
		bool addedFromSelection = false;

		string mainNumber = GetNonEmptyString(mainContactPrefsKey);
		if (!string.IsNullOrEmpty(mainNumber))
		{
			_activeContacts.Add(mainNumber);
			addedFromSelection = true;
		}

		string secondaryNumber = GetNonEmptyString(secondaryContactPrefsKey);
		if (!string.IsNullOrEmpty(secondaryNumber) && secondaryNumber != mainNumber)
		{
			_activeContacts.Add(secondaryNumber);
			addedFromSelection = true;
		}

		// 2) If we still have nothing, fall back to the full EmergencyContacts list.
		if (!addedFromSelection && PlayerPrefs.HasKey(contactsPrefsKey))
		{
			string stored = PlayerPrefs.GetString(contactsPrefsKey);
			Debug.Log($"[EmergencyContactController] Found PlayerPrefs key '{contactsPrefsKey}' with value: '{stored}'");
			if (!string.IsNullOrEmpty(stored))
			{
				string[] split = stored.Split(';');
				foreach (string raw in split)
				{
					string trimmed = raw.Trim();
					if (string.IsNullOrEmpty(trimmed))
						continue;

					// Entries can be "number" or "name:number".
					string number = trimmed;
					string[] parts = trimmed.Split(':');
					if (parts.Length >= 2)
					{
						number = parts[1].Trim();
					}

					if (string.IsNullOrWhiteSpace(number))
						continue;

					if (!_activeContacts.Contains(number))
					{
						_activeContacts.Add(number);
					}
				}
			}
		}
		else if (!addedFromSelection)
		{
			Debug.Log($"[EmergencyContactController] No PlayerPrefs key '{contactsPrefsKey}' found, using defaultEmergencyContacts.");
		}

		// 3) Fallback to serialized defaults if nothing was loaded
		if (_activeContacts.Count == 0 && defaultEmergencyContacts != null)
		{
			Debug.Log($"[EmergencyContactController] Loading {defaultEmergencyContacts.Count} contacts from defaultEmergencyContacts.");
			_activeContacts.AddRange(defaultEmergencyContacts);
		}
		
		Debug.Log($"[EmergencyContactController] Total active contacts after load: {_activeContacts.Count}");
	}

	private static string GetNonEmptyString(string key)
	{
		if (string.IsNullOrEmpty(key) || !PlayerPrefs.HasKey(key))
		{
			return null;
		}

		string value = PlayerPrefs.GetString(key);
		return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}

	/// <summary>
	/// Composes the final message as a human-readable text that will be sent to each contact,
	/// based on the base/override message and any optional contextual data.
	/// </summary>
	private string ComposeMessage(
		string overrideMessage,
		string videoUrl,
		string imageBase64,
		Vector3? position)
	{
		string messageBody = !string.IsNullOrWhiteSpace(overrideMessage)
			? overrideMessage
			: baseMessage;

		// Build a simple human-readable message
		string finalMessage = messageBody;

		if (!string.IsNullOrWhiteSpace(videoUrl))
		{
			finalMessage += $"\n\nJoin video call: {videoUrl}";
		}

		return finalMessage;
	}

	private class EmergencyMessagePayload
	{
		public string message;
		public string videoUrl;
		public string imageBase64;
		public Vector3? position;
	}
}


