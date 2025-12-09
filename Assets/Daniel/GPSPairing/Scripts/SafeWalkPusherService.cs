using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NativeWebSocket;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

[Serializable]
public class SafeWalkLocationPayload
{
	public string pairingId;
	public string sessionId;
	public double latitude;
	public double longitude;
	public double accuracy;
	public long timestamp;
}

[Serializable]
public class SafeWalkLocationEvent : UnityEvent<SafeWalkLocationPayload> { }

/// <summary>
/// Manages both the WebSocket subscription to Pusher Channels (to receive data from the mobile app)
/// and the HTTP REST calls required to trigger events towards the phone.
/// </summary>
public class SafeWalkPusherService : MonoBehaviour
{
	[Header("Pusher Credentials")]
	[SerializeField] private SafeWalkPusherSettings settings;

	[Header("Diagnostics")]
	[SerializeField] private bool verboseLogging = true;

	public SafeWalkLocationEvent OnLocationUpdated;

	private WebSocket _webSocket;
	private string _currentPairingId;
	private string _currentChannel;
	private string _socketId;
	private bool _isSubscribed = false;
	private TaskCompletionSource<bool> _subscriptionCompletionSource;
	private TaskCompletionSource<bool> _connectionCompletionSource;
	private bool _isConnecting = false;

	private void Awake()
	{
		if (OnLocationUpdated == null)
		{
			OnLocationUpdated = new SafeWalkLocationEvent();
		}
	}

	private void Update()
	{
#if !UNITY_WEBGL || UNITY_EDITOR
		_webSocket?.DispatchMessageQueue();
#endif
	}

	private void OnDestroy()
	{
		CloseSocket();
	}

	public async Task ConnectPairingChannelAsync(string pairingId)
	{
		Debug.Log($"[SafeWalkPusherService] >>> ConnectPairingChannelAsync called with pairing ID: {pairingId}");

		// Check if another connection is in progress
		if (_isConnecting)
		{
			Debug.LogWarning("[SafeWalkPusherService] Connection already in progress - cancelling previous attempt");
			// Cancel any in-flight completion sources
			_connectionCompletionSource?.TrySetCanceled();
			_subscriptionCompletionSource?.TrySetCanceled();
			_isConnecting = false;
		}

		_isConnecting = true;

		try
		{
			if (!EnsureSettingsLoaded())
			{
				Debug.LogError("[SafeWalkPusherService] Settings not loaded!");
				throw new InvalidOperationException("Pusher settings not loaded");
			}

			if (string.IsNullOrWhiteSpace(pairingId))
			{
				Debug.LogWarning("[SafeWalkPusherService] Cannot connect without a pairing id.");
				throw new ArgumentException("Pairing ID cannot be empty", nameof(pairingId));
			}

			string targetChannel = BuildPairingChannelName(pairingId);
			Debug.Log($"[SafeWalkPusherService] Target channel: {targetChannel}");
			Debug.Log($"[SafeWalkPusherService] Current channel: {_currentChannel ?? "null"}");
			Debug.Log($"[SafeWalkPusherService] WebSocket state: {_webSocket?.State.ToString() ?? "null"}");
			Debug.Log($"[SafeWalkPusherService] Is subscribed: {_isSubscribed}");

			// Check if already connected
			if (_currentChannel == targetChannel &&
				_webSocket != null &&
				_webSocket.State == WebSocketState.Open &&
				_isSubscribed)
			{
				Debug.Log("[SafeWalkPusherService] ✓ Already connected and subscribed - skipping reconnection");
				return;
			}

			Debug.Log("[SafeWalkPusherService] Setting up new connection...");
			_currentPairingId = pairingId;
			_currentChannel = targetChannel;
			_isSubscribed = false;

			// Create NEW subscription completion source for this pairing attempt
			_subscriptionCompletionSource = new TaskCompletionSource<bool>();

			Debug.Log("[SafeWalkPusherService] Calling OpenSocketAsync...");
			try
			{
				await OpenSocketAsync();
				Debug.Log("[SafeWalkPusherService] OpenSocketAsync completed");
			}
			catch (Exception ex)
			{
				Debug.LogError($"[SafeWalkPusherService] ❌ OpenSocketAsync failed: {ex.Message}");
				_subscriptionCompletionSource = null;
				throw; // Re-throw to propagate to caller
			}

			// Wait for subscription to succeed (with 10 second timeout)
			Debug.Log("[SafeWalkPusherService] Waiting for subscription to complete (10s timeout)...");
			var timeoutTask = Task.Delay(10000);
			var completedTask = await Task.WhenAny(_subscriptionCompletionSource.Task, timeoutTask);

			if (completedTask == timeoutTask)
			{
				Debug.LogError("[SafeWalkPusherService] ❌ Subscription timeout after 10 seconds!");
				Debug.LogError($"[SafeWalkPusherService] WebSocket state: {_webSocket?.State.ToString() ?? "null"}");
				_subscriptionCompletionSource = null;
				throw new TimeoutException("Subscription to Pusher channel timed out after 10 seconds");
			}

			bool success = await _subscriptionCompletionSource.Task;
			_subscriptionCompletionSource = null; // Clear it so error handlers don't interfere

			Debug.Log($"[SafeWalkPusherService] ✓ Subscription completed with result: {success}");
			if (!success)
			{
				throw new InvalidOperationException("Pusher subscription failed");
			}
		}
		finally
		{
			_isConnecting = false;
		}
	}

