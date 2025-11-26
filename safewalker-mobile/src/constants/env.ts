import Constants from "expo-constants";

type PusherExtra = {
  appId?: string;
  key?: string;
  secret?: string;
  cluster?: string;
};

function getExtra(): { pusher?: PusherExtra } {
  const expoConfig = Constants.expoConfig ?? null;
  if (expoConfig?.extra) {
    return expoConfig.extra as { pusher?: PusherExtra };
  }

  // Fallback for classic manifests (dev client / Expo Go)
  const manifestExtra = (Constants as Record<string, any>).manifest?.extra;
  if (manifestExtra) {
    return manifestExtra as { pusher?: PusherExtra };
  }

  return {};
}

const extra = getExtra();
const pusherExtra = extra?.pusher ?? {};

export const PUSHER_APP_ID =
  process.env.EXPO_PUBLIC_PUSHER_APP_ID ?? pusherExtra.appId ?? "";
export const PUSHER_KEY =
  process.env.EXPO_PUBLIC_PUSHER_KEY ?? pusherExtra.key ?? "";
export const PUSHER_SECRET =
  process.env.EXPO_PUBLIC_PUSHER_SECRET ?? pusherExtra.secret ?? "";
export const PUSHER_CLUSTER =
  process.env.EXPO_PUBLIC_PUSHER_CLUSTER ?? pusherExtra.cluster ?? "";

export function assertPusherConfig() {
  if (!PUSHER_APP_ID || !PUSHER_KEY || !PUSHER_SECRET || !PUSHER_CLUSTER) {
    throw new Error(
      "Missing Pusher configuration. Ensure app.config.ts extra.pusher or EXPO_PUBLIC_PUSHER_* env vars are set."
    );
  }
}
