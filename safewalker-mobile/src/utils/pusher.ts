import { hmac } from "@noble/hashes/hmac";
import { sha256 } from "@noble/hashes/sha256";
import { bytesToHex, utf8ToBytes } from "@noble/hashes/utils";
import * as Crypto from "expo-crypto";

import {
  PUSHER_APP_ID,
  PUSHER_CLUSTER,
  PUSHER_KEY,
  PUSHER_SECRET,
  assertPusherConfig,
} from "@/constants/env";

export const PAIRING_CHANNEL_PREFIX = "safewalk-mobile-";
export const SESSION_CHANNEL_PREFIX = "safewalk-session-";

export type SafeWalkLocationPayload = {
  pairingId: string;
  sessionId: string;
  latitude: number;
  longitude: number;
  accuracy?: number | null;
  timestamp: number;
};

type TriggerEventParams = {
  channels: string[];
  eventName: string;
  payload: Record<string, unknown>;
};

export function getPairingChannelName(pairingId: string) {
  return `${PAIRING_CHANNEL_PREFIX}${pairingId}`;
}

export function getSessionChannelName(sessionId: string) {
  return `${SESSION_CHANNEL_PREFIX}${sessionId}`;
}

export async function triggerPusherEvent({
  channels,
  eventName,
  payload,
}: TriggerEventParams) {
  assertPusherConfig();

  if (!channels.length) {
    throw new Error("triggerPusherEvent requires at least one channel.");
  }

  const body = JSON.stringify({
    name: eventName,
    channels,
    data: JSON.stringify(payload ?? {}),
  });

  const bodyMd5 = await Crypto.digestStringAsync(
    Crypto.CryptoDigestAlgorithm.MD5,
    body
  );

  const timestamp = Math.floor(Date.now() / 1000).toString();
  const params = new URLSearchParams({
    auth_key: PUSHER_KEY,
    auth_timestamp: timestamp,
    auth_version: "1.0",
    body_md5: bodyMd5,
  });

  const stringToSign = `POST\n/apps/${PUSHER_APP_ID}/events\n${params.toString()}`;
  const signature = bytesToHex(
    hmac(sha256, utf8ToBytes(PUSHER_SECRET), utf8ToBytes(stringToSign))
  );
  params.append("auth_signature", signature);

  const url = `https://api-${PUSHER_CLUSTER}.pusher.com/apps/${PUSHER_APP_ID}/events?${params.toString()}`;
  const response = await fetch(url, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body,
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(
      `Failed to trigger Pusher event (${eventName}): ${response.status} ${response.statusText} – ${text}`
    );
  }
}

export async function publishLocationUpdate(
  payload: SafeWalkLocationPayload
): Promise<void> {
  if (!payload.pairingId || !payload.sessionId) {
    throw new Error("Location payload requires pairingId and sessionId.");
  }

  const channels = [
    getPairingChannelName(payload.pairingId),
    getSessionChannelName(payload.sessionId),
  ];

  await triggerPusherEvent({
    channels,
    eventName: "mobile_location_update",
    payload,
  });
}

export async function notifySafeModeLifecycle(
  pairingId: string,
  eventName: string,
  extraPayload: Record<string, unknown> = {}
) {
  if (!pairingId) {
    throw new Error("notifySafeModeLifecycle requires pairingId");
  }

  await triggerPusherEvent({
    channels: [getPairingChannelName(pairingId)],
    eventName,
    payload: {
      pairingId,
      timestamp: Date.now(),
      ...extraPayload,
    },
  });
}
