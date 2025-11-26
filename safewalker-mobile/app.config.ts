import { ExpoConfig } from "expo/config";

const pusherEnv = {
  appId: process.env.EXPO_PUBLIC_PUSHER_APP_ID ?? "",
  key: process.env.EXPO_PUBLIC_PUSHER_KEY ?? "",
  secret: process.env.EXPO_PUBLIC_PUSHER_SECRET ?? "",
  cluster: process.env.EXPO_PUBLIC_PUSHER_CLUSTER ?? "",
};

const config: ExpoConfig = {
  name: "safewalker-mobile",
  slug: "safewalker-mobile",
  version: "1.0.0",
  orientation: "portrait",
  icon: "./assets/images/icon.png",
  scheme: "safewalkermobile",
  userInterfaceStyle: "automatic",
  newArchEnabled: true,
  platforms: ["ios", "android"],
  ios: {
    bundleIdentifier: "com.safewalkers",
    supportsTablet: true,
  },
  android: {
    package: "com.safewalkers",
    adaptiveIcon: {
      backgroundColor: "#E6F4FE",
      foregroundImage: "./assets/images/android-icon-foreground.png",
      backgroundImage: "./assets/images/android-icon-background.png",
      monochromeImage: "./assets/images/android-icon-monochrome.png",
    },
    edgeToEdgeEnabled: true,
    predictiveBackGestureEnabled: false,
    permissions: [
      "ACCESS_FINE_LOCATION",
      "ACCESS_COARSE_LOCATION",
      "ACCESS_BACKGROUND_LOCATION",
      "INTERNET",
      "FOREGROUND_SERVICE",
      "FOREGROUND_SERVICE_LOCATION",
    ],
  },
  plugins: [
    "expo-router",
    "expo-dev-client",
    [
      "expo-splash-screen",
      {
        image: "./assets/images/splash-icon.png",
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
  },
};

export default config;