	public async Task TriggerSafeModeEnabledAsync(string pairingId, string sessionId, string videoUrl)
	{
		if (!EnsureSettingsLoaded())
			return;

		if (string.IsNullOrWhiteSpace(sessionId))
		{
			throw new ArgumentException("Session id is required for safe_mode_enabled.", nameof(sessionId));
		}

		var payload = new
		{
			pairingId,
			id = sessionId,
			videoUrl,
			timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		};

		await TriggerEventAsync(new[] { BuildPairingChannelName(pairingId) }, "safe_mode_enabled", payload);
	}

	public async Task TriggerSafeModeDisabledAsync(string pairingId, string sessionId)
	{
		if (!EnsureSettingsLoaded())
			return;

		var payload = new
		{
			pairingId,
			id = sessionId,
			timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		};

		await TriggerEventAsync(new[] { BuildPairingChannelName(pairingId) }, "safe_mode_disabled", payload);
	}

	public async Task TriggerDevicePairedAsync(string pairingId)
	{
		if (!EnsureSettingsLoaded())
			return;

		string channelName = BuildPairingChannelName(pairingId);
		Debug.Log($"[SafeWalkPusherService] ========================================");
		Debug.Log($"[SafeWalkPusherService] Sending device_paired event");
		Debug.Log($"[SafeWalkPusherService] Channel: {channelName}");
		Debug.Log($"[SafeWalkPusherService] Pairing ID: {pairingId}");

		var payload = new
		{
			pairingId,
			deviceType = "metaquest",
			timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		};

		Debug.Log($"[SafeWalkPusherService] Payload: {JsonConvert.SerializeObject(payload)}");

		await TriggerEventAsync(new[] { channelName }, "device_paired", payload);

		Debug.Log($"[SafeWalkPusherService] ✓ Event sent successfully");
		Debug.Log($"[SafeWalkPusherService] ========================================");
	}

	public static string BuildPairingChannelName(string pairingId) => $"safewalk-mobile-{pairingId}";
	public static string BuildSessionChannelName(string sessionId) => $"safewalk-session-{sessionId}";

