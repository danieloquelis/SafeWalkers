using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class PairingIdEvent : UnityEvent<string> { }

/// <summary>
/// Tracks the currently paired mobile device, persists the pairing id across sessions,
/// connects the Pusher service to the appropriate channel and exposes helper methods
/// for sending Safe Mode lifecycle events.
/// </summary>
public class SafeWalkPairingController : MonoBehaviour
{
	public const string DefaultPairingPrefsKey = "SafeWalk.PairingId";

	[SerializeField] private SafeWalkPusherService pusherService;
	[SerializeField] private string playerPrefsKey = DefaultPairingPrefsKey;

	[Header("Events")]
	public PairingIdEvent OnPairingIdChanged;
	public SafeWalkLocationEvent OnLocationUpdated;

	public string CurrentPairingId => _currentPairingId;
	public string PlayerPrefsKey => playerPrefsKey;

	private string _currentPairingId;

	private void Awake()
	{
		if (pusherService == null)
		{
			pusherService = GetComponent<SafeWalkPusherService>();
		}

		if (OnPairingIdChanged == null)
		{
			OnPairingIdChanged = new PairingIdEvent();
		}

		if (OnLocationUpdated == null)
		{
			OnLocationUpdated = new SafeWalkLocationEvent();
		}

		if (pusherService != null)
		{
			pusherService.OnLocationUpdated.AddListener(ForwardLocationUpdate);
		}

		LoadPairingIdFromPrefs(connectToPusher: true);
	}

	private void OnDestroy()
	{
		if (pusherService != null)
		{
			pusherService.OnLocationUpdated.RemoveListener(ForwardLocationUpdate);
		}
	}

	public void HandleQrPayload(string payload)
	{
		if (string.IsNullOrEmpty(payload))
			return;

		try
		{
			var parsed = JsonConvert.DeserializeObject<PairingPayload>(payload);
			if (parsed == null || string.IsNullOrWhiteSpace(parsed.pairingId))
			{
				Debug.LogWarning($"[SafeWalkPairingController] QR payload did not contain a pairingId. Raw={payload}");
				return;
			}

			SetPairingIdInternal(parsed.pairingId.Trim(), true);
			_ = pusherService?.ConnectPairingChannelAsync(parsed.pairingId.Trim());
		}
		catch (Exception ex)
		{
			Debug.LogError($"[SafeWalkPairingController] Failed to parse QR payload: {ex}");
		}
	}

	public async void NotifySafeModeEnabled(string sessionId, string videoUrl)
	{
		if (string.IsNullOrWhiteSpace(_currentPairingId))
		{
			Debug.LogWarning("[SafeWalkPairingController] Cannot notify Safe Mode without a pairing id.");
			return;
		}

		if (string.IsNullOrWhiteSpace(sessionId))
		{
			Debug.LogWarning("[SafeWalkPairingController] Session id is required to enable Safe Mode.");
			return;
		}

		try
		{
			await pusherService.TriggerSafeModeEnabledAsync(_currentPairingId, sessionId, videoUrl);
		}
		catch (Exception ex)
		{
			Debug.LogError($"[SafeWalkPairingController] Failed to trigger safe_mode_enabled: {ex}");
		}
	}

	public async void NotifySafeModeDisabled(string sessionId)
	{
		if (string.IsNullOrWhiteSpace(_currentPairingId))
			return;

		try
		{
			await pusherService.TriggerSafeModeDisabledAsync(_currentPairingId, sessionId);
		}
		catch (Exception ex)
		{
			Debug.LogError($"[SafeWalkPairingController] Failed to trigger safe_mode_disabled: {ex}");
		}
	}

	private void SetPairingIdInternal(string pairingId, bool persist)
	{
		if (pairingId == _currentPairingId)
			return;

		_currentPairingId = pairingId;
		OnPairingIdChanged?.Invoke(_currentPairingId);

		if (persist)
		{
			PlayerPrefs.SetString(playerPrefsKey, _currentPairingId);
			PlayerPrefs.Save();
		}
	}

	private void ForwardLocationUpdate(SafeWalkLocationPayload payload)
	{
		if (payload == null)
			return;

		if (!string.IsNullOrEmpty(_currentPairingId) && payload.pairingId != _currentPairingId)
		{
			// Ignore updates for other pairings.
			return;
		}

		OnLocationUpdated?.Invoke(payload);
	}

	[Serializable]
	private class PairingPayload
	{
		public string type;
		public string device;
		public string pairingId;
	}

	public void RefreshPairingFromPrefs()
	{
		LoadPairingIdFromPrefs(connectToPusher: true);
	}

	private void LoadPairingIdFromPrefs(bool connectToPusher)
	{
		if (string.IsNullOrEmpty(playerPrefsKey) || !PlayerPrefs.HasKey(playerPrefsKey))
		{
			return;
		}

		string stored = PlayerPrefs.GetString(playerPrefsKey);
		if (string.IsNullOrWhiteSpace(stored))
		{
			return;
		}

		SetPairingIdInternal(stored, false);
		if (connectToPusher)
		{
			_ = pusherService?.ConnectPairingChannelAsync(stored);
		}
	}
}

