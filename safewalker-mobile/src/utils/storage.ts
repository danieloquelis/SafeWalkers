import AsyncStorage from "@react-native-async-storage/async-storage";

const PAIRING_ID_KEY = "safewalk_pairing_id";
const SESSION_ID_KEY = "safewalk_session_id";

export async function getPairingId(): Promise<string> {
  try {
    let pairingId = await AsyncStorage.getItem(PAIRING_ID_KEY);
    if (!pairingId) {
      pairingId = `device_${Math.random().toString(36).slice(2, 10)}`;
      await AsyncStorage.setItem(PAIRING_ID_KEY, pairingId);
    }
    return pairingId;
  } catch (e) {
    console.error("Failed to get/set pairing ID", e);
    return `device_${Math.random().toString(36).slice(2, 10)}`;
  }
}

export async function getSessionId(): Promise<string | null> {
  try {
    return await AsyncStorage.getItem(SESSION_ID_KEY);
  } catch (e) {
    console.error("Failed to get session ID", e);
    return null;
  }
}

export async function setSessionId(sessionId: string | null): Promise<void> {
  try {
    if (sessionId) {
      await AsyncStorage.setItem(SESSION_ID_KEY, sessionId);
    } else {
      await AsyncStorage.removeItem(SESSION_ID_KEY);
    }
  } catch (e) {
    console.error("Failed to set session ID", e);
  }
}
