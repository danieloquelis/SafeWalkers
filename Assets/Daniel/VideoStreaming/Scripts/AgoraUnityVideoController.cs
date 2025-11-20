using System;
using UnityEngine;

// NOTE:
// This script assumes that the Agora Unity Video SDK is imported into the project.
// After importing the SDK, make sure the Agora namespaces (Agora.Rtc, etc.) are available.
// See Agora's Video Calling product overview for capabilities:
// https://docs.agora.io/en/video-calling/overview/product-overview

#if AGORA_RTC_SDK
using Agora.Rtc;
using Meta.XR; // For PassthroughCameraAccess (Meta MRUK PCA)
using Unity.Collections;
#endif

/// <summary>
/// Thin wrapper around the Agora Unity Video SDK that:
/// - Initializes the SDK with the provided App ID.
/// - Joins the provided channel (matching the session id generated in VideoStreamingSessionManager).
/// - Publishes local audio/video.
/// - Provides hooks for wiring remote video to a Unity surface.
///
/// On Quest / Meta devices, the actual camera source should come from PassthroughCameraAccess
/// (see Meta's migration guide from WebcamTexture to PCA) and be fed into Agora as a custom video source.
/// This script focuses on the Agora side and keeps the PCA wiring as a separate concern.
/// </summary>
public class AgoraUnityVideoController : MonoBehaviour
{
    [Header("Agora Credentials")]
    [Tooltip("Agora App ID (not secret, safe to keep in client for testing).")]
    [SerializeField] private string appId = "7dfebc6ae4c64cf0b067d3d436b7fb44";

    [Tooltip("Token is optional for testing if your Agora project is configured without tokens.")]
    [SerializeField] private string token = ""; // Empty for testing without a token.

    [Header("Remote Video")]
    [Tooltip("Surface where the remote video will be rendered (e.g., a RawImage or Quad).")]
    [SerializeField] private GameObject remoteVideoSurface;

#if AGORA_RTC_SDK
    [Header("Passthrough Camera (PCA)")]
    [Tooltip("Meta PassthroughCameraAccess component that provides the Quest headset camera frames.")]
    [SerializeField] private PassthroughCameraAccess passthroughCameraAccess;

    [Tooltip("If true, use PassthroughCameraAccess frames as the outgoing video source to Agora.")]
    [SerializeField] private bool usePassthroughAsExternalVideoSource = true;

    [Tooltip("Target FPS for pushing PCA frames into Agora.")]
    [SerializeField] private int passthroughTargetFps = 30;
#endif

    /// <summary>Current Agora channel name.</summary>
    public string CurrentChannelName { get; private set; }

    // Backing Agora engine instance (from current Agora Unity SDK)
#if AGORA_RTC_SDK
    private RtcEngine _rtcEngine;

    // PCA → Agora state
    private bool _externalSourceConfigured;
    private float _pcaFrameInterval;
    private float _pcaTimeSinceLastPush;
    private byte[] _pcaBuffer;
#endif

    private bool _initialized;

    private void Start()
    {
#if AGORA_RTC_SDK
        _pcaFrameInterval = passthroughTargetFps > 0 ? 1f / passthroughTargetFps : 1f / 30f;

        // Request PCA permission if needed
        if (usePassthroughAsExternalVideoSource && passthroughCameraAccess != null)
        {
            RequestPassthroughCameraPermission();
        }
#endif
    }

#if AGORA_RTC_SDK
    private void RequestPassthroughCameraPermission()
    {
        const string pcaPermission = "horizonos.permission.HEADSET_CAMERA";
        
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(pcaPermission))
        {
            Debug.Log("[AgoraUnityVideoController] Requesting Passthrough Camera Access permission...");
            UnityEngine.Android.Permission.RequestUserPermission(pcaPermission);
        }
        else
        {
            Debug.Log("[AgoraUnityVideoController] Passthrough Camera Access permission already granted.");
        }
    }
#endif

    /// <summary>
    /// Called by <see cref="VideoStreamingSessionManager"/> after it generates a session id.
    /// The session id is used directly as the Agora channel name so Unity and the web client
    /// can meet in the same room.
    /// </summary>
    public void StartOrJoinSession(string channelName)
    {
        if (string.IsNullOrWhiteSpace(channelName))
        {
            Debug.LogError("[AgoraUnityVideoController] Cannot join Agora channel: channel name is empty.");
            return;
        }

        CurrentChannelName = channelName;

﻿#if AGORA_RTC_SDK
        EnsureEngineInitialized();

        if (_rtcEngine == null)
        {
            Debug.LogError("[AgoraUnityVideoController] RtcEngine is not available – check that the Agora SDK is imported and the AGORA_RTC_SDK define is set.");
            return;
        }

        Debug.Log($"[AgoraUnityVideoController] Joining Agora channel: {channelName}");

        // COMMUNICATION profile fits 1:1 style calls for SafeWalkers.
        _rtcEngine.SetChannelProfile(CHANNEL_PROFILE_TYPE.CHANNEL_PROFILE_COMMUNICATION);
        _rtcEngine.SetClientRole(CLIENT_ROLE_TYPE.CLIENT_ROLE_BROADCASTER);

        // Enable video and audio.
        _rtcEngine.EnableVideo();
        _rtcEngine.EnableAudio();

        // Join without a token for testing (token can be added later).
        int result = _rtcEngine.JoinChannel(token, channelName, "", 0);
        Debug.Log($"[AgoraUnityVideoController] JoinChannel result: {result}");
#else
        Debug.LogWarning("[AgoraUnityVideoController] AGORA_RTC_SDK is not defined. Import the Agora Unity Video SDK and define AGORA_RTC_SDK in Player Settings > Scripting Define Symbols.");
#endif
    }

    private void OnApplicationQuit()
    {
        LeaveAndCleanup();
    }

    private void OnDestroy()
    {
        LeaveAndCleanup();
    }

