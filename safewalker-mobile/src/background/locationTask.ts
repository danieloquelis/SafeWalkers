import * as TaskManager from "expo-task-manager";

import { publishLocationUpdate } from "@/utils/pusher";
import { getPairingId, getSessionId } from "@/utils/storage";

export const LOCATION_TASK_NAME = "safewalk-background-location";

let taskDefined = false;

export function ensureBackgroundLocationTask() {
  if (taskDefined) return;

  TaskManager.defineTask(LOCATION_TASK_NAME, async ({ data, error }) => {
    if (error) {
      console.error("Background location task error", error);
      return;
    }

    const { locations } = (data as { locations?: any[] }) ?? {};
    const latest = locations?.[0];

    if (!latest) return;

    // Get the current session ID from storage
    const [sessionId, pairingId] = await Promise.all([
      getSessionId(),
      getPairingId(),
    ]);

    if (!sessionId || !pairingId) {
      console.warn("Missing session or pairing ID - location update skipped");
      return;
    }

    const locationData = {
      pairingId,
      sessionId,
      latitude: latest.coords.latitude,
      longitude: latest.coords.longitude,
      accuracy: latest.coords.accuracy,
      timestamp: latest.timestamp ?? Date.now(),
    };

    try {
      console.log("Background location update", locationData);
      await publishLocationUpdate(locationData);
    } catch (publishError) {
      console.error(
        "Failed to publish background location update",
        publishError
      );
    }
  });

  taskDefined = true;
}