	private async Task OpenSocketAsync()
	{
		Debug.Log("[SafeWalkPusherService] >>> OpenSocketAsync started");

		// Close existing socket without completing TaskCompletionSources
		CloseSocket();
		Debug.Log("[SafeWalkPusherService] Closed existing socket");

		if (!EnsureSettingsLoaded())
		{
			Debug.LogError("[SafeWalkPusherService] Settings not loaded in OpenSocketAsync");
			throw new InvalidOperationException("Pusher settings not loaded");
		}

		if (string.IsNullOrWhiteSpace(settings.apiKey) || string.IsNullOrWhiteSpace(settings.cluster))
		{
			Debug.LogError("[SafeWalkPusherService] Missing Pusher configuration (apiKey or cluster)");
			throw new InvalidOperationException("Missing Pusher configuration");
		}

		string url =
			$"wss://ws-{settings.cluster}.pusher.com/app/{settings.apiKey}?protocol=7&client=unity-native&version=1.0&support_transports=ws";

		Debug.Log($"[SafeWalkPusherService] WebSocket URL: {url}");

		// Create NEW connection completion source for this connection attempt
		_connectionCompletionSource = new TaskCompletionSource<bool>();

		_webSocket = new WebSocket(url);
		_webSocket.OnOpen += HandleSocketOpen;
		_webSocket.OnClose += HandleSocketClose;
		_webSocket.OnError += HandleSocketError;
		_webSocket.OnMessage += HandleSocketMessage;

		Debug.Log("[SafeWalkPusherService] WebSocket created, starting connection (non-blocking)...");

		// Start connection but DON'T await it - this allows Unity's Update() loop to continue
		_ = _webSocket.Connect();

		// Wait for the OnOpen callback to fire via TaskCompletionSource
		Debug.Log("[SafeWalkPusherService] Waiting for OnOpen callback (8s timeout)...");
		var timeoutTask = Task.Delay(8000);
		var completedTask = await Task.WhenAny(_connectionCompletionSource.Task, timeoutTask);

		if (completedTask == timeoutTask)
		{
			Debug.LogError("[SafeWalkPusherService] ❌ WebSocket OnOpen TIMEOUT after 8 seconds!");
			Debug.LogError("[SafeWalkPusherService] Connection never opened. Check network/firewall.");
			_connectionCompletionSource = null;
			CloseSocket();
			throw new TimeoutException("WebSocket connection attempt timed out");
		}

		bool success = await _connectionCompletionSource.Task;
		_connectionCompletionSource = null; // Clear it so error handlers don't interfere

		if (!success)
		{
			Debug.LogError("[SafeWalkPusherService] ❌ WebSocket connection failed");
			CloseSocket();
			throw new InvalidOperationException("WebSocket connection failed");
		}

		Debug.Log("[SafeWalkPusherService] ✓ WebSocket connection established");
	}

	private void HandleSocketOpen()
	{
		Debug.Log("[SafeWalkPusherService] ✓✓✓ HandleSocketOpen called - WebSocket is OPEN ✓✓✓");

		// Signal that connection is established
		_connectionCompletionSource?.TrySetResult(true);

		SubscribeToChannel();
	}

	private void HandleSocketClose(WebSocketCloseCode closeCode)
	{
		Debug.LogWarning($"[SafeWalkPusherService] WebSocket closed: {closeCode}");

		// Only signal failure if we're actively connecting and completion sources exist
		if (_isConnecting)
		{
			_connectionCompletionSource?.TrySetResult(false);
			_subscriptionCompletionSource?.TrySetResult(false);
		}
	}

	private void HandleSocketError(string message)
	{
		Debug.LogError($"[SafeWalkPusherService] WebSocket error: {message}");

		// Only signal failure if we're actively connecting and completion sources exist
		if (_isConnecting)
		{
			_connectionCompletionSource?.TrySetResult(false);
			_subscriptionCompletionSource?.TrySetResult(false);
		}
	}

