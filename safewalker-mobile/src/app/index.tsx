import { Pusher } from "@pusher/pusher-websocket-react-native";
import React, { useEffect, useMemo, useRef, useState } from "react";
import {
  ActivityIndicator,
  Animated,
  AppState,
  Platform,
  Pressable,
  StyleSheet,
  Text,
  View,
} from "react-native";
import QRCode from "react-native-qrcode-svg";

import * as Location from "expo-location";

import {
  LOCATION_TASK_NAME,
  ensureBackgroundLocationTask,
} from "@/background/locationTask";
import {
  PUSHER_CLUSTER,
  PUSHER_KEY,
  assertPusherConfig,
} from "@/constants/env";
import {
  getPairingChannelName,
  notifySafeModeLifecycle,
  publishLocationUpdate,
} from "@/utils/pusher";
import { getPairingId, getSessionId, setSessionId } from "@/utils/storage";

type SafeModeState = "idle" | "ready" | "sharing";

export default function SafeWalkScreen() {
  const [pairingId, setPairingId] = useState<string>("");
  const [safeModeState, setSafeModeState] = useState<SafeModeState>("idle");
  const [sessionId, setSessionIdState] = useState<string | null>(null);
  const [connecting, setConnecting] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [backgroundTaskReady, setBackgroundTaskReady] = useState(false);
  const [questPaired, setQuestPaired] = useState(false);
  const [fullyReady, setFullyReady] = useState(false);
  const sessionIdRef = useRef<string | null>(null);
  const pusherRef = useRef<Pusher | null>(null);
  const channelNameRef = useRef<string>("");
  const reconnectAttempts = useRef<number>(0);
  const reconnectTimeout = useRef<NodeJS.Timeout | null>(null);

  // Load persistent pairing ID and session ID on mount
  useEffect(() => {
    const loadStoredData = async () => {
      const storedPairingId = await getPairingId();
      const storedSessionId = await getSessionId();
      setPairingId(storedPairingId);
      setSessionIdState(storedSessionId);
    };
    loadStoredData();
  }, []);

  useEffect(() => {
    sessionIdRef.current = sessionId;
  }, [sessionId]);

  const pulse = useMemo(() => new Animated.Value(1), []);

  // Request permissions and initialize background task early
  useEffect(() => {
    const initializeApp = async () => {
      try {
        // Request permissions first
        if (Platform.OS === "android") {
          const fg = await Location.requestForegroundPermissionsAsync();
          if (fg.status !== "granted") {
            console.warn("Foreground location permission not granted");
            setError("Location permission needed");
            return;
          }

          const bg = await Location.requestBackgroundPermissionsAsync();
          if (bg.status !== "granted") {
            console.warn("Background location permission not granted");
          }
        }

        // Initialize background task EARLY - before Pusher connection
        ensureBackgroundLocationTask();

        // Give the task manager a moment to register the task
        await new Promise(resolve => setTimeout(resolve, 500));

        setBackgroundTaskReady(true);
        console.log("Background task initialized and ready");
      } catch (error) {
        console.error("Failed to initialize app", error);
        setError("Setup failed. Please restart the app.");
      }
    };

    initializeApp();
  }, []);

  useEffect(() => {
    if (safeModeState === "sharing") {
      Animated.loop(
        Animated.sequence([
          Animated.timing(pulse, {
            toValue: 1.1,
            duration: 600,
            useNativeDriver: true,
          }),
          Animated.timing(pulse, {
            toValue: 1,
            duration: 600,
            useNativeDriver: true,
          }),
        ])
      ).start();
    } else {
      pulse.setValue(1);
    }
  }, [pulse, safeModeState]);

  // Pusher connection management
  useEffect(() => {
    // Wait for background task to be ready before connecting to Pusher
    if (!pairingId || !backgroundTaskReady) {
      return;
    }

    try {
      assertPusherConfig();
    } catch (configError) {
      setError(
        configError instanceof Error
          ? configError.message
          : "Setup incomplete"
      );
      setConnecting(false);
      return;
    }

    const pusher = Pusher.getInstance();
    pusherRef.current = pusher;
    const channelName = getPairingChannelName(pairingId);
    channelNameRef.current = channelName;

    const init = async () => {
      try {
        await pusher.init({
          apiKey: PUSHER_KEY,
          cluster: PUSHER_CLUSTER,
          onConnectionStateChange: (currentState: string) => {
            console.log(`Pusher connection state: ${currentState}`);
            if (currentState === "connected") {
              setError(null);
            }
          },
          onError: (message: string, code: Number, e: any) => {
            console.error("Pusher error", message, code, e);
            setError("Connection lost. Reconnecting...");
          },
          onEvent: (event: {
            channelName: string;
            eventName: string;
            data: string;
          }) => {
            if (event.channelName !== channelName) return;

            if (event.eventName === "device_paired") {
              console.log("MetaQuest device paired!");
              setQuestPaired(true);
              setSafeModeState("ready");
              setError(null);
            } else if (event.eventName === "safe_mode_enabled") {
              const data = JSON.parse(event.data) as { id?: string };
              const incomingSessionId = data?.id ?? null;
              setSessionIdState(incomingSessionId);
              setSessionId(incomingSessionId); // Persist to storage
              setSafeModeState("sharing");
              setError(null);

              // Start location sharing
              startBackgroundLocation()
                .then(() => {
                  console.log("Background location started successfully");
                  if (incomingSessionId) {
                    // Send immediate location update
                    sendImmediateLocationUpdate(pairingId, incomingSessionId);
                    // Acknowledge SafeMode activation
                    sendSafeModeAck(
                      pairingId,
                      "mobile_safe_mode_ready",
                      incomingSessionId
                    );
                  }
                })
                .catch((e) => {
                  console.error("Failed to start background location", e);
                  setError("Couldn't start location sharing");
                  setSafeModeState("ready");
                });
            } else if (event.eventName === "safe_mode_disabled") {
              setSafeModeState("ready");
              const previousSessionId = sessionIdRef.current ?? undefined;
              setSessionIdState(null);
              setSessionId(null); // Clear from storage

              // Check if task is running before stopping
              Location.hasStartedLocationUpdatesAsync(LOCATION_TASK_NAME)
                .then((isRunning) => {
                  if (isRunning) {
                    return Location.stopLocationUpdatesAsync(LOCATION_TASK_NAME);
                  } else {
                    console.log("Location task was not running");
                  }
                })
                .catch((e) => {
                  console.warn("Failed to stop background location", e);
                });

              sendSafeModeAck(
                pairingId,
                "mobile_safe_mode_disabled",
                previousSessionId
              );
            }
          },
          onSubscriptionSucceeded: (subscribedChannel: string) => {
            if (subscribedChannel === channelName) {
              console.log("Pusher subscription succeeded - now ready to receive events");
              setConnecting(false);
              setFullyReady(true); // NOW we're ready to show QR and receive events
              // Keep in "idle" state until MetaQuest scans the QR code
            }
          },
          onSubscriptionError: (
            subscribedChannel: string,
            message: string,
            error: unknown
          ) => {
            console.error(
              "Subscription error",
              subscribedChannel,
              message,
              error
            );
            setError("Connection problem. Please restart the app.");
            setConnecting(false);
          },
        });

        await pusher.connect();
        await pusher.subscribe({ channelName });
      } catch (e) {
        console.error("Pusher initialization error", e);
        setError("Couldn't connect. Check your internet.");
        setConnecting(false);
      }
    };

    init();

    return () => {
      const cleanup = async () => {
        try {
          await pusher.unsubscribe({ channelName });
          await pusher.disconnect();
        } catch (e) {
          console.warn("Cleanup error", e);
        }
      };
      cleanup();
    };
  }, [pairingId, backgroundTaskReady]);

  // Reconnection function with retry logic
  const attemptReconnection = async () => {
    if (!pusherRef.current || !channelNameRef.current || !pairingId) {
      console.log("Cannot reconnect: missing refs");
      return;
    }

    const maxAttempts = 5;
    const attempt = reconnectAttempts.current;

    if (attempt >= maxAttempts) {
      console.error("Max reconnection attempts reached");
      setError("Can't reconnect. Please restart the app.");
      return;
    }

    console.log(`Reconnection attempt ${attempt + 1}/${maxAttempts}`);
    setError("Reconnecting...");

    try {
      const pusher = pusherRef.current;
      const channelName = channelNameRef.current;

      // Try to disconnect first to clean up
      try {
        await pusher.disconnect();
      } catch (e) {
        // Ignore disconnect errors
      }

      // Wait a bit before reconnecting
      await new Promise(resolve => setTimeout(resolve, 1000 * Math.min(attempt + 1, 3)));

      // Reconnect
      await pusher.connect();
      await pusher.subscribe({ channelName });

      console.log("Pusher reconnected successfully");
      setError(null);
      setFullyReady(true);
      reconnectAttempts.current = 0; // Reset on success
    } catch (e) {
      console.error("Reconnection failed", e);
      reconnectAttempts.current += 1;

      // Schedule retry
      if (reconnectTimeout.current) {
        clearTimeout(reconnectTimeout.current);
      }
      reconnectTimeout.current = setTimeout(() => {
        attemptReconnection();
      }, 3000);
    }
  };

  // Handle app state changes for reconnection
  useEffect(() => {
    const subscription = AppState.addEventListener("change", (nextAppState) => {
      console.log(`AppState changed to: ${nextAppState}`);

      if (nextAppState === "active") {
        // App came to foreground - check if we need to reconnect
        if (pusherRef.current && channelNameRef.current && pairingId) {
          console.log("App became active - attempting reconnection");
          reconnectAttempts.current = 0; // Reset attempts
          attemptReconnection();
        }
      } else if (nextAppState === "background" || nextAppState === "inactive") {
        // App going to background - clear any pending reconnect timers
        if (reconnectTimeout.current) {
          clearTimeout(reconnectTimeout.current);
          reconnectTimeout.current = null;
        }
      }
    });

    return () => {
      subscription.remove();
      if (reconnectTimeout.current) {
        clearTimeout(reconnectTimeout.current);
      }
    };
  }, [pairingId]);

  // Manual stop function
  const handleStopSharing = async () => {
    try {
      setSafeModeState("ready");
      const previousSessionId = sessionIdRef.current ?? undefined;
      setSessionIdState(null);
      setSessionId(null); // Clear from storage

      // Check if task is running before trying to stop it
      const isRunning = await Location.hasStartedLocationUpdatesAsync(LOCATION_TASK_NAME);
      if (isRunning) {
        await Location.stopLocationUpdatesAsync(LOCATION_TASK_NAME);
        console.log("Location sharing stopped by user");
      } else {
        console.log("Location task was not running");
      }

      // Notify the MetaQuest app that we stopped
      if (pairingId && previousSessionId) {
        sendSafeModeAck(pairingId, "mobile_safe_mode_disabled", previousSessionId);
      }
    } catch (e) {
      console.error("Failed to stop location sharing", e);
      setError("Couldn't stop sharing. Try again.");
    }
  };

  let statusText: string;
  let statusEmoji: string = "";
  if (safeModeState === "idle") {
    if (questPaired) {
      statusText = "MetaQuest Paired";
      statusEmoji = "✓";
    } else {
      statusText = "Waiting for headset...";
      statusEmoji = "";
    }
  } else if (safeModeState === "ready") {
    statusText = "Ready for emergency";
    statusEmoji = "✓";
  } else {
    statusText = "Location sharing active";
    statusEmoji = "📍";
  }

  // Don't render until pairing ID is loaded
  if (!pairingId) {
    return (
      <View style={styles.container}>
        <ActivityIndicator size="large" color="#111827" />
        <Text style={styles.loadingText}>Loading...</Text>
      </View>
    );
  }

  // Don't show QR code until fully ready to receive events
  if (!fullyReady) {
    return (
      <View style={styles.container}>
        <View style={styles.card}>
          <Text style={styles.title}>Safe Walk</Text>
          <View style={styles.row}>
            <ActivityIndicator size="small" color="#111827" />
            <Text style={styles.statusText}>
              {connecting ? "Connecting..." : "Getting ready..."}
            </Text>
          </View>
          {error && <Text style={styles.errorText}>{error}</Text>}
        </View>
      </View>
    );
  }

  return (
    <View style={styles.container}>
      <View style={styles.card}>
        <Text style={styles.title}>Safe Walk</Text>
        <Text style={styles.subtitle}>
          Scan this code with your headset to connect
        </Text>

        <View style={styles.qrContainer}>
          <QRCode
            value={JSON.stringify({
              type: "safewalk_pairing",
              device: "android",
              pairingId,
            })}
            size={200}
          />
        </View>

        <Text style={styles.pairingIdLabel}>Pairing ID</Text>
        <Text style={styles.pairingId}>{pairingId}</Text>

        {!connecting && (
          <View style={styles.statusContainer}>
            {statusEmoji && <Text style={styles.statusEmoji}>{statusEmoji}</Text>}
            <Text style={[
              styles.statusText,
              questPaired && safeModeState === "idle" && styles.pairedText,
              safeModeState === "ready" && styles.readyText
            ]}>
              {statusText}
            </Text>
          </View>
        )}

        {sessionId && (
          <Text style={styles.sessionText}>Session: {sessionId}</Text>
        )}

        {error && <Text style={styles.errorText}>{error}</Text>}

        {safeModeState === "sharing" && (
          <>
            <View style={styles.sharingContainer}>
              <Animated.View
                style={[styles.pulseDot, { transform: [{ scale: pulse }] }]}
              />
              <Text style={styles.sharingText}>Location Active</Text>
            </View>
            <Pressable
              style={styles.stopButton}
              onPress={handleStopSharing}
            >
              <Text style={styles.stopButtonText}>Stop Sharing</Text>
            </Pressable>
          </>
        )}
      </View>
    </View>
  );
}

