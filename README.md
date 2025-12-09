![SafeWalk Logo](Media/Portrait.png)

# SafeWalk

**SafeWalk** is a Mixed Reality (MR) personal safety application designed for Smart Glasses (Vision) and Meta Quest Headsets. When you feel unsafe while walking alone, SafeWalk discreetly activates "Safe Mode" through hand gestures or voice commands, alerting your trusted contacts with your real-time location and live video streaming.

---

## Features

### Emergency Hand Gestures

Configure a discreet hand gesture (Phone, Help, or Thumbs Down) to trigger Safe Mode silently without drawing attention.

### Wake Word Detection

Train a custom voice command that activates Safe Mode hands-free using speech recognition.

### Real-Time GPS Tracking

Once Safe Mode is enabled, your location is continuously shared with your emergency contacts through the mobile companion app.

### Live Video Streaming

Trusted contacts can join a live video call from the headset's passthrough camera, allowing them to see your surroundings in real-time.

### SMS Emergency Alerts

Automatic SMS notifications are sent to your configured emergency contacts with a link to track your location and join the video call.

### Safe Place Finder

AI-powered suggestions for nearby safe locations (hospitals, police stations, shopping malls, or public spaces) based on your current position.

### AI Agent (Ghost Character)

An animated assistant character that provides guidance and support during emergency situations.

---

## How It Works

1. **Setup**: Configure your emergency contacts and choose your preferred activation method (gesture or wake word)
2. **Pair**: Connect your Meta Quest headset with your Android phone using the companion mobile app
3. **Walk**: Go about your day with the headset in passthrough mode
4. **Activate**: If you feel unsafe, perform your configured gesture or say your wake word
5. **Alert**: Your contacts receive an SMS with a link to view your live location and video feed

---

## Installation

### Meta Quest App

1. Download the Meta Quest app from:
   **[https://www.meta.com/s/2XVNtclD5](https://www.meta.com/s/2XVNtclD5)**

2. Install the app on your Meta Quest headset

### Android Companion App

1. Download the Android companion app from:
   **[SafeWalk Mobile APK](https://expo.dev/accounts/danieloquelis/projects/SafeWalk/builds/139ecbb2-24e0-435c-b3c7-de428bbe88dd)**

2. Enable "Install from unknown sources" on your Android device
3. Install the APK file

---

## Pairing Instructions

### Method 1: QR Code Pairing (Experimental)

1. Open the SafeWalk mobile app on your Android phone
2. A QR code will be displayed on the screen
3. In the Meta Quest app, point your headset camera at the QR code
4. Wait for the confirmation beep sound

> **Note**: QR code scanning is an experimental feature from Meta SDK 81 (MRUK). The Meta Quest Store version may not fully support experimental features. If QR code scanning doesn't work, please use the manual pairing method below.

### Method 2: Manual Pairing (Recommended)

If QR code scanning fails or is unavailable:

1. Open the SafeWalk mobile app and note the **Pairing ID** displayed below the QR code
2. In the Meta Quest app, navigate to the Setup menu
3. Select "Manual Pairing"
4. Enter the Pairing ID exactly as shown on your phone
5. Tap "Pair Device" and wait for confirmation

---

## Architecture

![SafeWalk Architecture](Media/Architecture.png)

The system consists of three main components:

- **Meta Quest App**: The main MR application running on the headset, handling gesture detection, video streaming via Agora, and safe place calculations
- **Mobile Companion App**: Android app that streams GPS location data via Pusher and displays the QR code for pairing
- **Web App**: Browser-based dashboard for trusted contacts to view real-time location on a map and join video calls

---

This Software is intellectual property owned by its developers and is **NOT open source**. Unauthorized copying, distribution, or use of this Software may result in legal action.

For licensing inquiries, please contact the development team.
