package dev.vortex.client;

import com.google.gson.*;
import java.io.*;
import java.net.*;
import java.nio.*;
import java.nio.channels.SocketChannel;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicLong;

/** Local Discord IPC only. No bot token, OAuth, TCP or Discord account reads. */
public final class DiscordRpc implements AutoCloseable {
    public static final String APPLICATION_ID = "1547929448165281924";
    public enum Mode { MENU, SINGLEPLAYER, MULTIPLAYER }
    public record Activity(String minecraftVersion, Mode mode, String invite) {
        public Activity(String minecraftVersion, Mode mode) { this(minecraftVersion, mode, ""); }
        public Activity {
            if (minecraftVersion == null || !minecraftVersion.matches("[A-Za-z0-9_.+-]{1,48}") || mode == null)
                throw new IllegalArgumentException("Invalid activity");
            if (invite == null) throw new IllegalArgumentException("Invalid invite");
            if (!invite.isEmpty()) {
                var decoded = ServerInvite.decode(invite, System.currentTimeMillis()/1000);
                if (mode != Mode.MULTIPLAYER || !decoded.version().equals(minecraftVersion)) throw new IllegalArgumentException("Invite does not match activity");
            }
        }
    }
    public interface Transport extends AutoCloseable {
        int read(byte[] buffer, int offset, int length) throws IOException;
        void write(byte[] frame) throws IOException;
        void close() throws IOException;
    }
    @FunctionalInterface interface Connector { Transport connect() throws IOException; }
    public record Frame(int opcode, byte[] bytes) { }
    private final Connector connector;
    private final ScheduledExecutorService worker;
    private final long started = System.currentTimeMillis() / 1000;
    private final AtomicLong ids = new AtomicLong();
    private volatile boolean enabled, closed;
    private volatile Activity desired = new Activity("Minecraft", Mode.MENU);
    private volatile String status = "Disabled";
    private volatile Session session;
    private long nextAttempt, lastSent;
    private Activity sent;

    public DiscordRpc() { this(DiscordRpc::connectDesktop); }
    DiscordRpc(Connector connector) {
        this.connector = connector;
        worker = Executors.newSingleThreadScheduledExecutor(r -> { var t = new Thread(r, "Vortex Discord RPC"); t.setDaemon(true); return t; });
        worker.scheduleWithFixedDelay(this::pump, 0, 1, TimeUnit.SECONDS);
    }
    public void update(boolean enabled, Activity activity) {
        if (closed) return;
        desired = Objects.requireNonNull(activity);
        if (this.enabled != enabled) status = enabled ? "Connecting to Discord desktop…" : "Disabled";
        this.enabled = enabled;
    }
    public String status() { return status; }
    private void pump() {
        try {
            if (closed || !enabled) { drop(); return; }
            long now = System.nanoTime();
            var current = session;
            if (current == null || current.failed) {
                drop();
                if (now < nextAttempt) return;
                nextAttempt = now + TimeUnit.SECONDS.toNanos(15);
                status = "Connecting to Discord desktop…";
                current = new Session(connector.connect());
                session = current;
                current.start();
                current.ready.get(5, TimeUnit.SECONDS);
                if (closed || !enabled) { drop(); return; }
                sent = null;
            }
            Activity latest = desired;
            boolean clearingInvite = sent != null && !sent.invite().isEmpty() && latest.invite().isEmpty();
            if (!latest.equals(sent) && (sent == null || clearingInvite || now - lastSent >= TimeUnit.SECONDS.toNanos(15))) {
                String nonce = Long.toString(ids.incrementAndGet());
                var result = new CompletableFuture<Void>();
                current.pending.put(nonce, result);
                current.send(1, activityPayload(latest, started, ProcessHandle.current().pid(), nonce).toString());
                result.get(5, TimeUnit.SECONDS);
                current.pending.remove(nonce);
                sent = latest; lastSent = System.nanoTime();
                if (enabled) status = "Connected · activity shared";
            }
        } catch (Exception ex) {
            drop();
            if (enabled && !closed) status = "Discord unavailable or rejected activity · retrying";
        }
    }
    private void drop() { var old = session; session = null; sent = null; if (old != null) old.close(); }
    @Override public void close() { closed = true; enabled = false; status = "Disabled"; worker.shutdownNow(); drop(); }

