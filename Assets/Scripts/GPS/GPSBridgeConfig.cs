namespace GPSBridgeHeadless
{
    
    /// System receives real-time GPS data from a phone app (gpsdrelay -for android)
    /// over UDP and makes it available to unity scripts
    /// Configuration: Unity LISTENS, Phone SENDS
    /// Phone app should send UDP packets TO Pc's/Quest IP address
    /// NmeaClient receives raw NMEA sentences
    /// NmeaParser converts them to usable GPS data
    

    public static class GpsBridgeConfig
    {
        // This should be your PC's/ Quest IP address (not phone's IP!), device running Unity app!
        // Find it by running in Terminal: ifconfig | grep "inet " | grep -v 127.0.0.1, or look in Quest settings- Wi-Fi- connected network- advanced
        // Important must be on same Wi-Fi network as phone
        
        public const string DEVICE_IP = "192.168.x.xx"; 
        
        // Port that Unity will listen on
        public const int PORT = 11123;
        
        // Use UDP protocol
        public const bool USE_UDP = true;
        
        // Unity LISTENS for incoming UDP packets
        public const bool UDP_LISTEN_MODE = true;
        
        // Autoconnect when NmeaClient starts
        public const bool AUTO_CONNECT = true;
        
        // Safety
        public const int MAX_LINE_LENGTH = 512;
        
        // Optional smoothing for lat/lon (0..0.95)
        public const float SMOOTHING = 0.2f;
    }
}