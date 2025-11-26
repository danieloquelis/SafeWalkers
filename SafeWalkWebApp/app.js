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
const connectBtn = document.getElementById("connectBtn");
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
  if (mapInstance) {
    console.log("[Map] Map already initialized");
    return;
  }

  if (!window.L) {
    console.warn("[Map] Leaflet not loaded yet");
    return;
  }

  console.log("[Map] Initializing map...");
  try {
    mapInstance = L.map("map", {
      zoomControl: false,
      attributionControl: false,
      closePopupOnClick: false,
      dragging: true,
    }).setView([0, 0], MAP_ZOOM);

    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
      maxZoom: 19,
    }).addTo(mapInstance);

    console.log("[Map] Map initialized successfully");
  } catch (err) {
    console.error("[Map] Failed to initialize map:", err);
  }
}

function updateMap(lat, lng) {
  console.log(`[Map] Update requested: lat=${lat}, lng=${lng}`);
  ensureMap();

  if (!mapInstance) {
    console.error("[Map] Cannot update - map not initialized");
    return;
  }

  const coords = [lat, lng];
  if (!userMarker) {
    console.log("[Map] Creating marker");
    userMarker = L.circleMarker(coords, {
      radius: 7,
      weight: 2,
      color: "#34d399",
      fillColor: "#10b981",
      fillOpacity: 0.85,
    }).addTo(mapInstance);
  } else {
    console.log("[Map] Updating marker position");
    userMarker.setLatLng(coords);
  }

  if (!lastCoords) {
    console.log("[Map] First location - centering map");
    mapInstance.setView(coords, MAP_ZOOM);
  } else {
    console.log("[Map] Panning to new location");
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
    if (connectBtn) {
      connectBtn.classList.add("hidden");
      connectBtn.disabled = true;
      connectBtn.textContent = "Connected";
    }
  } catch (err) {
    console.error("Failed to join Agora channel", err);
    setStatus("Camera access denied. Please allow permissions and retry.");
    if (connectBtn) {
      connectBtn.disabled = false;
      connectBtn.textContent = "Retry connection";
    }
  }
}

async function leaveVideoChannel() {
  if (!joined || !agoraClient) return;
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
    await agoraClient.leave();
  } catch (err) {
    console.error("Error leaving Agora channel", err);
  } finally {
    joined = false;
    const localPlayer = document.getElementById("local-player");
    if (localPlayer) {
      localPlayer.innerHTML = '<div class="video-loading">Camera preview</div>';
    }
    if (remoteLoading) {
      remoteLoading.style.display = "flex";
      remoteLoading.textContent = "Waiting for SafeWalker…";
    }
    if (connectBtn) {
      connectBtn.classList.remove("hidden");
      connectBtn.disabled = false;
      connectBtn.textContent = "Reconnect";
    }
  }
}

function initPusher() {
  if (pusher) {
    console.log("[Pusher] Already initialized");
    return;
  }

  if (!window.Pusher) {
    console.error("[Pusher] Pusher library not loaded");
    return;
  }

  console.log(
    "[Pusher] Initializing with key:",
    PUSHER_KEY,
    "cluster:",
    PUSHER_CLUSTER
  );
  Pusher.logToConsole = false; // Use our own logging
  pusher = new Pusher(PUSHER_KEY, {
    cluster: PUSHER_CLUSTER,
    forceTLS: true,
  });

  pusher.connection.bind("state_change", (state) => {
    console.log("[Pusher] Connection state:", state.current);
    setStatus(`Location stream: ${state.current}`);
  });

  pusher.connection.bind("error", (err) => {
    console.error("[Pusher] Connection error:", err);
    setStatus("Location stream error. Retrying…");
  });
}

function subscribeToLocations() {
  if (!sessionId) {
    console.warn("[Pusher] Cannot subscribe - no sessionId");
    return;
  }

  initPusher();
  if (!pusher) {
    console.error("[Pusher] Pusher not initialized");
    return;
  }

  if (locationChannel) {
    pusher.unsubscribe(locationChannel.name);
  }

  const channelName = SESSION_CHANNEL_PREFIX + sessionId;
  console.log("[Pusher] Subscribing to channel:", channelName);
  locationChannel = pusher.subscribe(channelName);

  locationChannel.bind("pusher:subscription_succeeded", () => {
    console.log("[Pusher] Subscription succeeded");
    setStatus("Connected to live location feed");
  });

  locationChannel.bind(LOCATION_EVENT, (payload) => {
    console.log("[Pusher] Location event received:", payload);
    const data =
      typeof payload === "string" ? JSON.parse(payload) : payload || {};
    console.log("[Pusher] Parsed data:", data);

    if (
      typeof data.latitude === "number" &&
      typeof data.longitude === "number"
    ) {
      console.log(
        `[Pusher] Valid location: ${data.latitude}, ${data.longitude}`
      );
      updateMap(data.latitude, data.longitude);
      setStatus(
        `Last update • ${new Date(
          data.timestamp || Date.now()
        ).toLocaleTimeString()}`
      );
    } else {
      console.warn("[Pusher] Invalid location data:", data);
    }
  });
}

async function startExperience() {
  console.log("[App] Starting experience with sessionId:", sessionId);

  // Initialize map early
  ensureMap();
  initMapInteractions();

  if (!sessionId) {
    setStatus(
      "Missing sessionId. Append ?sessionId=XYZ to the URL shared by the headset."
    );
    if (remoteLoading) {
      remoteLoading.textContent =
        "Missing sessionId in URL. Ask the SafeWalker to resend the link.";
    }
    if (connectBtn) {
      connectBtn.disabled = true;
      connectBtn.textContent = "Missing sessionId";
    }
    return;
  }

  if (sessionLabel) {
    sessionLabel.textContent = sessionId;
  }

  subscribeToLocations();

  if (connectBtn) {
    connectBtn.addEventListener("click", () => {
      if (!joined) {
        connectBtn.disabled = true;
        connectBtn.textContent = "Connecting…";
        joinVideoChannel();
      }
    });
  } else {
    joinVideoChannel();
  }
}

document.addEventListener("DOMContentLoaded", startExperience);
window.addEventListener("beforeunload", () => {
  leaveVideoChannel();
});
