import { Stack } from "expo-router";
import { StatusBar } from "expo-status-bar";
import "react-native-reanimated";

export default function RootLayout() {
  return (
    <>
      <Stack
        screenOptions={{
          headerTitle: "Safe Walk",
          headerTitleAlign: "center",
          headerShadowVisible: false,
        }}
      />
      <StatusBar style="dark" />
    </>
  );
}
