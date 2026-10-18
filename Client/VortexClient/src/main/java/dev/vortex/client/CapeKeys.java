package dev.vortex.client;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.nio.file.AtomicMoveNotSupportedException;
import java.security.SecureRandom;
import java.util.HexFormat;
import java.util.UUID;

/** Private local credentials live outside every game/pack directory. Never export. */
public final class CapeKeys {
    private final Path path;
    public CapeKeys(Path path) { this.path = path; }
    public static CapeKeys local() { return new CapeKeys(Path.of(System.getProperty("user.home"), ".vortex", "cape-keys.json")); }
    private JsonObject read() throws IOException {
        if (!Files.exists(path)) return new JsonObject();
        if (Files.isSymbolicLink(path) || Files.isSymbolicLink(path.getParent()) || Files.size(path) > 65536)
            throw new IOException("Cape key file is linked or too large; original was preserved.");
        try { return JsonParser.parseString(Files.readString(path)).getAsJsonObject(); }
        catch (RuntimeException error) { throw new IOException("Cape key file is unreadable; original was preserved."); }
    }
    public synchronized String get(UUID id) throws IOException {
        var value = read().get(CapeConnection.id(id));
        if (value == null) return "";
        if (!value.isJsonPrimitive() || !CapeConnection.secret(value.getAsString())) throw new IOException("Invalid saved cape key; original was preserved.");
        return value.getAsString();
    }
    public synchronized String ensure(UUID id) throws IOException {
        Files.createDirectories(path.getParent());
        Path lockPath = path.resolveSibling(path.getFileName()+".lock");
        if (Files.isSymbolicLink(path.getParent()) || Files.isSymbolicLink(lockPath)) throw new IOException("Cape key paths must not be links.");
        try (var channel = java.nio.channels.FileChannel.open(lockPath,java.nio.file.StandardOpenOption.CREATE,java.nio.file.StandardOpenOption.WRITE)) {
            for (int attempt=0;attempt<50;attempt++) {
                java.nio.channels.FileLock lock=null;
                try { lock=channel.tryLock(); } catch (java.nio.channels.OverlappingFileLockException busy) { }
                if(lock!=null) { try { return ensureLocked(id); } finally { lock.release(); } }
                try { Thread.sleep(100); } catch(InterruptedException interrupted) { Thread.currentThread().interrupt(); throw new IOException("Cape key save cancelled."); }
            }
        }
        throw new IOException("Cape keys are in use by another client. Retry later.");
    }
    private String ensureLocked(UUID id) throws IOException {
        String existing = get(id); if (!existing.isEmpty()) return existing;
        var document = read();
        if (document.size() >= 128) throw new IOException("Cape key storage is full.");
        byte[] bytes = new byte[32]; new SecureRandom().nextBytes(bytes);
        String key = HexFormat.of().formatHex(bytes);
        document.addProperty(CapeConnection.id(id), key);
        Files.createDirectories(path.getParent());
        if (Files.isSymbolicLink(path.getParent())) throw new IOException("Cape key folder must not be a link.");
        Path temporary = Files.createTempFile(path.getParent(), "cape-keys-", ".tmp");
        try {
            if (Files.getFileStore(temporary).supportsFileAttributeView("posix"))
                Files.setPosixFilePermissions(temporary,java.nio.file.attribute.PosixFilePermissions.fromString("rw-------"));
            Files.writeString(temporary,document.toString());
            try { Files.move(temporary,path,StandardCopyOption.ATOMIC_MOVE,StandardCopyOption.REPLACE_EXISTING); }
            catch (AtomicMoveNotSupportedException error) { Files.move(temporary,path,StandardCopyOption.REPLACE_EXISTING); }
        } finally { Files.deleteIfExists(temporary); }
        return key;
    }
}
