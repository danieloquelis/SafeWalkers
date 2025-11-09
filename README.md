GPS Streaming Setup Guide (Android)
1. Phone Requirements - Your phone and the device running Unity (Pc/Quest) must be on the SAME
Wi-Fi network. - The phone will SEND GPS data via UDP to Unity/Quest.
2. Recommended Android App App: gpsdRelay 
Setup in gpsdRelay: - Open app - Permission request: allow location - Mode: UDP - Destination IP: IP
address of your Mac or Quest (example: 192.168.x.x) - Destination Port: 11123 - Press “Start”
Finding your DEVICE IP: - For example on Mac: run in Terminal: ifconfig | grep "inet " | grep -v 127.0.0.1 - On Quest:
Settings -> Wi-Fi -> Connected Network -> Advanced -> IP
3. Recommended iPhone App App: GPS2IP
iPhone Setup: - same parameters as for android

