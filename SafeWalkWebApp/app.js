// SafeWalkers web client
// Uses Agora Web SDK (via CDN) to join the same channel as the Unity headset.

// IMPORTANT:
// - The session id passed in the URL (?sessionId=abcd1234) is used as the Agora channel name.
// - App ID is not secret and matches the Unity configuration.

const AGORA_APP_ID = "7dfebc6ae4c64cf0b067d3d436b7fb44";
const AGORA_TOKEN = null; // No token for testing; add one here later if you enable token auth.

const sessionIdInput = document.getElementById("sessionIdInput");
const joinBtn = document.getElementById("joinBtn");
const leaveBtn = document.getElementById("leaveBtn");
const yearSpan = document.getElementById("year");

if (yearSpan) {
  yearSpan.textContent = new Date().getFullYear().toString();
}

const urlParams = new URLSearchParams(window.location.search);
const sessionIdFromUrl = urlParams.get("sessionId") || "";

sessionIdInput.value = sessionIdFromUrl;

// Agora client and tracks
let client = null;
let localAudioTrack = null;
let localVideoTrack = null;
let joined = false;

async function initClient() {
  if (client) {
    return client;
  }

  if (!window.AgoraRTC) {
    console.error(
      "AgoraRTC is not available. Check that the CDN script is loading correctly."
    );
    alert(
      "Video SDK failed to load. Please refresh and check your network connectivity."
    );
    return null;
  }

  client = AgoraRTC.createClient({
    mode: "rtc",
    codec: "vp8",
  });

  // Remote user published media
  client.on("user-published", async (user, mediaType) => {
    await client.subscribe(user, mediaType);

    if (mediaType === "video") {
      const remotePlayerContainer = document.getElementById("remote-player");
      if (remotePlayerContainer) {
        // Clear placeholder
        remotePlayerContainer.innerHTML = "";
        user.videoTrack.play("remote-player");
      }
    }

    if (mediaType === "audio") {
      user.audioTrack.play();
    }
  });

  // Remote user left or unpublished
  client.on("user-unpublished", (user, mediaType) => {
    if (mediaType === "video") {
      const remotePlayerContainer = document.getElementById("remote-player");
      if (remotePlayerContainer) {
        remotePlayerContainer.innerHTML =
          '<div class="video-placeholder">Remote video paused</div>';
      }
    }
  });

  client.on("user-left", () => {
    const remotePlayerContainer = document.getElementById("remote-player");
    if (remotePlayerContainer) {
      remotePlayerContainer.innerHTML =
        '<div class="video-placeholder">Remote user left</div>';
    }
  });

  return client;
}

async function joinCall() {
  if (joined) return;

  const channelName = sessionIdInput.value.trim();
  if (!channelName) {
    alert(
      "Missing session ID. Make sure your link contains ?sessionId=... or type it in."
    );
    return;
  }

  const rtcClient = await initClient();
  if (!rtcClient) return;

  try {
    // Request camera/mic permission and create local tracks
    [localAudioTrack, localVideoTrack] =
      await AgoraRTC.createMicrophoneAndCameraTracks();

    const uid = await rtcClient.join(
      AGORA_APP_ID,
      channelName,
      AGORA_TOKEN,
      null
    );
    console.log("Joined Agora channel:", channelName, "with uid:", uid);

    // Play local preview
    const localPlayerContainer = document.getElementById("local-player");
    if (localPlayerContainer) {
      localPlayerContainer.innerHTML = "";
      localVideoTrack.play("local-player");
    }

    // Publish local tracks so the headset can subscribe
    await rtcClient.publish([localAudioTrack, localVideoTrack]);
    joined = true;
    joinBtn.disabled = true;
    leaveBtn.disabled = false;
  } catch (err) {
    console.error("Failed to join Agora channel:", err);
    alert(
      "Unable to start the call. Please check camera/microphone permissions and try again."
    );
  }
}

async function leaveCall() {
  if (!joined || !client) return;

  try {
    if (localAudioTrack) {
      localAudioTrack.stop();
      localAudioTrack.close();
      localAudioTrack = null;
    }
    if (localVideoTrack) {
      localVideoTrack.stop();
      localVideoTrack.close();
      localVideoTrack = null;
    }

    await client.leave();

    const localPlayerContainer = document.getElementById("local-player");
    if (localPlayerContainer) {
      localPlayerContainer.innerHTML =
        '<div class="video-placeholder">Your camera preview</div>';
    }

    const remotePlayerContainer = document.getElementById("remote-player");
    if (remotePlayerContainer) {
      remotePlayerContainer.innerHTML =
        '<div class="video-placeholder">Waiting for remote video…</div>';
    }

    joined = false;
    joinBtn.disabled = false;
    leaveBtn.disabled = true;
  } catch (err) {
    console.error("Error while leaving call:", err);
  }
}

joinBtn.addEventListener("click", () => {
  joinCall();
});

leaveBtn.addEventListener("click", () => {
  leaveCall();
});
