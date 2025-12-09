import { ExpoConfig } from "expo/config";

const pusherEnv = {
  appId: process.env.EXPO_PUBLIC_PUSHER_APP_ID ?? "",
  key: process.env.EXPO_PUBLIC_PUSHER_KEY ?? "",
  secret: process.env.EXPO_PUBLIC_PUSHER_SECRET ?? "",
  cluster: process.env.EXPO_PUBLIC_PUSHER_CLUSTER ?? "",
};

const config: ExpoConfig = {
  name: "SafeWalk",
  slug: "SafeWalk",
  version: "1.0.0",
  orientation: "portrait",
  icon: "./assets/images/icon.png",
  scheme: "safewalkermobile",
  userInterfaceStyle: "automatic",
  newArchEnabled: true,
  platforms: ["ios", "android"],
  ios: {
    bundleIdentifier: "com.safewalk",
    supportsTablet: true,
  },
  android: {
    package: "com.safewalk",
    edgeToEdgeEnabled: true,
    predictiveBackGestureEnabled: false,
    permissions: [
      "ACCESS_FINE_LOCATION",
      "ACCESS_COARSE_LOCATION",
      "ACCESS_BACKGROUND_LOCATION",
      "INTERNET",
      "FOREGROUND_SERVICE",
      "FOREGROUND_SERVICE_LOCATION",
      "WAKE_LOCK",
      "REQUEST_IGNORE_BATTERY_OPTIMIZATIONS",
      "ACCESS_NETWORK_STATE",
    ],
  },
  plugins: [
    "expo-router",
    "expo-dev-client",
    [
      "expo-splash-screen",
      {
        image: "./assets/images/splash.png",
        imageWidth: 200,
        resizeMode: "contain",
        backgroundColor: "#ffffff",
      },
    ],
  ],
  experiments: {
    typedRoutes: true,
    reactCompiler: true,
    tsconfigPaths: true,
  },
  extra: {
    pusher: pusherEnv,
    eas: {
      projectId: "a7cf91f8-49b8-41c3-880c-6c8c69928e53",
    },
  },
};

export default config;