	private void HandleSocketMessage(byte[] bytes)
	{
		string raw = Encoding.UTF8.GetString(bytes);
		if (verboseLogging)
		{
			Debug.Log($"[SafeWalkPusherService] << {raw}");
		}

		var envelope = JsonConvert.DeserializeObject<PusherEnvelope>(raw);
		if (envelope == null)
			return;

		switch (envelope.Event)
		{
			case "pusher:connection_established":
				HandleConnectionEstablished(envelope.Data);
				break;
			case "pusher:ping":
				SendSocketPayload(new PusherClientMessage { @event = "pusher:pong" });
				break;
			case "pusher_internal:subscription_succeeded":
				Debug.Log("[SafeWalkPusherService] ✓✓✓ SUBSCRIPTION SUCCEEDED ✓✓✓");
				_isSubscribed = true;
				if (_subscriptionCompletionSource != null)
				{
					Debug.Log("[SafeWalkPusherService] Setting completion source result to true");
					_subscriptionCompletionSource.TrySetResult(true);
				}
				else
				{
					Debug.LogWarning("[SafeWalkPusherService] Subscription completion source is NULL!");
				}
				break;
			case "mobile_location_update":
				HandleLocationUpdate(envelope.Data);
				break;
			default:
				if (envelope.Event?.StartsWith("pusher:error", StringComparison.OrdinalIgnoreCase) == true)
				{
					Debug.LogError($"[SafeWalkPusherService] Error from Pusher: {envelope.Data}");
				}
				break;
		}
	}

	private void HandleConnectionEstablished(string data)
	{
		Debug.Log("[SafeWalkPusherService] >>> HandleConnectionEstablished called");

		if (string.IsNullOrEmpty(data))
		{
			Debug.LogWarning("[SafeWalkPusherService] Connection established but data is empty");
			return;
		}

		var established = JsonConvert.DeserializeObject<ConnectionEstablishedPayload>(data);
		_socketId = established?.socket_id;
		Debug.Log($"[SafeWalkPusherService] ✓ Connection established. socket_id={_socketId}");

		SubscribeToChannel();
	}

	private void HandleLocationUpdate(string data)
	{
		if (string.IsNullOrEmpty(data))
			return;

		try
		{
			var payload = JsonConvert.DeserializeObject<SafeWalkLocationPayload>(data);
			if (payload != null)
			{
				OnLocationUpdated?.Invoke(payload);
			}
		}
		catch (Exception ex)
		{
			Debug.LogError($"[SafeWalkPusherService] Failed to parse location update: {ex}");
		}
	}

	private void SubscribeToChannel()
	{
		Debug.Log("[SafeWalkPusherService] >>> SubscribeToChannel called");
		Debug.Log($"[SafeWalkPusherService] WebSocket: {(_webSocket != null ? "exists" : "NULL")}");
		Debug.Log($"[SafeWalkPusherService] Current channel: {_currentChannel ?? "NULL"}");

		if (_webSocket == null || string.IsNullOrEmpty(_currentChannel))
		{
			Debug.LogWarning("[SafeWalkPusherService] Cannot subscribe - WebSocket or channel is null");
			return;
		}

		var subscribePayload = new PusherClientMessage
		{
			@event = "pusher:subscribe",
			data = new SubscriptionData { channel = _currentChannel }
		};

		Debug.Log($"[SafeWalkPusherService] Sending subscribe message for channel: {_currentChannel}");
		SendSocketPayload(subscribePayload);
		Debug.Log("[SafeWalkPusherService] Subscribe message sent");
	}

	private void SendSocketPayload(PusherClientMessage message)
	{
		if (_webSocket == null)
			return;

		string json = JsonConvert.SerializeObject(message);
		if (verboseLogging)
		{
			Debug.Log($"[SafeWalkPusherService] >> {json}");
		}

		_ = _webSocket.SendText(json);
	}