#if AGORA_RTC_SDK
    private bool _pcaDebugLogged = false;

    private void Update()
    {
        // Push PCA frames into Agora as external video source so remote users
        // see exactly what the Quest headset sees.
        if (!_initialized || _rtcEngine == null)
        {
            if (!_pcaDebugLogged)
            {
                Debug.LogWarning($"[AgoraUnityVideoController] PCA Update blocked: _initialized={_initialized}, _rtcEngine={(_rtcEngine != null ? "exists" : "null")}");
                _pcaDebugLogged = true;
            }
            return;
        }

        if (!usePassthroughAsExternalVideoSource || passthroughCameraAccess == null)
        {
            if (!_pcaDebugLogged)
            {
                Debug.LogWarning($"[AgoraUnityVideoController] PCA disabled or not assigned: usePassthrough={usePassthroughAsExternalVideoSource}, pcaAccess={(passthroughCameraAccess != null ? "assigned" : "null")}");
                _pcaDebugLogged = true;
            }
            return;
        }

        if (!passthroughCameraAccess.IsPlaying)
        {
            if (!_pcaDebugLogged)
            {
                Debug.LogWarning($"[AgoraUnityVideoController] PCA is not playing yet. Check that PassthroughCameraAccess is enabled and has camera permission.");
                _pcaDebugLogged = true;
            }
            return;
        }

        if (!passthroughCameraAccess.IsUpdatedThisFrame)
            return;

        // Configure external video source once.
        if (!_externalSourceConfigured)
        {
            var senderOptions = new SenderOptions();
            var rc = _rtcEngine.SetExternalVideoSource(
                enabled: true,
                useTexture: false,
                sourceType: EXTERNAL_VIDEO_SOURCE_TYPE.VIDEO_FRAME,
                encodedVideoOption: senderOptions);

            Debug.Log($"[AgoraUnityVideoController] SetExternalVideoSource result: {rc}");
            _externalSourceConfigured = rc == 0;
            if (!_externalSourceConfigured)
            {
                Debug.LogError($"[AgoraUnityVideoController] Failed to configure external video source, result code: {rc}");
                return;
            }
        }

        _pcaTimeSinceLastPush += Time.deltaTime;
        if (_pcaTimeSinceLastPush < _pcaFrameInterval)
            return;
        _pcaTimeSinceLastPush = 0f;

        // Get latest PCA colors and resolution.
        NativeArray<Color32> colors = passthroughCameraAccess.GetColors();
        if (!colors.IsCreated || colors.Length == 0)
        {
            Debug.LogWarning($"[AgoraUnityVideoController] PCA GetColors returned empty or invalid array. IsCreated={colors.IsCreated}, Length={colors.Length}");
            return;
        }

        var res = passthroughCameraAccess.CurrentResolution;
        int width = res.x;
        int height = res.y;
        int pixelCount = width * height;
        if (pixelCount <= 0 || colors.Length < pixelCount)
        {
            Debug.LogWarning($"[AgoraUnityVideoController] Invalid PCA resolution: {width}x{height}, pixelCount={pixelCount}, colors.Length={colors.Length}");
            return;
        }

        if (!_pcaDebugLogged)
        {
            Debug.Log($"[AgoraUnityVideoController] Successfully capturing PCA frames: {width}x{height}, pushing at {passthroughTargetFps} FPS");
            _pcaDebugLogged = true;
        }

        int bufferSize = pixelCount * 4;
        if (_pcaBuffer == null || _pcaBuffer.Length != bufferSize)
        {
            _pcaBuffer = new byte[bufferSize];
        }

        // Copy Color32 array into RGBA byte buffer for Agora.
        for (int i = 0; i < pixelCount; i++)
        {
            Color32 c = colors[i];
            int idx = i * 4;
            _pcaBuffer[idx + 0] = c.r;
            _pcaBuffer[idx + 1] = c.g;
            _pcaBuffer[idx + 2] = c.b;
            _pcaBuffer[idx + 3] = c.a;
        }

        var frame = new ExternalVideoFrame
        {
            type = VIDEO_BUFFER_TYPE.VIDEO_BUFFER_RAW_DATA,
            format = VIDEO_PIXEL_FORMAT.VIDEO_PIXEL_RGBA,
            buffer = _pcaBuffer,
            stride = width,
            height = height,
            cropLeft = 0,
            cropTop = 0,
            cropRight = 0,
            cropBottom = 0,
            rotation = 0,
            timestamp = (long)(Time.realtimeSinceStartup * 1000)
        };

        int pushResult = _rtcEngine.PushVideoFrame(frame);
        if (pushResult != 0)
        {
            Debug.LogWarning($"[AgoraUnityVideoController] PushVideoFrame failed with code: {pushResult}");
        }
    }
