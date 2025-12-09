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
	public int ScanCount => _scanCount;
	public int RequiredScans => RequiredScansConst;
	public bool IsPairingComplete => _scanCount >= RequiredScansConst;

	private string _currentPairingId;
	private string _lastScannedPairingId;
	private int _scanCount = 0;
	private const int RequiredScansConst = 2;
	private const int MaxScans = 2;

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

	public async void HandleQrPayload(string payload)
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

			string sanitizedPairingId = parsed.pairingId.Trim();
			Debug.Log($"[SafeWalkPairingController] ============ QR CODE DETECTED ============");
			Debug.Log($"[SafeWalkPairingController] Pairing ID: {sanitizedPairingId}");

			// Store the pairing ID immediately
			SetPairingIdInternal(sanitizedPairingId, true);
			Debug.Log($"[SafeWalkPairingController] Pairing ID saved to PlayerPrefs");

			if (pusherService == null)
			{
				Debug.LogError("[SafeWalkPairingController] CRITICAL: Pusher service is NULL!");
				return;
			}

			Debug.Log("[SafeWalkPairingController] Starting Pusher connection...");

			// Connect to Pusher channel first (waits for subscription to complete)
			await pusherService.ConnectPairingChannelAsync(sanitizedPairingId);
			Debug.Log($"[SafeWalkPairingController] ✓ Connected to Pusher channel: {sanitizedPairingId}");

			// Wait a moment to ensure mobile is ready
			Debug.Log("[SafeWalkPairingController] Waiting 2 seconds for mobile app to be ready...");
			await Task.Delay(2000);

			// Send the pairing event
			Debug.Log("[SafeWalkPairingController] Sending device_paired event to mobile...");
			await pusherService.TriggerDevicePairedAsync(sanitizedPairingId);
			Debug.Log("[SafeWalkPairingController] ✓✓✓ PAIRING COMPLETE - Event sent to mobile app ✓✓✓");
		}
		catch (Exception ex)
		{
			Debug.LogError($"[SafeWalkPairingController] ❌ ERROR during pairing: {ex.Message}");
			Debug.LogError($"[SafeWalkPairingController] Stack trace: {ex.StackTrace}");
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

	/// <summary>
	/// Resets the scan counter. Use this when you want to allow re-pairing.
	/// </summary>
	public void ResetScanCounter()
	{
		_scanCount = 0;
		_lastScannedPairingId = null;
		Debug.Log("[SafeWalkPairingController] Scan counter reset. Ready for new pairing.");
	}

	private void LoadPairingIdFromPrefs(bool connectToPusher)
	{
		Debug.Log("[SafeWalkPairingController] LoadPairingIdFromPrefs called");

		if (string.IsNullOrEmpty(playerPrefsKey) || !PlayerPrefs.HasKey(playerPrefsKey))
		{
			Debug.Log("[SafeWalkPairingController] No pairing ID found in PlayerPrefs");
			return;
		}

		string stored = PlayerPrefs.GetString(playerPrefsKey);
		if (string.IsNullOrWhiteSpace(stored))
		{
			Debug.Log("[SafeWalkPairingController] Pairing ID in PlayerPrefs is empty");
			return;
		}

		Debug.Log($"[SafeWalkPairingController] Found pairing ID in PlayerPrefs: {stored}");
		SetPairingIdInternal(stored, false);

		if (connectToPusher && pusherService != null)
		{
			Debug.Log("[SafeWalkPairingController] Connecting to Pusher on startup (async, non-blocking)...");
			// Don't await - let it connect in background
			_ = pusherService.ConnectPairingChannelAsync(stored);
		}
	}
}

