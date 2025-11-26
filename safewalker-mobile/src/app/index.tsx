import { Pusher } from "@pusher/pusher-websocket-react-native";
import React, { useEffect, useMemo, useRef, useState } from "react";
import {
  ActivityIndicator,
  Animated,
  Platform,
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
  const sessionIdRef = useRef<string | null>(null);

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

  useEffect(() => {
    const requestPermissions = async () => {
      try {
        const fg = await Location.requestForegroundPermissionsAsync();
        if (fg.status !== "granted") {
          console.warn("Foreground location permission not granted");
          return;
        }

        const bg = await Location.requestBackgroundPermissionsAsync();
        if (bg.status !== "granted") {
          console.warn("Background location permission not granted");
        }
      } catch (permError) {
        console.error("Failed to request location permissions", permError);
      }
    };

    if (Platform.OS === "android") {
      requestPermissions();
    }
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

  useEffect(() => {
    ensureBackgroundLocationTask();

    if (!pairingId) {
      // Pairing ID not loaded yet
      return;
    }

    try {
      assertPusherConfig();
    } catch (configError) {
      setError(
        configError instanceof Error
          ? configError.message
          : "Missing Pusher configuration."
      );
      setConnecting(false);
      return;
    }

    const pusher = Pusher.getInstance();
    const channelName = getPairingChannelName(pairingId);

    const init = async () => {
      try {
        await pusher.init({
          apiKey: PUSHER_KEY,
          cluster: PUSHER_CLUSTER,
          onConnectionStateChange: (currentState: string) => {
            console.log(`Pusher connection state: ${currentState}`);
          },
          onError: (message: string, code: Number, e: any) => {
            console.error("Pusher error", message, code, e);
            setError("Connection issue. Waiting to reconnect...");
          },
          onEvent: (event: {
            channelName: string;
            eventName: string;
            data: string;
          }) => {
            if (event.channelName !== channelName) return;

            if (event.eventName === "safe_mode_enabled") {
              const data = JSON.parse(event.data) as { id?: string };
              const incomingSessionId = data?.id ?? null;
              setSessionIdState(incomingSessionId);
              setSessionId(incomingSessionId); // Persist to storage
              setSafeModeState("sharing");

              startBackgroundLocation().catch((e) => {
                console.error("Failed to start background location", e);
                setError("Unable to start background location updates.");
                setSafeModeState("ready");
              });

              if (incomingSessionId) {
                sendImmediateLocationUpdate(pairingId, incomingSessionId);
                sendSafeModeAck(
                  pairingId,
                  "mobile_safe_mode_ready",
                  incomingSessionId
                );
              }
            } else if (event.eventName === "safe_mode_disabled") {
              setSafeModeState("ready");
              const previousSessionId = sessionIdRef.current ?? undefined;
              setSessionIdState(null);
              setSessionId(null); // Clear from storage
              Location.stopLocationUpdatesAsync(LOCATION_TASK_NAME).catch(
                (e) => {
                  console.warn("Failed to stop background location", e);
                }
              );
              sendSafeModeAck(
                pairingId,
                "mobile_safe_mode_disabled",
                previousSessionId
              );
            }
          },
          onSubscriptionSucceeded: (subscribedChannel: string) => {
            if (subscribedChannel === channelName) {
              setConnecting(false);
              setSafeModeState("ready");
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
            setError("Failed to subscribe to pairing channel.");
            setConnecting(false);
          },
        });

        await pusher.connect();
        await pusher.subscribe({ channelName });
      } catch (e) {
        console.error("Pusher initialization error", e);
        setError("Failed to initialize connection.");
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
  }, [pairingId]);

  let statusText: string;
  if (safeModeState === "idle") {
    statusText = "Waiting for Meta Quest to connect...";
  } else if (safeModeState === "ready") {
    statusText = "Paired successfully and ready.";
  } else {
    statusText = "Safe Mode enabled – sharing location.";
  }

  // Don't render until pairing ID is loaded
  if (!pairingId) {
    return (
      <View style={styles.container}>
        <ActivityIndicator size="large" color="#111827" />
      </View>
    );
  }

  return (
    <View style={styles.container}>
      <View style={styles.card}>
        <Text style={styles.title}>Pair with Meta Quest</Text>
        <Text style={styles.subtitle}>
          Scan this QR code from your Meta Quest app to pair.
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

        {connecting && (
          <View style={styles.row}>
            <ActivityIndicator size="small" color="#111827" />
            <Text style={styles.statusText}>Connecting to Safe Walk...</Text>
          </View>
        )}

        {!connecting && <Text style={styles.statusText}>{statusText}</Text>}

        {sessionId && (
          <Text style={styles.sessionText}>Session ID: {sessionId}</Text>
        )}

        {error && <Text style={styles.errorText}>{error}</Text>}

        {safeModeState === "sharing" && (
          <View style={styles.sharingContainer}>
            <Animated.View
              style={[styles.pulseDot, { transform: [{ scale: pulse }] }]}
            />
            <Text style={styles.sharingText}>Sharing Location</Text>
          </View>
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
      notificationTitle: "Safe Walk",
      notificationBody: "Sharing your location in the background",
    },
    pausesUpdatesAutomatically: false, // Keep running even when stationary
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
  statusText: {
    fontSize: 13,
    color: "#4b5563",
    marginTop: 8,
    textAlign: "center",
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
});