    public static JsonObject activityPayload(Activity value, long start, long pid, String nonce) {
        var activity = new JsonObject(); activity.addProperty("type", 0);
        activity.addProperty("details", "Minecraft " + value.minecraftVersion());
        activity.addProperty("state", switch (value.mode()) { case MENU -> "Main menu"; case SINGLEPLAYER -> "Singleplayer"; case MULTIPLAYER -> "Multiplayer"; });
        var timestamps = new JsonObject(); timestamps.addProperty("start", start); activity.add("timestamps", timestamps);
        var assets = new JsonObject(); assets.addProperty("large_image", "vortex"); assets.addProperty("large_text", "Vortex Client"); activity.add("assets", assets);
        if (!value.invite().isEmpty()) {
            var secrets = new JsonObject(); secrets.addProperty("join", value.invite()); activity.add("secrets", secrets);
            var party = new JsonObject(); party.addProperty("id", "vortex-" + ServerInvite.decode(value.invite(), System.currentTimeMillis()/1000).nonce());
            var size = new JsonArray(); size.add(1); size.add(2); party.add("size", size); activity.add("party", party);
            activity.addProperty("instance", true);
        }
        var args = new JsonObject(); args.addProperty("pid", pid); args.add("activity", activity);
        var command = new JsonObject(); command.addProperty("cmd", "SET_ACTIVITY"); command.add("args", args); command.addProperty("nonce", nonce);
        return command;
    }
    public static byte[] frame(int opcode, byte[] payload) throws IOException {
        if (opcode < 0 || opcode > 4 || payload.length > 65536) throw new IOException("Invalid IPC frame");
        return ByteBuffer.allocate(8 + payload.length).order(ByteOrder.LITTLE_ENDIAN).putInt(opcode).putInt(payload.length).put(payload).array();
    }
    public static Frame readFrame(Transport transport) throws IOException {
        byte[] header = new byte[8]; readFully(transport, header);
        var buffer = ByteBuffer.wrap(header).order(ByteOrder.LITTLE_ENDIAN);
        int opcode = buffer.getInt(), length = buffer.getInt();
        if (opcode < 0 || opcode > 4 || length < 0 || length > 65536) throw new IOException("Invalid Discord IPC frame");
        byte[] bytes = new byte[length]; readFully(transport, bytes); return new Frame(opcode, bytes);
    }
    private static void readFully(Transport transport, byte[] bytes) throws IOException {
        int offset = 0;
        while (offset < bytes.length) { int count = transport.read(bytes, offset, bytes.length - offset); if (count <= 0) throw new EOFException("Discord IPC closed"); offset += count; }
    }
    private static final class Session implements AutoCloseable {
        final Transport transport;
        final CompletableFuture<Void> ready = new CompletableFuture<>();
        final ConcurrentHashMap<String, CompletableFuture<Void>> pending = new ConcurrentHashMap<>();
        volatile boolean failed;
        Session(Transport transport) { this.transport = transport; }
        void start() throws IOException {
            var thread = new Thread(this::receive, "Vortex Discord IPC reader"); thread.setDaemon(true); thread.start();
            var hello = new JsonObject(); hello.addProperty("v", 1); hello.addProperty("client_id", APPLICATION_ID);
            send(0, hello.toString());
        }
        synchronized void send(int opcode, String text) throws IOException { transport.write(frame(opcode, text.getBytes(StandardCharsets.UTF_8))); }
        void receive() {
            try {
                while (!failed) {
                    var frame = readFrame(transport);
                    if (frame.opcode() == 2) throw new IOException("Discord closed IPC");
                    if (frame.opcode() == 3) { synchronized (this) { transport.write(DiscordRpc.frame(4, frame.bytes())); } continue; }
                    if (frame.opcode() != 1) continue;
                    var message = JsonParser.parseString(new String(frame.bytes(), StandardCharsets.UTF_8)).getAsJsonObject();
                    String event = string(message, "evt");
                    if ("READY".equals(event)) ready.complete(null);
                    String nonce = string(message, "nonce");
                    var waiter = pending.get(nonce);
                    if ("ERROR".equals(event)) {
                        var error = new IOException("Discord rejected RPC");
                        if (waiter != null) waiter.completeExceptionally(error); else ready.completeExceptionally(error);
                    } else if (waiter != null && "SET_ACTIVITY".equals(string(message, "cmd"))) waiter.complete(null);
                }
            } catch (Exception ex) { ready.completeExceptionally(ex); pending.values().forEach(p -> p.completeExceptionally(ex)); }
            finally { close(); }
        }
        private static String string(JsonObject object, String key) {
            var value = object.get(key); return value != null && value.isJsonPrimitive() && value.getAsJsonPrimitive().isString() ? value.getAsString() : "";
        }
        @Override public void close() { failed = true; try { transport.close(); } catch (IOException ignored) { } }
    }
    static String windowsPipePath(int index) {
        if (index < 0 || index > 9) throw new IllegalArgumentException("Invalid Discord pipe index");
        // Use the Win32 device namespace, not the extended filesystem prefix.
        return "\\\\.\\pipe\\discord-ipc-" + index;
    }
    static Transport connectDesktop() throws IOException {
        if (System.getProperty("os.name", "").toLowerCase(Locale.ROOT).startsWith("windows")) {
            for (int i = 0; i < 10; i++) {
                try {
                    return new WindowsDiscordPipe(windowsPipePath(i));
                } catch (IOException ignored) { }
            }
        } else {
            var directories = new LinkedHashSet<String>();
            for (String key : List.of("XDG_RUNTIME_DIR", "TMPDIR", "TMP", "TEMP")) { String value = System.getenv(key); if (value != null && !value.isBlank()) directories.add(value); }
            directories.add("/tmp");
            for (String directory : directories) for (int i = 0; i < 10; i++) {
                SocketChannel channel = null;
                try {
                    channel = SocketChannel.open(StandardProtocolFamily.UNIX);
                    channel.connect(UnixDomainSocketAddress.of(Path.of(directory, "discord-ipc-" + i)));
                    SocketChannel socket = channel;
                    return new Transport() {
                        public int read(byte[] b, int o, int l) throws IOException { return socket.read(ByteBuffer.wrap(b, o, l)); }
                        public void write(byte[] b) throws IOException { var data = ByteBuffer.wrap(b); while (data.hasRemaining()) socket.write(data); }
                        public void close() throws IOException { socket.close(); }
                    };
                } catch (IOException | IllegalArgumentException ex) { if (channel != null) try { channel.close(); } catch (IOException ignored) { } }
            }
        }
        throw new IOException("Discord desktop IPC not found");
    }
}
