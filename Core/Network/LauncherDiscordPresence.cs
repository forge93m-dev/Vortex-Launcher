using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VortexLauncher.Core.Network;

/// <summary>Opt-in local desktop IPC. Never reads account data or requires tokens.</summary>
public sealed class LauncherDiscordPresence : IDisposable
{
    public const string ApplicationId = "1547929448165281924";
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<CancellationToken, Task<Stream>> _connect;
    private readonly object _gate = new();
    private readonly long _started = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private CancellationTokenSource? _connection;
    private bool _enabled, _playing, _receiveInvites;
    private volatile bool _subscriptionReady;
    private long _lastInvite = long.MinValue / 2;
    private string _page = "Instances", _image = "vortex", _status = "Disabled";
    public event Action<string>? StatusChanged;
    public event Action<DiscordServerInvite>? InvitationReceived;
    public string Status { get { lock (_gate) return _status; } }

    public LauncherDiscordPresence() : this(ConnectDesktopAsync) { }
    internal LauncherDiscordPresence(Func<CancellationToken, Task<Stream>> connector)
    {
        _connect = connector;
        _ = Task.Run(RunAsync);
    }
    public static string PageState(string page) => page switch
    {
        "Modpacks" => "Discovering modpacks", "Accounts" => "Managing profiles",
        "Settings" => "Changing launcher settings", "Wardrobe" => "Customizing appearance",
        "Instance" => "Managing a Minecraft instance", _ => "Browsing the library"
    };
    public static string NormalizeImage(string? key) =>
        key != null && System.Text.RegularExpressions.Regex.IsMatch(key, "^[a-z0-9_-]{1,64}$") ? key : "";

