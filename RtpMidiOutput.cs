using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;
using Commons.Music.Midi;

public class RtpMidiPortDetails : IMidiPortDetails
{
    public string Id { get; }
    public string Manufacturer { get; }
    public string Name { get; }
    public string Version { get; }

    public RtpMidiPortDetails(string host, int port)
    {
        Id = $"rtp-{host}-{port}";
        Manufacturer = "RTP-MIDI";
        Name = $"RTP-MIDI: {host}:{port}";
        Version = "1.0";
    }
}

public class RtpMidiOutput : IMidiOutput
{
    private readonly string host;
    private readonly int port;
    private readonly UdpClient controlClient;
    private readonly UdpClient dataClient;
    private readonly uint initiatorToken;
    private readonly uint ssrc;
    private readonly IPEndPoint controlDestination;
    private readonly IPEndPoint dataDestination;
    private CancellationTokenSource? cts;
    private ushort sequenceNumber = 0;
    private int debugDumpCount = 0;
    private readonly object sendLock = new object();

    public IMidiPortDetails Details { get; }
    public MidiPortConnectionState Connection { get; private set; }

    private static readonly Stopwatch Stopwatch = Stopwatch.StartNew();

    private static void CreateBoundClients(out UdpClient control, out UdpClient data)
    {
        var rand = new Random();
        for (int i = 0; i < 100; i++)
        {
            int port = rand.Next(5000, 30000) * 2;
            UdpClient? c = null;
            UdpClient? d = null;
            try
            {
                c = new UdpClient(port);
                d = new UdpClient(port + 1);
                control = c;
                data = d;
                return;
            }
            catch
            {
                c?.Dispose();
                d?.Dispose();
            }
        }
        control = new UdpClient(0);
        data = new UdpClient(0);
    }

    public RtpMidiOutput(string host, int port)
    {
        this.host = host;
        this.port = port;
        Details = new RtpMidiPortDetails(host, port);
        Connection = MidiPortConnectionState.Closed;

        // Generate fully random 32-bit Token and SSRC
        var rand = new Random();
        var bytes = new byte[8];
        rand.NextBytes(bytes);
        initiatorToken = BitConverter.ToUInt32(bytes, 0);
        ssrc = BitConverter.ToUInt32(bytes, 4);

        // Bind consecutive UDP ports
        CreateBoundClients(out controlClient, out dataClient);

        // Print local ports assigned
        var controlLocalPort = ((IPEndPoint)controlClient.Client.LocalEndPoint!).Port;
        var dataLocalPort = ((IPEndPoint)dataClient.Client.LocalEndPoint!).Port;
        Console.WriteLine($"[RTP-MIDI] Local ports assigned: Control={controlLocalPort}, Data={dataLocalPort} (consecutive: {controlLocalPort + 1 == dataLocalPort})");

        var addresses = Dns.GetHostAddresses(host);
        var ip = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        if (ip == null)
        {
            throw new Exception($"[RTP-MIDI] Failed to resolve IPv4 address for host: {host}");
        }

        controlDestination = new IPEndPoint(ip, port);
        dataDestination = new IPEndPoint(ip, port + 1);

        // Try establish connection synchronously with timeout
        if (!EstablishSession())
        {
            controlClient.Close();
            dataClient.Close();
            throw new Exception($"[RTP-MIDI] Failed to connect to RTP-MIDI device at {host}:{port}");
        }

        Connection = MidiPortConnectionState.Open;

        // Start receive loops for clock sync
        StartReceiveLoops();
    }

    private static long GetMidiTimestamp()
    {
        // 100 microsecond units
        return (long)(Stopwatch.ElapsedTicks * 10000.0 / Stopwatch.Frequency);
    }

