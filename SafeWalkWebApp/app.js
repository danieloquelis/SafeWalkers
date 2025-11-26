const AGORA_APP_ID = "7dfebc6ae4c64cf0b067d3d436b7fb44";
const AGORA_TOKEN = null;
const PUSHER_KEY = "c7915488e6204cc07cf0";
const PUSHER_CLUSTER = "eu";
const SESSION_CHANNEL_PREFIX = "safewalk-session-";
const LOCATION_EVENT = "mobile_location_update";
const MAP_ZOOM = 15;

const sessionLabel = document.getElementById("sessionLabel");
const pusherStatus = document.getElementById("pusherStatus");
const remoteLoading = document.getElementById("remoteLoading");
const navigateBtn = document.getElementById("navigateBtn");
const urlParams = new URLSearchParams(window.location.search);
const sessionId = (urlParams.get("sessionId") || "").trim();

let agoraClient;
let localAudioTrack;
let localVideoTrack;
let joined = false;

let pusher;
let locationChannel;

let mapInstance;
let userMarker;
let lastCoords = null;

function setStatus(message) {
  if (pusherStatus) {
    pusherStatus.textContent = message;
  }
}

function ensureMap() {
  if (mapInstance || !window.L) return;

  mapInstance = L.map("map", {
    zoomControl: false,
    attributionControl: false,
    closePopupOnClick: false,
    dragging: true,
  }).setView([0, 0], MAP_ZOOM);

  L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
    maxZoom: 19,
  }).addTo(mapInstance);
}

function updateMap(lat, lng) {
  ensureMap();
  if (!mapInstance) return;

  const coords = [lat, lng];
  if (!userMarker) {
    userMarker = L.circleMarker(coords, {
      radius: 7,
      weight: 2,
      color: "#34d399",
      fillColor: "#10b981",
      fillOpacity: 0.85,
    }).addTo(mapInstance);
  } else {
    userMarker.setLatLng(coords);
  }

  if (!lastCoords) {
    mapInstance.setView(coords, MAP_ZOOM);
  } else {
    mapInstance.panTo(coords, { animate: true, duration: 0.6 });
  }

  lastCoords = coords;
  if (navigateBtn) {
    navigateBtn.disabled = false;
  }
}

function initMapInteractions() {
  ensureMap();

  if (navigateBtn) {
    navigateBtn.addEventListener("click", () => {
      if (!lastCoords) return;
      const [lat, lng] = lastCoords;
      window.open(
        `https://www.google.com/maps/dir/?api=1&destination=${lat},${lng}`,
        "_blank",
        "noopener"
      );
    });
    navigateBtn.disabled = true;
  }
}

async function initAgora() {
  if (agoraClient) return agoraClient;
  if (!window.AgoraRTC) {
    setStatus("Video SDK missing. Refresh the page.");
    return null;
  }

  agoraClient = AgoraRTC.createClient({ mode: "rtc", codec: "vp8" });

  agoraClient.on("user-published", async (user, mediaType) => {
    await agoraClient.subscribe(user, mediaType);

    if (mediaType === "video") {
      const remotePlayer = document.getElementById("remote-player");
      if (remotePlayer) {
        remotePlayer.innerHTML = "";
        user.videoTrack.play("remote-player");
        if (remoteLoading) remoteLoading.style.display = "none";
      }
    }

    if (mediaType === "audio") {
      user.audioTrack.play();
    }
  });

  agoraClient.on("user-unpublished", () => {
    if (remoteLoading) remoteLoading.style.display = "flex";
  });

  agoraClient.on("user-left", () => {
    if (remoteLoading) {
      remoteLoading.style.display = "flex";
      remoteLoading.textContent = "SafeWalker disconnected";
    }
  });

  return agoraClient;
}

async function joinVideoChannel() {
  if (joined || !sessionId) return;

  const client = await initAgora();
  if (!client) return;

  try {
    [localAudioTrack, localVideoTrack] =
      await AgoraRTC.createMicrophoneAndCameraTracks();

    await client.join(AGORA_APP_ID, sessionId, AGORA_TOKEN, null);

    const localPlayer = document.getElementById("local-player");
    if (localPlayer) {
      localPlayer.innerHTML = "";
      localVideoTrack.play("local-player");
    }

    await client.publish([localAudioTrack, localVideoTrack]);
    joined = true;
  } catch (err) {
    console.error("Failed to join Agora channel", err);
    setStatus("Camera access denied. Please allow permissions and reload.");
  }
}

function initPusher() {
  if (pusher || !window.Pusher) return;

  Pusher.logToConsole = false;
  pusher = new Pusher(PUSHER_KEY, {
    cluster: PUSHER_CLUSTER,
    forceTLS: true,
  });

  pusher.connection.bind("state_change", (state) => {
    setStatus(`Location stream: ${state.current}`);
  });

  pusher.connection.bind("error", (err) => {
    console.error("Pusher error", err);
    setStatus("Location stream error. Retrying…");
  });
}

function subscribeToLocations() {
  if (!sessionId) return;
  initPusher();
  if (!pusher) return;

  if (locationChannel) {
    pusher.unsubscribe(locationChannel.name);
  }

  const channelName = SESSION_CHANNEL_PREFIX + sessionId;
  locationChannel = pusher.subscribe(channelName);

  locationChannel.bind("pusher:subscription_succeeded", () => {
    setStatus("Connected to live location feed");
  });

  locationChannel.bind(LOCATION_EVENT, (payload) => {
    const data =
      typeof payload === "string" ? JSON.parse(payload) : payload || {};
    if (
      typeof data.latitude === "number" &&
      typeof data.longitude === "number"
    ) {
      updateMap(data.latitude, data.longitude);
      setStatus(
        `Last update • ${new Date(
          data.timestamp || Date.now()
        ).toLocaleTimeString()}`
      );
    }
  });
}

async function startExperience() {
  initMapInteractions();

  if (!sessionId) {
    setStatus(
      "Missing sessionId. Append ?sessionId=XYZ to the URL shared by the headset."
    );
    return;
  }

  if (sessionLabel) {
    sessionLabel.textContent = sessionId;
  }

  await joinVideoChannel();
  subscribeToLocations();
}

document.addEventListener("DOMContentLoaded", startExperience);
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