    public void Update(bool enabled, string page, string image, bool playing, bool receiveInvites = false)
    {
        lock (_gate)
        {
            bool changed = _receiveInvites != receiveInvites;
            _enabled = enabled; _page = page; _image = NormalizeImage(image); _playing = playing; _receiveInvites = receiveInvites;
            if (!enabled || playing || changed) _connection?.Cancel();
        }
        if (!enabled) SetStatus("Disabled");
        else if (playing) SetStatus("Paused while Minecraft is running");
    }
    private void SetStatus(string status)
    {
        lock (_gate)
        {
            if (!_enabled) status = "Disabled";
            else if (_playing) status = "Paused while Minecraft is running";
            if (_status == status) return;
            _status = status;
        }
        StatusChanged?.Invoke(status);
    }
    public static object Payload(string page, string image, long started, string nonce)
    {
        var activity = new Dictionary<string, object>
        {
            ["type"] = 0, ["details"] = "Vortex Launcher", ["state"] = PageState(page),
            ["timestamps"] = new { start = started }
        };
        string key = NormalizeImage(image);
        if (key.Length > 0) activity["assets"] = new { large_image = key, large_text = "Vortex Launcher" };
        return new { cmd = "SET_ACTIVITY", args = new { pid = Environment.ProcessId, activity }, nonce };
    }
    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            bool active;
            lock (_gate) active = _enabled && !_playing;
            if (!active) { try { await Task.Delay(250, _stop.Token); } catch (OperationCanceledException) { } continue; }
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            lock (_gate)
            {
                _connection = lifetime;
                if (!_enabled || _playing) lifetime.Cancel();
            }
            try
            {
                SetStatus("Connecting to Discord desktop…");
                _subscriptionReady = false;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var stream = await _connect(timeout.Token);
                // Disposing interrupts IPC reads/writes on shutdown and when sharing is disabled.
                using var registration = lifetime.Token.Register(stream.Dispose);
                var writeGate = new SemaphoreSlim(1, 1);
                try
                {
                    await WriteAsync(stream, writeGate, 0, new { v = 1, client_id = ApplicationId }, timeout.Token);
                    await WaitForAsync(stream, writeGate, "", timeout.Token);
                    bool receive;
                    lock (_gate) receive = _receiveInvites;
                    if (receive)
                    {
                        string subscribe = Guid.NewGuid().ToString("N");
                        using var subscribeTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        subscribeTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                        await WriteAsync(stream, writeGate, 1, new { cmd = "SUBSCRIBE", evt = "ACTIVITY_JOIN", nonce = subscribe }, subscribeTimeout.Token);
                        try { await WaitForAsync(stream, writeGate, subscribe, subscribeTimeout.Token, "SUBSCRIBE"); _subscriptionReady = true; }
                        catch (CommandRejectedException) { /* Activity sharing remains available without invitations. */ }
                    }
                    string? sent = null;
                    DateTime nextSend = DateTime.MinValue;
                    Task<(int Opcode, byte[] Data)>? pendingRead = null;
                    while (!lifetime.IsCancellationRequested)
                    {
                        string page, image;
                        lock (_gate) { page = _page; image = _image; }
                        string desired = PageState(page) + "|" + image;
                        if (desired != sent && DateTime.UtcNow >= nextSend)
                        {
                            string nonce = Guid.NewGuid().ToString("N");
                            using var ackTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                            ackTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                            await WriteAsync(stream, writeGate, 1, Payload(page, image, _started, nonce), ackTimeout.Token);
                            while (true)
                            {
                                pendingRead ??= ReadFrameAsync(stream, lifetime.Token);
                                var reply = await pendingRead.WaitAsync(ackTimeout.Token);
                                pendingRead = null;
                                await HandleFrameAsync(reply, stream, writeGate, ackTimeout.Token);
                                if (reply.Opcode != 1) continue;
                                using var json = JsonDocument.Parse(reply.Data);
                                if (Text(json.RootElement, "nonce") == nonce && Text(json.RootElement, "cmd") == "SET_ACTIVITY") break;
                            }
                            sent = desired; nextSend = DateTime.UtcNow.AddSeconds(15);
                            SetStatus("Connected · activity shared" + (receive ? _subscriptionReady ? " · invitations ready" : " · invitations unavailable" : ""));
                        }
                        // Preserve partial frames and a single outstanding reader between updates.
                        pendingRead ??= ReadFrameAsync(stream, lifetime.Token);
                        await Task.WhenAny(pendingRead, Task.Delay(250, lifetime.Token));
                        if (pendingRead.IsCompleted)
                        {
                            await HandleFrameAsync(await pendingRead, stream, writeGate, lifetime.Token);
                            pendingRead = null;
                        }
                    }
                }
                finally { writeGate.Dispose(); }
            }
            catch (Exception) when (!_stop.IsCancellationRequested)
            {
                lock (_gate) active = _enabled && !_playing;
                if (active) SetStatus("Discord unavailable or rejected activity · retrying");
            }
            catch (Exception) { }
            finally { lock (_gate) if (ReferenceEquals(_connection, lifetime)) _connection = null; }
            lock (_gate) active = _enabled && !_playing;
            if (active && !lifetime.IsCancellationRequested) { try { await Task.Delay(TimeSpan.FromSeconds(15), _stop.Token); } catch (OperationCanceledException) { } }
        }
    }
    internal static async Task<(int Opcode, byte[] Data)> ReadFrameAsync(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[8]; await stream.ReadExactlyAsync(header, token);
        int op = BinaryPrimitives.ReadInt32LittleEndian(header), length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (op < 0 || op > 4 || length < 0 || length > 65536) throw new IOException("Invalid Discord IPC frame");
        byte[] data = new byte[length]; await stream.ReadExactlyAsync(data, token); return (op, data);
    }
    private static async Task WriteAsync(Stream stream, SemaphoreSlim gate, int opcode, object data, CancellationToken token)
    {
        byte[] payload = data is byte[] bytes ? bytes : JsonSerializer.SerializeToUtf8Bytes(data);
        if (payload.Length > 65536) throw new IOException("IPC payload too large");
        byte[] frame = new byte[8 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, opcode); BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), payload.Length);
        payload.CopyTo(frame, 8);
        await gate.WaitAsync(token);
        try { await stream.WriteAsync(frame, token); } finally { gate.Release(); }
    }
    private sealed class CommandRejectedException : IOException { }
    private async Task HandleFrameAsync((int Opcode, byte[] Data) frame, Stream stream, SemaphoreSlim gate, CancellationToken token)
    {
        if (frame.Opcode == 2) throw new IOException("Discord IPC closed");
        if (frame.Opcode == 3) await WriteAsync(stream, gate, 4, frame.Data, token);
        if (frame.Opcode == 1)
        {
            using var json = JsonDocument.Parse(frame.Data);
            var root = json.RootElement;
            if (Text(root, "evt") == "ERROR") throw new CommandRejectedException();
            bool allowed;
            lock (_gate) allowed = _enabled && !_playing && _receiveInvites && _subscriptionReady;
            if (allowed && Text(root, "cmd") == "DISPATCH" && Text(root, "evt") == "ACTIVITY_JOIN" &&
                root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
                DiscordServerInvite.TryDecode(Text(data, "secret"), DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out var invite) &&
                Environment.TickCount64 - _lastInvite >= 10000)
            {
                _lastInvite = Environment.TickCount64;
                InvitationReceived?.Invoke(invite!);
            }
        }
    }
    private static string Text(JsonElement json, string key) => json.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    private async Task WaitForAsync(Stream stream, SemaphoreSlim gate, string nonce, CancellationToken token, string command = "SET_ACTIVITY")
    {
        while (true)
        {
            var frame = await ReadFrameAsync(stream, token);
            await HandleFrameAsync(frame, stream, gate, token);
            if (frame.Opcode != 1) continue;
            using var json = JsonDocument.Parse(frame.Data);
            var root = json.RootElement;
            if (nonce.Length == 0 ? Text(root, "evt") == "READY" : Text(root, "nonce") == nonce && Text(root, "cmd") == command) return;
        }
    }
    private static async Task<Stream> ConnectDesktopAsync(CancellationToken token)
    {
        if (OperatingSystem.IsWindows())
        {
            for (int i = 0; i < 10; i++)
            {
                var pipe = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                try { await pipe.ConnectAsync(100, token); return pipe; }
                catch (Exception ex) when (ex is IOException or TimeoutException) { pipe.Dispose(); }
                catch { pipe.Dispose(); throw; }
            }
        }
        else
        {
            var dirs = new HashSet<string> { "/tmp" };
            foreach (string key in new[] { "XDG_RUNTIME_DIR", "TMPDIR", "TMP", "TEMP" })
                if (Environment.GetEnvironmentVariable(key) is { Length: > 0 } dir) dirs.Add(dir);
            foreach (string dir in dirs) for (int i = 0; i < 10; i++)
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try { await socket.ConnectAsync(new UnixDomainSocketEndPoint(Path.Combine(dir, "discord-ipc-" + i)), token); return new NetworkStream(socket, true); }
                catch (Exception ex) when (ex is SocketException or ArgumentException or IOException) { socket.Dispose(); }
                catch { socket.Dispose(); throw; }
            }
        }
        throw new IOException("Discord desktop IPC not found");
    }
    public void Dispose()
    {
        _stop.Cancel();
        lock (_gate) { _enabled = false; _connection?.Cancel(); }
    }
}