    private bool EstablishSession()
    {
        // Set sync receive timeout for handshake
        controlClient.Client.ReceiveTimeout = 500;
        dataClient.Client.ReceiveTimeout = 500;

        // Control Invitation Loop
        bool controlConnected = false;
        var inviteControl = CreateInvitationPacket(initiatorToken, ssrc, "mypmid");

        for (int i = 0; i < 5; i++)
        {
            try
            {
                controlClient.Send(inviteControl, inviteControl.Length, controlDestination);
                Console.WriteLine($"[RTP-MIDI] Sent IN to Control {controlDestination} (attempt {i + 1})");

                IPEndPoint? remoteEP = null;
                var buffer = controlClient.Receive(ref remoteEP);
                if (IsOkPacket(buffer, initiatorToken))
                {
                    controlConnected = true;
                    Console.WriteLine("[RTP-MIDI] Control session accepted!");
                    break;
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
            {
                // Timeout, retry
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RTP-MIDI] Error during Control connection: {ex.Message}");
            }

            if (controlConnected) break;
        }

        if (!controlConnected) return false;

        // Data Invitation Loop
        bool dataConnected = false;
        var inviteData = CreateInvitationPacket(initiatorToken, ssrc, "mypmid");

        for (int i = 0; i < 5; i++)
        {
            try
            {
                dataClient.Send(inviteData, inviteData.Length, dataDestination);
                Console.WriteLine($"[RTP-MIDI] Sent IN to Data {dataDestination} (attempt {i + 1})");

                IPEndPoint? remoteEP = null;
                var buffer = dataClient.Receive(ref remoteEP);
                if (IsOkPacket(buffer, initiatorToken))
                {
                    dataConnected = true;
                    Console.WriteLine("[RTP-MIDI] Data session accepted!");
                    break;
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
            {
                // Timeout, retry
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RTP-MIDI] Error during Data connection: {ex.Message}");
            }

            if (dataConnected) break;
        }

        // Reset timeouts for async receive loops
        controlClient.Client.ReceiveTimeout = 0;
        dataClient.Client.ReceiveTimeout = 0;

        return dataConnected;
    }

    private void SendClockSyncAnchor(UdpClient client, IPEndPoint destination)
    {
        byte[] packet = new byte[36];
        packet[0] = 0xFF;
        packet[1] = 0xFF;
        packet[2] = 0x43;
        packet[3] = 0x4B; // 'CK'
        
        WriteUInt32BigEndian(packet, 4, ssrc);
        packet[8] = 0; // count = 0

        long now = GetMidiTimestamp();
        WriteInt64BigEndian(packet, 12, now); // t1
        WriteInt64BigEndian(packet, 20, 0);   // t2
        WriteInt64BigEndian(packet, 28, 0);   // t3

        try
        {
            client.Send(packet, packet.Length, destination);
            Console.WriteLine($"[RTP-MIDI] Sent CK0 (clock sync anchor) to {destination}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RTP-MIDI] Failed to send CK0 to {destination}: {ex.Message}");
        }
    }

    private void StartReceiveLoops()
    {
        cts = new CancellationTokenSource();
        var token = cts.Token;

        Task.Run(async () => await ReceiveLoopAsync(controlClient, "Control", token), token);
        Task.Run(async () => await ReceiveLoopAsync(dataClient, "Data", token), token);

        // Start periodic clock sync task as AppleMIDI initiator must initiate sync
        Task.Run(async () =>
        {
            // Initial sync immediately after connecting
            SendClockSyncAnchor(controlClient, controlDestination);
            SendClockSyncAnchor(dataClient, dataDestination);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(15000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (Connection != MidiPortConnectionState.Open) break;

                SendClockSyncAnchor(controlClient, controlDestination);
                SendClockSyncAnchor(dataClient, dataDestination);
            }
        }, token);
    }

    private async Task ReceiveLoopAsync(UdpClient client, string name, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await client.ReceiveAsync(token);
                var buffer = result.Buffer;
                if (buffer.Length < 4) continue;

                if (buffer[0] == 0xFF && buffer[1] == 0xFF)
                {
                    ushort command = (ushort)((buffer[2] << 8) | buffer[3]);
                    if (command == 0x434B) // 'CK'
                    {
                        byte count = buffer.Length > 8 ? buffer[8] : (byte)0;
                        Console.WriteLine($"[RTP-MIDI DEBUG] Received CK (count={count}) on {name} from {result.RemoteEndPoint}");
                        HandleClockSync(client, result.RemoteEndPoint, buffer);
                    }
                    else if (command == 0x4259) // 'BY'
                    {
                        Console.WriteLine($"[RTP-MIDI] Received BY from {name} (peer ended session)");
                        Connection = MidiPortConnectionState.Closed;
                    }
                }
                else
                {
                    Console.WriteLine($"[RTP-MIDI DEBUG] Received unknown packet on {name} from {result.RemoteEndPoint}: {string.Join(" ", buffer.Take(16).Select(b => b.ToString("X2")))}{(buffer.Length > 16 ? "..." : "")}");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Console.WriteLine($"[RTP-MIDI] Receive error on {name}: {ex.Message}");
                }
                break;
            }
        }
    }