async function startBackgroundLocation() {
  // Request foreground permission first (required on Android)
  const foregroundStatus = await Location.requestForegroundPermissionsAsync();
  if (foregroundStatus.status !== "granted") {
    throw new Error("Foreground location permission not granted");
  }

  // Then request background permission
  const backgroundStatus = await Location.requestBackgroundPermissionsAsync();
  if (backgroundStatus.status !== "granted") {
    throw new Error("Background location permission not granted");
  }

  // Check if already running
  const isRegistered = await Location.hasStartedLocationUpdatesAsync(
    LOCATION_TASK_NAME
  );
  if (isRegistered) {
    console.log("Background location already running");
    return;
  }

  // Start background location updates
  await Location.startLocationUpdatesAsync(LOCATION_TASK_NAME, {
    accuracy: Location.Accuracy.High,
    timeInterval: 15000, // 15 seconds
    distanceInterval: 0, // Update regardless of distance
    foregroundService: {
      notificationTitle: "Safe Walk Active",
      notificationBody: "Sharing location for emergency - tap to open",
      notificationColor: "#dc2626",
    },
    pausesUpdatesAutomatically: false, // Keep running even when stationary
    deferredUpdatesInterval: 15000,
    deferredUpdatesDistance: 0,
  });
  console.log("Background location tracking started");
}

