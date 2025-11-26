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
		if (!EnsureSettingsLoaded())
			return;

		if (string.IsNullOrWhiteSpace(pairingId))
		{
			Debug.LogWarning("[SafeWalkPusherService] Cannot connect without a pairing id.");
			return;
		}

		string targetChannel = BuildPairingChannelName(pairingId);

		if (_currentChannel == targetChannel &&
			_webSocket != null &&
			(_webSocket.State == WebSocketState.Open || _webSocket.State == WebSocketState.Connecting))
		{
			if (verboseLogging)
			{
				Debug.Log("[SafeWalkPusherService] Already connected to pairing channel.");
			}
			return;
		}

		_currentPairingId = pairingId;
		_currentChannel = targetChannel;

		await OpenSocketAsync();
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

	public static string BuildPairingChannelName(string pairingId) => $"safewalk-mobile-{pairingId}";
	public static string BuildSessionChannelName(string sessionId) => $"safewalk-session-{sessionId}";

	private async Task OpenSocketAsync()
	{
		CloseSocket();

		if (!EnsureSettingsLoaded())
			return;

		if (string.IsNullOrWhiteSpace(settings.apiKey) || string.IsNullOrWhiteSpace(settings.cluster))
		{
			Debug.LogError("[SafeWalkPusherService] Missing Pusher configuration.");
			return;
		}

		string url =
			$"wss://ws-{settings.cluster}.pusher.com/app/{settings.apiKey}?protocol=7&client=unity-native&version=1.0&support_transports=ws";

		_webSocket = new WebSocket(url);
		_webSocket.OnOpen += HandleSocketOpen;
		_webSocket.OnClose += HandleSocketClose;
		_webSocket.OnError += HandleSocketError;
		_webSocket.OnMessage += HandleSocketMessage;

		try
		{
			await _webSocket.Connect();
		}
		catch (Exception ex)
		{
			Debug.LogError($"[SafeWalkPusherService] Failed to connect to Pusher: {ex}");
			CloseSocket();
		}
	}

	private void HandleSocketOpen()
	{
		if (verboseLogging)
		{
			Debug.Log("[SafeWalkPusherService] WebSocket connection opened.");
		}

		SubscribeToChannel();
	}

	private void HandleSocketClose(WebSocketCloseCode closeCode)
	{
		Debug.LogWarning($"[SafeWalkPusherService] WebSocket closed: {closeCode}");
	}

	private void HandleSocketError(string message)
	{
		Debug.LogError($"[SafeWalkPusherService] WebSocket error: {message}");
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
				if (verboseLogging)
				{
					Debug.Log("[SafeWalkPusherService] Subscription succeeded.");
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
		if (string.IsNullOrEmpty(data))
			return;

		var established = JsonConvert.DeserializeObject<ConnectionEstablishedPayload>(data);
		_socketId = established?.socket_id;
		if (verboseLogging)
		{
			Debug.Log($"[SafeWalkPusherService] Connection established. socket_id={_socketId}");
		}

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
		if (_webSocket == null || string.IsNullOrEmpty(_currentChannel))
			return;

		var subscribePayload = new PusherClientMessage
		{
			@event = "pusher:subscribe",
			data = new SubscriptionData { channel = _currentChannel }
		};

		SendSocketPayload(subscribePayload);
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