    private void HandleClockSync(UdpClient client, IPEndPoint remoteEP, byte[] buffer)
    {
        if (buffer.Length < 36) return;

        byte count = buffer[8];
        long t1 = ReadInt64BigEndian(buffer, 12);
        long t2 = ReadInt64BigEndian(buffer, 20);
        long t3 = ReadInt64BigEndian(buffer, 28);

        long now = GetMidiTimestamp();

        byte[] response = new byte[36];
        response[0] = 0xFF;
        response[1] = 0xFF;
        response[2] = 0x43;
        response[3] = 0x4B; // 'CK'
        
        WriteUInt32BigEndian(response, 4, ssrc);

        if (count == 0)
        {
            response[8] = 1;
            WriteInt64BigEndian(response, 12, t1);
            WriteInt64BigEndian(response, 20, now);
            WriteInt64BigEndian(response, 28, now);
        }
        else if (count == 1)
        {
            response[8] = 2;
            WriteInt64BigEndian(response, 12, t1);
            WriteInt64BigEndian(response, 20, t2);
            WriteInt64BigEndian(response, 28, now);
        }
        else if (count == 2)
        {
            // Sync complete, no reply needed
            Console.WriteLine($"[RTP-MIDI DEBUG] Clock sync sequence complete (round-trip calculated)!");
            return;
        }

        try
        {
            client.Send(response, response.Length, remoteEP);
            Console.WriteLine($"[RTP-MIDI DEBUG] Sent CK{response[8]} reply to {remoteEP}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RTP-MIDI WARNING] Failed to send CK{response[8]} reply: {ex.Message}");
        }
    }

    public void Send(byte[] mevent, int offset, int length, long timestamp)
    {
        try
        {
            if (Connection != MidiPortConnectionState.Open) return;

            if (debugDumpCount < 200)
            {
                debugDumpCount++;
                Console.WriteLine($"[RTP-MIDI DEBUG] Send called ({debugDumpCount}/200): len={length}, offset={offset}, arrLen={mevent?.Length ?? 0}, data={(mevent != null ? string.Join(" ", mevent.Skip(offset).Take(length).Select(b => b.ToString("X2"))) : "null")}");
            }

            if (mevent == null || length <= 0 || offset < 0 || offset + length > mevent.Length)
            {
                Console.WriteLine($"[RTP-MIDI WARNING] Invalid Send parameters: mevent={(mevent == null ? "null" : "non-null")}, offset={offset}, length={length}");
                return;
            }

            lock (sendLock)
            {
                // Z=0 (Delta-time 0 is NOT present).
                int rtpMidiHeaderLen = (length < 15) ? 1 : 2;
                byte[] packet = new byte[12 + rtpMidiHeaderLen + length];

                // RTP Header
                packet[0] = 0x80; // V=2
                packet[1] = 0x61; // PT=97 (RTP-MIDI)
                
                ushort seq = sequenceNumber++;
                packet[2] = (byte)(seq >> 8);
                packet[3] = (byte)seq;

                uint rtpTimestamp = unchecked((uint)GetMidiTimestamp());
                WriteUInt32BigEndian(packet, 4, rtpTimestamp);
                WriteUInt32BigEndian(packet, 8, ssrc);

                // RTP-MIDI Header (B, J, Z=0, P)
                // Z=0 means no delta time 0.
                if (length < 15)
                {
                    packet[12] = (byte)length;
                }
                else
                {
                    packet[12] = (byte)(0x80 | (length >> 8)); // B=1 (0x80) | Z=0 (0x00)
                    packet[13] = (byte)(length & 0xFF);
                }

                // Copy MIDI payload immediately after header (no delta-time byte)
                Array.Copy(mevent, offset, packet, 12 + rtpMidiHeaderLen, length);

                try
                {
                    dataClient.Send(packet, packet.Length, dataDestination);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RTP-MIDI] Send UDP error: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RTP-MIDI CRITICAL ERROR] Exception inside Send method: {ex}");
        }
    }