#endif

    /// <summary>
    /// Leaves the current channel and disposes the Agora engine.
    /// </summary>
    public void LeaveAndCleanup()
    {
#if AGORA_RTC_SDK
        if (_rtcEngine != null)
        {
            Debug.Log("[AgoraUnityVideoController] Leaving Agora channel and disposing engine.");
            _rtcEngine.LeaveChannel();
            _rtcEngine.Dispose();
            _rtcEngine = null;
            _initialized = false;
        }
#endif
    }

﻿#if AGORA_RTC_SDK
    private void EnsureEngineInitialized()
    {
        if (_initialized)
            return;

        if (string.IsNullOrWhiteSpace(appId))
        {
            Debug.LogError("[AgoraUnityVideoController] App ID is empty. Set it in the inspector.");
            return;
        }

        Debug.Log("[AgoraUnityVideoController] Initializing Agora engine.");

        // Follow the pattern from Agora's own JoinChannelVideo example
        _rtcEngine = Agora.Rtc.RtcEngine.CreateAgoraRtcEngine() as RtcEngine;

        var context = new RtcEngineContext();
        context.appId = appId;
        context.channelProfile = CHANNEL_PROFILE_TYPE.CHANNEL_PROFILE_COMMUNICATION;
        context.audioScenario = AUDIO_SCENARIO_TYPE.AUDIO_SCENARIO_DEFAULT;
        context.areaCode = AREA_CODE.AREA_CODE_GLOB;
        context.logConfig = new LogConfig
        {
            filePath = "log_agora.txt",
            fileSizeInKB = 2048,
            level = LOG_LEVEL.LOG_LEVEL_INFO
        };

        var initResult = _rtcEngine.Initialize(context);
        Debug.Log($"[AgoraUnityVideoController] Initialize result: {initResult}");

        // Attach our event handler so we can respond to remote user join/leave.
        _rtcEngine.InitEventHandler(new UnityAgoraEventHandler(this));

        _initialized = true;
    }

    /// <summary>
    /// Event handler that bridges Agora callbacks into this controller.
    /// </summary>
    private class UnityAgoraEventHandler : IRtcEngineEventHandler
    {
        private readonly AgoraUnityVideoController _controller;

        public UnityAgoraEventHandler(AgoraUnityVideoController controller)
        {
            _controller = controller;
        }

        public override void OnJoinChannelSuccess(RtcConnection connection, int elapsed)
        {
            Debug.Log($"[AgoraUnityVideoController] Joined channel '{connection.channelId}' with uid {connection.localUid}, elapsed={elapsed}ms");
        }

        public override void OnUserJoined(RtcConnection connection, uint uid, int elapsed)
        {
            Debug.Log($"[AgoraUnityVideoController] Remote user joined, uid={uid}, elapsed={elapsed}ms");

            if (_controller.remoteVideoSurface != null)
            {
                _controller.TryAttachRemoteVideoSurface(_controller.remoteVideoSurface, uid, connection.channelId);
            }
        }

        public override void OnUserOffline(RtcConnection connection, uint uid, USER_OFFLINE_REASON_TYPE reason)
        {
            Debug.Log($"[AgoraUnityVideoController] Remote user left, uid={uid}, reason={reason}");
        }
    }

    /// <summary>
    /// Tries to add / configure the Agora VideoSurface component for the given remote user.
    /// The VideoSurface script is provided by the Agora Unity Video SDK.
    /// </summary>
    private void TryAttachRemoteVideoSurface(GameObject target, uint uid, string channelId)
    {
        if (target == null)
            return;

        var videoSurface = target.GetComponent<VideoSurface>();
        if (videoSurface == null)
        {
            videoSurface = target.AddComponent<VideoSurface>();
        }

        // Follow the current SDK usage from JoinChannelVideo.MakeVideoView:
        // local uid 0 vs remote uid > 0.
        if (uid == 0)
        {
            videoSurface.SetForUser(uid, channelId);
        }
        else
        {
            videoSurface.SetForUser(uid, channelId, VIDEO_SOURCE_TYPE.VIDEO_SOURCE_REMOTE);
        }

        videoSurface.SetEnable(true);
    }
#endif
}


