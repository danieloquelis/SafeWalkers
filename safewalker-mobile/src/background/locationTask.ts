import * as TaskManager from "expo-task-manager";

import { getSessionId } from "@/utils/storage";

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
    const sessionId = await getSessionId();

    if (!sessionId) {
      console.warn("No session ID found - location update skipped");
      return;
    }

    const locationData = {
      sessionId,
      latitude: latest.coords.latitude,
      longitude: latest.coords.longitude,
      accuracy: latest.coords.accuracy,
      timestamp: latest.timestamp,
    };

    console.log("Background location update", locationData);

    // TODO: Send location data to your backend API
    // Example:
    // try {
    //   await fetch('https://your-backend.com/api/location', {
    //     method: 'POST',
    //     headers: { 'Content-Type': 'application/json' },
    //     body: JSON.stringify(locationData),
    //   });
    // } catch (e) {
    //   console.error('Failed to send location', e);
    // }
  });

  taskDefined = true;
}