    public async Task CloseAsync()
    {
        if (Connection == MidiPortConnectionState.Closed) return;

        Connection = MidiPortConnectionState.Closed;
        cts?.Cancel();

        var byPacket = CreateByPacket(initiatorToken, ssrc);
        try
        {
            await controlClient.SendAsync(byPacket, byPacket.Length, controlDestination);
            await dataClient.SendAsync(byPacket, byPacket.Length, dataDestination);
        }
        catch { }

        controlClient.Close();
        dataClient.Close();
        cts?.Dispose();
    }

    public void Dispose()
    {
        CloseAsync().Wait();
    }

    #region Serialization Helpers
    private static byte[] CreateInvitationPacket(uint token, uint ssrc, string name)
    {
        byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        byte[] packet = new byte[16 + nameBytes.Length + 1];

        packet[0] = 0xFF;
        packet[1] = 0xFF;
        packet[2] = 0x49;
        packet[3] = 0x4E; // 'IN'

        WriteUInt32BigEndian(packet, 4, 2); // Version 2
        WriteUInt32BigEndian(packet, 8, token);
        WriteUInt32BigEndian(packet, 12, ssrc);

        Array.Copy(nameBytes, 0, packet, 16, nameBytes.Length);
        packet[packet.Length - 1] = 0; // Null-terminator

        return packet;
    }

    private static bool IsOkPacket(byte[] buffer, uint expectedToken)
    {
        if (buffer.Length < 16) return false;
        if (buffer[0] != 0xFF || buffer[1] != 0xFF) return false;
        if (buffer[2] != 0x4F || buffer[3] != 0x4B) return false; // 'OK'

        uint version = (uint)((buffer[4] << 24) | (buffer[5] << 16) | (buffer[6] << 8) | buffer[7]);
        uint token = (uint)((buffer[8] << 24) | (buffer[9] << 16) | (buffer[10] << 8) | buffer[11]);

        return version == 2 && token == expectedToken;
    }

    private static byte[] CreateByPacket(uint token, uint ssrc)
    {
        byte[] packet = new byte[16];
        packet[0] = 0xFF;
        packet[1] = 0xFF;
        packet[2] = 0x42;
        packet[3] = 0x59; // 'BY'

        WriteUInt32BigEndian(packet, 4, 2); // Version 2
        WriteUInt32BigEndian(packet, 8, token);
        WriteUInt32BigEndian(packet, 12, ssrc);
        return packet;
    }

    private static long ReadInt64BigEndian(byte[] buffer, int offset)
    {
        return (long)(
            ((ulong)buffer[offset] << 56) |
            ((ulong)buffer[offset + 1] << 48) |
            ((ulong)buffer[offset + 2] << 40) |
            ((ulong)buffer[offset + 3] << 32) |
            ((ulong)buffer[offset + 4] << 24) |
            ((ulong)buffer[offset + 5] << 16) |
            ((ulong)buffer[offset + 6] << 8) |
            (ulong)buffer[offset + 7]
        );
    }

    private static void WriteInt64BigEndian(byte[] buffer, int offset, long value)
    {
        ulong val = (ulong)value;
        buffer[offset] = (byte)(val >> 56);
        buffer[offset + 1] = (byte)(val >> 48);
        buffer[offset + 2] = (byte)(val >> 40);
        buffer[offset + 3] = (byte)(val >> 32);
        buffer[offset + 4] = (byte)(val >> 24);
        buffer[offset + 5] = (byte)(val >> 16);
        buffer[offset + 6] = (byte)(val >> 8);
        buffer[offset + 7] = (byte)val;
    }

    private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
    #endregion
}