	private void CloseSocket()
	{
		if (_webSocket != null)
		{
			_webSocket.OnOpen -= HandleSocketOpen;
			_webSocket.OnClose -= HandleSocketClose;
			_webSocket.OnError -= HandleSocketError;
			_webSocket.OnMessage -= HandleSocketMessage;
			_ = _webSocket.Close();
			_webSocket = null;
		}

		_socketId = null;
		_isSubscribed = false;
		// Don't complete TaskCompletionSources here - they're managed by the connection/subscription flow
	}

	private async Task TriggerEventAsync(IEnumerable<string> channels, string eventName, object payload)
	{
		if (!EnsureSettingsLoaded())
			return;

		if (string.IsNullOrWhiteSpace(settings.appId) || string.IsNullOrWhiteSpace(settings.apiKey) ||
			string.IsNullOrWhiteSpace(settings.secret) || string.IsNullOrWhiteSpace(settings.cluster))
		{
			Debug.LogError("[SafeWalkPusherService] Missing Pusher REST configuration.");
			return;
		}

		var targetChannels = new List<string>(channels);
		if (targetChannels.Count == 0)
		{
			Debug.LogWarning("[SafeWalkPusherService] trigger called without channels.");
			return;
		}

		var bodyObject = new
		{
			name = eventName,
			channels = targetChannels,
			data = JsonConvert.SerializeObject(payload ?? new object())
		};

		string body = JsonConvert.SerializeObject(bodyObject);
		string bodyMd5 = ComputeMd5Hex(body);
		string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

		var query = new StringBuilder();
		query.Append("auth_key=").Append(settings.apiKey);
		query.Append("&auth_timestamp=").Append(timestamp);
		query.Append("&auth_version=1.0");
		query.Append("&body_md5=").Append(bodyMd5);

		string stringToSign = $"POST\n/apps/{settings.appId}/events\n{query}";
		string signature = ComputeHmacSha256(stringToSign, settings.secret);
		query.Append("&auth_signature=").Append(signature);

		string url = $"https://api-{settings.cluster}.pusher.com/apps/{settings.appId}/events?{query}";

		using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
		byte[] bodyRaw = Encoding.UTF8.GetBytes(body);
		request.uploadHandler = new UploadHandlerRaw(bodyRaw);
		request.downloadHandler = new DownloadHandlerBuffer();
		request.SetRequestHeader("Content-Type", "application/json");

		var operation = request.SendWebRequest();
		while (!operation.isDone)
		{
			await Task.Yield();
		}

		if (request.result != UnityWebRequest.Result.Success)
		{
			Debug.LogError(
				$"[SafeWalkPusherService] Failed to trigger '{eventName}': {request.result} - {request.error}\n{request.downloadHandler.text}");
		}
		else if (verboseLogging)
		{
			Debug.Log($"[SafeWalkPusherService] Event '{eventName}' sent to {targetChannels.Count} channel(s).");
		}
	}

	private static string ComputeMd5Hex(string input)
	{
		using MD5 md5 = MD5.Create();
		byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
		return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
	}

	private static string ComputeHmacSha256(string input, string key)
	{
		using HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
		byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
		return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
	}

	[Serializable]
	private class PusherEnvelope
	{
		[JsonProperty("event")] public string Event;
		[JsonProperty("data")] public string Data;
		[JsonProperty("channel")] public string Channel;
	}

	[Serializable]
	private class PusherClientMessage
	{
		public string @event;
		public SubscriptionData data;
	}

	[Serializable]
	private class SubscriptionData
	{
		public string channel;
	}

	[Serializable]
	private class ConnectionEstablishedPayload
	{
		public string socket_id;
		public int activity_timeout;
	}

	private bool EnsureSettingsLoaded()
	{
		if (settings == null)
		{
			settings = Resources.Load<SafeWalkPusherSettings>("SafeWalkPusherSettings");
		}

		if (settings == null)
		{
			Debug.LogError("[SafeWalkPusherService] SafeWalkPusherSettings is not assigned. Create a ScriptableObject and reference it.");
			return false;
		}

		return true;
	}
}

