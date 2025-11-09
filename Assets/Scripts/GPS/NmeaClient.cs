using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GPSBridgeHeadless
{
    /// Receives NMEA sentences from the phone over UDP
    /// No UI; reads settings from GpsBridgeConfig.

    public class NmeaClient : MonoBehaviour
    {
        public bool autoConnectOnStart = GpsBridgeConfig.AUTO_CONNECT;

        public event Action<string> OnNmeaSentence;
        public event Action<string> OnStatus;
        public event Action<string> OnError;

        CancellationTokenSource _cts;
        TcpClient _tcp;
        UdpClient _udp;
        IPEndPoint _udpRemote;

        void Start()
        {
            Debug.Log($"[GPS] Using IP {GpsBridgeConfig.PHONE_IP} : {GpsBridgeConfig.PORT}  " +
                      $"UDP={GpsBridgeConfig.USE_UDP} Listen={GpsBridgeConfig.UDP_LISTEN_MODE}");
            if (autoConnectOnStart) _ = ConnectAsync();
        }

        void OnDisable() => Disconnect();

        public async Task ConnectAsync()
        {
            Disconnect();
            _cts = new CancellationTokenSource();
            try
            {
                if (GpsBridgeConfig.USE_UDP)
                    await Task.Run(() => RunUdp(_cts.Token));
                else
                    await Task.Run(() => RunTcp(_cts.Token));
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"ConnectAsync exception: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            try { _cts?.Cancel(); } catch {}
            try { _tcp?.Close(); } catch {}
            try { _udp?.Close(); } catch {}
            _tcp = null; _udp = null; _cts = null;
        }

        void RunTcp(CancellationToken token)
        {
            try
            {
                // Force IPv4 explicitly (avoids odd IPv6 routing issues)
                if (!IPAddress.TryParse(GpsBridgeConfig.PHONE_IP, out var ip))
                {
                    OnError?.Invoke($"Invalid IP '{GpsBridgeConfig.PHONE_IP}'");
                    return;
                }
                var remoteEndPoint = new IPEndPoint(ip, GpsBridgeConfig.PORT);

                OnStatus?.Invoke($"TCP connecting to {remoteEndPoint} ...");

                // Create a raw Socket with IPv4 to guarantee address family
                using (var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    sock.NoDelay = true;
                    sock.SendTimeout = 4000;
                    sock.ReceiveTimeout = 4000;

                    sock.Connect(remoteEndPoint); // can throw SocketException "No route to host"
                    OnStatus?.Invoke("TCP connected.");

                    var sb = new StringBuilder();
                    var buffer = new byte[1024];

                    while (!token.IsCancellationRequested)
                    {
                        if (sock.Available == 0) { Thread.Sleep(2); continue; }
                        int n = sock.Receive(buffer);
                        if (n <= 0) break;

                        for (int i = 0; i < n; i++)
                        {
                            char c = (char)buffer[i];
                            if (c == '\n' || c == '\r')
                            {
                                if (sb.Length > 0)
                                {
                                    var line = sb.ToString().Trim();
                                    sb.Length = 0;
                                    if (line.StartsWith("$")) OnNmeaSentence?.Invoke(line);
                                }
                            }
                            else
                            {
                                if (sb.Length < GpsBridgeConfig.MAX_LINE_LENGTH) sb.Append(c);
                                else sb.Length = 0; // safety reset
                            }
                        }
                    }
                }
            }
            catch (SocketException se)
            {
                // Diagnostics
                OnError?.Invoke($"TCP error: {se.Message} (Code {(int)se.SocketErrorCode})");
                OnStatus?.Invoke("Hint: If terminal 'nc' works but Unity doesn't, try: " +
                                 "1) Ensure you closed all nc sessions; " +
                                 "2) Turn off VPN; " +
                                 "3) Use same Wi-Fi band; " +
                                 "4) Try UDP mode.");
            }
            catch (Exception e)
            {
                OnError?.Invoke($"TCP error: {e.Message}");
            }
            finally
            {
                OnStatus?.Invoke("TCP connection closed.");
            }
        }

        void RunUdp(CancellationToken token)
        {
            try
            {
                if (GpsBridgeConfig.UDP_LISTEN_MODE)
                {
                    _udp = new UdpClient(GpsBridgeConfig.PORT); // binds locally
                    OnStatus?.Invoke($"UDP listening on *:{GpsBridgeConfig.PORT} ...");
                    _udp.Client.ReceiveTimeout = 1000;

                    var sb = new StringBuilder();
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            var remote = new IPEndPoint(IPAddress.Any, 0);
                            var data = _udp.Receive(ref remote);
                            ParseBytesAsLines(data, data.Length, sb);
                        }
                        catch (SocketException se) when (se.SocketErrorCode == SocketError.TimedOut)
                        {
                            // loop to allow cancellation
                        }
                    }
                }
                else
                {
                    if (!IPAddress.TryParse(GpsBridgeConfig.PHONE_IP, out var ip))
                    {
                        OnError?.Invoke($"Invalid IP '{GpsBridgeConfig.PHONE_IP}'");
                        return;
                    }
                    _udpRemote = new IPEndPoint(ip, GpsBridgeConfig.PORT);
                    _udp = new UdpClient();
                    _udp.Connect(_udpRemote);
                    OnStatus?.Invoke($"UDP connected to {_udpRemote} (receiving) ...");
                    _udp.Client.ReceiveTimeout = 1000;

                    var sb = new StringBuilder();
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            var data = _udp.Receive(ref _udpRemote);
                            ParseBytesAsLines(data, data.Length, sb);
                        }
                        catch (SocketException se) when (se.SocketErrorCode == SocketError.TimedOut)
                        {
                            // loop
                        }
                    }
                }
            }
            catch (Exception e)
            {
                OnError?.Invoke($"UDP error: {e.Message}");
            }
            finally
            {
                OnStatus?.Invoke("UDP closed.");
            }
        }

        void ParseBytesAsLines(byte[] data, int len, StringBuilder sb)
        {
            for (int i = 0; i < len; i++)
            {
                char c = (char)data[i];
                if (c == '\n' || c == '\r')
                {
                    if (sb.Length > 0)
                    {
                        var line = sb.ToString().Trim();
                        sb.Length = 0;
                        if (line.StartsWith("$")) OnNmeaSentence?.Invoke(line);
                    }
                }
                else
                {
                    if (sb.Length < GpsBridgeConfig.MAX_LINE_LENGTH) sb.Append(c);
                    else sb.Length = 0;
                }
            }
        }
    }
}