function sendImmediateLocationUpdate(pairingId: string, sessionId: string) {
  if (!pairingId || !sessionId) return;

  Location.getCurrentPositionAsync({
    accuracy: Location.Accuracy.High,
  })
    .then((position) =>
      publishLocationUpdate({
        pairingId,
        sessionId,
        latitude: position.coords.latitude,
        longitude: position.coords.longitude,
        accuracy: position.coords.accuracy,
        timestamp: position.timestamp ?? Date.now(),
      })
    )
    .catch((error) => {
      console.warn("Failed to send immediate location update", error);
    });
}

function sendSafeModeAck(
  pairingId: string,
  eventName: string,
  sessionId?: string
) {
  if (!pairingId) return;

  notifySafeModeLifecycle(pairingId, eventName, {
    sessionId,
  }).catch((error) => {
    console.warn(`Failed to notify ${eventName}`, error);
  });
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: "#ffffff",
    alignItems: "center",
    justifyContent: "center",
    padding: 24,
  },
  loadingText: {
    marginTop: 12,
    fontSize: 14,
    color: "#6b7280",
  },
  card: {
    width: "100%",
    borderRadius: 24,
    backgroundColor: "#ffffff",
    padding: 24,
    shadowColor: "#000",
    shadowOffset: { width: 0, height: 10 },
    shadowOpacity: 0.1,
    shadowRadius: 20,
    elevation: 6,
    alignItems: "center",
  },
  title: {
    fontSize: 22,
    fontWeight: "700",
    color: "#111827",
    marginBottom: 4,
    textAlign: "center",
  },
  subtitle: {
    fontSize: 14,
    color: "#4b5563",
    textAlign: "center",
    marginBottom: 20,
  },
  qrContainer: {
    padding: 16,
    borderRadius: 16,
    backgroundColor: "#f3f4f6",
    marginBottom: 16,
  },
  pairingIdLabel: {
    fontSize: 12,
    color: "#6b7280",
  },
  pairingId: {
    fontSize: 16,
    fontWeight: "600",
    color: "#111827",
    marginBottom: 12,
  },
  row: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
    marginTop: 8,
  },
  statusContainer: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: 6,
    marginTop: 12,
    paddingVertical: 8,
    paddingHorizontal: 16,
    borderRadius: 8,
    backgroundColor: "#f9fafb",
  },
  statusEmoji: {
    fontSize: 16,
  },
  statusText: {
    fontSize: 14,
    color: "#4b5563",
    textAlign: "center",
    fontWeight: "500",
  },
  pairedText: {
    color: "#059669",
    fontWeight: "600",
  },
  readyText: {
    color: "#2563eb",
    fontWeight: "600",
  },
  sessionText: {
    fontSize: 12,
    color: "#6b7280",
    marginTop: 4,
  },
  errorText: {
    fontSize: 12,
    color: "#b91c1c",
    marginTop: 8,
    textAlign: "center",
  },
  sharingContainer: {
    marginTop: 16,
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
  },
  pulseDot: {
    width: 14,
    height: 14,
    borderRadius: 7,
    backgroundColor: "#22c55e",
  },
  sharingText: {
    fontSize: 14,
    fontWeight: "600",
    color: "#15803d",
  },
  stopButton: {
    marginTop: 16,
    backgroundColor: "#dc2626",
    paddingVertical: 12,
    paddingHorizontal: 32,
    borderRadius: 12,
    shadowColor: "#000",
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.1,
    shadowRadius: 4,
    elevation: 3,
  },
  stopButtonText: {
    fontSize: 15,
    fontWeight: "700",
    color: "#ffffff",
    textAlign: "center",
  },
});
