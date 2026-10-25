package dev.vortex.client;

import java.io.InputStream;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.security.MessageDigest;
import java.time.Duration;
import java.util.HexFormat;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.zip.ZipFile;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.Minecraft;

/**
 * Title-screen panoramas from older Minecraft versions. The six images are Mojang's, so they are not bundled.
 * Since 1.13 or so the client jar holds only 1x1 placeholders and the real images are game assets: they are
 * downloaded from Mojang's asset server through that version's asset index, each checked against its SHA-1,
 * exactly as the launcher downloads assets. Versions whose index lacks them fall back to their client jar.
 */
public final class PanoramaLibrary {
    private PanoramaLibrary() { }
    /** A choice in the list: the Minecraft version the panorama comes from and its update's name. */
    public record Choice(String version, String title) { }
    /** Used until the full list has been looked up from Mojang (and for old versions that lookup cannot cover). */
    public static final List<Choice> CHOICES = List.of(
        new Choice("1.21.1", "Tricky Trials"), new Choice("1.20.1", "Trails & Tales"), new Choice("1.19.4", "The Wild Update"),
        new Choice("1.18.2", "Caves & Cliffs II"), new Choice("1.17.1", "Caves & Cliffs I"), new Choice("1.16.5", "Nether Update"),
        new Choice("1.15.2", "Buzzy Bees"), new Choice("1.14.4", "Village & Pillage"), new Choice("1.13.2", "Update Aquatic"),
        new Choice("1.12.2", "World of Color"), new Choice("1.8.9", "Classic"));
    static final String PANORAMA = "textures/gui/title/background/panorama";
    static final String MANIFEST = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    /** Mojang client jars are 5-30 MB; anything far larger is refused. Faces smaller than this are placeholders. */
    static final long MAX_JAR = 96L * 1024 * 1024, MAX_IMAGE = 16L * 1024 * 1024;
    static final int MIN_FACE = 64;
    static final String ASSETS = "https://resources.download.minecraft.net/";
    private static final HttpClient HTTP = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(8)).followRedirects(HttpClient.Redirect.NORMAL).build();
    private static final AtomicBoolean BUSY = new AtomicBoolean();
    private static volatile String status = "";
    private static volatile int progress = -1;

    public static boolean busy() { return BUSY.get(); }
    public static String status() { return status; }
    /** Download progress 0-100, or -1 when nothing is downloading. */
    public static int progress() { return progress; }
    public static Path folder() { return FabricLoader.getInstance().getConfigDir().resolve("vortex").resolve("panoramas"); }
    /** Release ids such as 1.20.1 or 26.1.2; anything else (paths, snapshots) is refused. */
    public static boolean valid(String version) { return version != null && version.matches("\\d{1,3}(\\.\\d{1,3}){1,3}"); }
    /** Update names only where certain; other versions are shown by number. */
    static final java.util.Map<String, String> NAMES = java.util.Map.ofEntries(
        java.util.Map.entry("1.8", "Bountiful Update"), java.util.Map.entry("1.9", "Combat Update"), java.util.Map.entry("1.10", "Frostburn Update"),
        java.util.Map.entry("1.11", "Exploration Update"), java.util.Map.entry("1.12", "World of Color"), java.util.Map.entry("1.13", "Update Aquatic"),
        java.util.Map.entry("1.14", "Village & Pillage"), java.util.Map.entry("1.15", "Buzzy Bees"), java.util.Map.entry("1.16", "Nether Update"),
        java.util.Map.entry("1.17", "Caves & Cliffs I"), java.util.Map.entry("1.18", "Caves & Cliffs II"), java.util.Map.entry("1.19", "The Wild Update"),
        java.util.Map.entry("1.20", "Trails & Tales"), java.util.Map.entry("1.21", "Tricky Trials"), java.util.Map.entry("1.21.2", "Bundles of Bravery"),
        java.util.Map.entry("1.21.4", "The Garden Awakens"), java.util.Map.entry("1.21.5", "Spring to Life"), java.util.Map.entry("1.21.6", "Chase the Skies"),
        java.util.Map.entry("1.21.9", "The Copper Age"));
    /** Name of the update a release belongs to: an exact drop (1.21.5) first, then its major update (1.20.1 -> 1.20). */
    static String titleFor(String version) {
        if (NAMES.containsKey(version)) return NAMES.get(version);
        String[] parts = version.split("\\.");
        String major = parts.length >= 2 ? parts[0] + "." + parts[1] : version;
        // 1.21.x drops have their own names; a patch of an unnamed drop must not borrow 1.21's.
        return !major.equals("1.21") && NAMES.containsKey(major) ? NAMES.get(major) : "Minecraft " + version;
    }
    private static volatile List<Choice> discovered;
    private static final AtomicBoolean LOOKING = new AtomicBoolean();
    public static boolean looking() { return LOOKING.get(); }
    /** Every distinct panorama found in Mojang's version list, newest first (cached for a week), else the built-in list. */
    public static List<Choice> choices() {
        var found = discovered;
        if (found == null) { found = discovered = readCatalog(folder()); adopt(found); }
        if (found.isEmpty()) return CHOICES;
        // The running game's own panorama is "Current Minecraft"; it is not listed twice.
        String current = labelFor(gameVersion(), found);
        return found.stream().filter(c -> !c.version().equals(current)).toList();
    }
    private static String gameVersion() {
        return FabricLoader.getInstance().getModContainer("minecraft").map(m -> m.getMetadata().getVersion().getFriendlyString()).orElse("");
    }
    /** Numeric release comparison: 1.21.11 is newer than 1.21.9, 26.1 newer than 1.21.11. */
    static int compare(String a, String b) {
        String[] x = a.split("\\."), y = b.split("\\.");
        for (int i = 0; i < Math.max(x.length, y.length); i++) {
            int p = i < x.length ? Integer.parseInt(x[i]) : 0, q = i < y.length ? Integer.parseInt(y[i]) : 0;
            if (p != q) return Integer.compare(p, q);
        }
        return 0;
    }
    /**
     * The list entry whose panorama a release shows: entries are the first release of each panorama, so it is the
     * newest entry not newer than the release (1.14.4 -> 1.14, 1.21.1 -> 1.21). Empty when it predates the list.
     */
    static String labelFor(String version, List<Choice> list) {
        if (!valid(version)) return "";
        String best = "";
        for (var choice : list) if (compare(choice.version(), version) <= 0 && (best.isEmpty() || compare(choice.version(), best) > 0)) best = choice.version();
        return best;
    }
    /** Panoramas downloaded under another version number (from the built-in list) move to their list entry; the choice follows. */
    private static void adopt(List<Choice> list) {
        if (list.isEmpty()) return;
        try {
            Path root = folder();
            if (Files.isDirectory(root)) try (var dirs = Files.list(root)) {
                for (var dir : dirs.filter(Files::isDirectory).toList()) {
                    String version = dir.getFileName().toString(), label = labelFor(version, list);
                    if (!valid(version) || label.isEmpty() || label.equals(version) || !installed(root, version)) continue;
                    if (!installed(root, label)) { deleteTree(root.resolve(label)); Files.move(dir, root.resolve(label), StandardCopyOption.ATOMIC_MOVE); }
                    else deleteTree(dir);
                }
            }
        } catch (Exception ignored) { /* nothing is lost: the panorama is downloaded again when chosen */ }
        String selected = VortexClient.config.menuPanorama, label = labelFor(selected, list);
        if (valid(selected) && !label.isEmpty() && !label.equals(selected)) { VortexClient.config.menuPanorama = label; VortexClient.config.save(); }
    }
    public static Choice find(String version) {
        if (!valid(version)) return null;
        return choices().stream().filter(c -> c.version().equals(version)).findFirst().orElse(new Choice(version, titleFor(version)));
    }
    /** The selected old panorama, or null for the current game's own one. */
    public static Choice selected() { return find(VortexClient.config.menuPanorama); }
    public static String title(String version) {
        var choice = find(version);
        return choice == null ? "Current Minecraft" : choice.title().equals("Minecraft " + choice.version()) ? choice.title() : choice.title() + " · " + choice.version();
    }
    /** All six faces saved; tiny files are the 1x1 placeholders an earlier download could have saved. */
    public static boolean installed(Path folder, String version) {
        try {
            for (int i = 0; i < 6; i++) {
                Path face = folder.resolve(version).resolve("panorama_" + i + ".png");
                if (!Files.isRegularFile(face) || Files.size(face) < 1024) return false;
            }
            return true;
        } catch (java.io.IOException unreadable) { return false; }
    }
    /** Index 0-5 when an identifier is one face of the title panorama, otherwise -1. */
    public static int face(String path) {
        if (!path.startsWith(PANORAMA + "_") || !path.endsWith(".png")) return -1;
        String digit = path.substring(PANORAMA.length() + 1, path.length() - 4);
        return digit.length() == 1 && digit.charAt(0) >= '0' && digit.charAt(0) <= '5' ? digit.charAt(0) - '0' : -1;
    }
    /** The file for a panorama face of the selected version, or null to use the game's own image. */
    public static Path override(String path) {
        int face = face(path);
        var choice = selected();
        if (face < 0 || choice == null) return null;
        Path file = folder().resolve(choice.version()).resolve("panorama_" + face + ".png");
        return installed(folder(), choice.version()) ? file : null;
    }

    /** Chooses a panorama: applies it at once when installed, otherwise downloads it first. */
    public static void choose(String version) {
        if (version.isEmpty() || installed(folder(), version)) { select(version); return; }
        if (find(version) == null || !BUSY.compareAndSet(false, true)) return;
        progress = 0; status = "Downloading the " + version + " panorama from Mojang...";
        var thread = new Thread(() -> {
            try {
                install(version, folder());
                Minecraft.getInstance().execute(() -> { status = "Ready."; select(version); });
            } catch (Exception failed) {
                status = "Could not get the " + version + " panorama. Check your connection and try again.";
            } finally { progress = -1; BUSY.set(false); }
        }, "Vortex panorama download");
        thread.setDaemon(true); thread.start();
    }
    private static void select(String version) {
        VortexClient.config.menuPanorama = version;
        VortexClient.config.save();
        reload();
    }
    /** Reloads the title panorama texture so a new choice shows without restarting. */
    static void reload() {
        var mc = Minecraft.getInstance();
        if (mc == null) return;
        var id = net.minecraft.resources.Identifier.withDefaultNamespace(PANORAMA);
        if (mc.getTextureManager().getTexture(id) instanceof net.minecraft.client.renderer.texture.ReloadableTexture texture) {
            try { texture.apply(texture.loadContents(mc.getResourceManager())); }
            catch (Exception failed) { status = "Could not show this panorama."; }
        }
    }

    /** Downloads the version's client jar from Mojang, verifies it and copies the six panorama images out. */
    static void install(String version, Path folder) throws Exception {
        var manifest = JsonParser.parseString(text(MANIFEST)).getAsJsonObject();
        String url = null, sha1 = null;
        for (var entry : manifest.getAsJsonArray("versions")) {
            var item = entry.getAsJsonObject();
            if (version.equals(item.get("id").getAsString())) { url = item.get("url").getAsString(); sha1 = item.get("sha1").getAsString(); }
        }
        if (url == null || !mojang(url)) throw new IllegalStateException("Unknown version");
        String versionJson = text(url);
        if (!sha1Hex(versionJson.getBytes(java.nio.charset.StandardCharsets.UTF_8)).equals(sha1)) throw new SecurityException("Version file checksum mismatch");
        var versionInfo = JsonParser.parseString(versionJson).getAsJsonObject();
        Files.createDirectories(folder);
        if (fromAssets(versionInfo, folder.resolve(version))) return;
        JsonObject client = versionInfo.getAsJsonObject("downloads").getAsJsonObject("client");
        String jarUrl = client.get("url").getAsString(), jarSha1 = client.get("sha1").getAsString();
        long size = client.get("size").getAsLong();
        if (!mojang(jarUrl) || size <= 0 || size > MAX_JAR) throw new IllegalStateException("Unexpected client jar");
        Path jar = Files.createTempFile(folder, "client-" + version + "-", ".jar.part");
        try {
            download(jarUrl, jar, size, jarSha1);
            extract(jar, folder.resolve(version));
        } finally { Files.deleteIfExists(jar); }
    }
    /**
     * The six faces from the asset server, through the version's asset index. False when the index does not
     * list them (old versions keep them in the client jar); throws when a listed face cannot be verified.
     */
    private static boolean fromAssets(JsonObject versionInfo, Path destination) throws Exception {
        var assetIndex = versionInfo.getAsJsonObject("assetIndex");
        if (assetIndex == null) return false;
        String indexUrl = assetIndex.get("url").getAsString(), indexSha1 = assetIndex.get("sha1").getAsString();
        if (!mojang(indexUrl)) throw new IllegalStateException("Unexpected asset index");
        String indexJson = text(indexUrl);
        if (!sha1Hex(indexJson.getBytes(java.nio.charset.StandardCharsets.UTF_8)).equals(indexSha1)) throw new SecurityException("Asset index checksum mismatch");
        var objects = JsonParser.parseString(indexJson).getAsJsonObject().getAsJsonObject("objects");
        String[] hashes = new String[6]; long[] sizes = new long[6]; long total = 0;
        for (int i = 0; i < 6; i++) {
            var entry = objects == null ? null : objects.getAsJsonObject("minecraft/" + PANORAMA + "_" + i + ".png");
            if (entry == null) return false;
            hashes[i] = entry.get("hash").getAsString(); sizes[i] = entry.get("size").getAsLong(); total += sizes[i];
            if (!hashes[i].matches("[0-9a-f]{40}") || sizes[i] <= 0 || sizes[i] > MAX_IMAGE) throw new IllegalStateException("Unexpected asset entry");
        }
        Path staging = destination.resolveSibling(destination.getFileName() + ".staging");
        deleteTree(staging); Files.createDirectories(staging);
        try {
            long done = 0;
            for (int i = 0; i < 6; i++) {
                Path face = staging.resolve("panorama_" + i + ".png");
                download(assetUrl(hashes[i]), face, sizes[i], hashes[i], done, total);
                done += sizes[i];
            }
            checkFaces(staging);
            deleteTree(destination);
            Files.move(staging, destination, StandardCopyOption.ATOMIC_MOVE);
            return true;
        } finally { deleteTree(staging); }
    }
    /** Mojang's asset server address for an object: /<first two hex digits>/<sha1>. */
    static String assetUrl(String hash) { return ASSETS + hash.substring(0, 2) + "/" + hash; }
    /** Six readable, square-or-not but equal-sized faces of at least MIN_FACE pixels; placeholders are refused. */
    static void checkFaces(Path folder) throws Exception {
        int width = -1, height = -1;
        for (int i = 0; i < 6; i++) {
            var image = javax.imageio.ImageIO.read(folder.resolve("panorama_" + i + ".png").toFile());
            if (image == null || image.getWidth() > 4096 || image.getHeight() > 4096) throw new IllegalStateException("Unreadable panorama image");
            if (image.getWidth() < MIN_FACE || image.getHeight() < MIN_FACE) throw new IllegalStateException("Placeholder panorama image");
            if (width < 0) { width = image.getWidth(); height = image.getHeight(); }
            else if (image.getWidth() != width || image.getHeight() != height) throw new IllegalStateException("Panorama faces differ in size");
        }
    }
    /** Looks up every distinct panorama in the background when the cached list is missing or older than a week. */
    public static void refreshList() {
        Path catalog = folder().resolve("catalog.json");
        try { if (Files.isRegularFile(catalog) && Files.getLastModifiedTime(catalog).toMillis() > System.currentTimeMillis() - 7L * 86_400_000) return; }
        catch (java.io.IOException ignored) { /* look up again */ }
        if (!LOOKING.compareAndSet(false, true)) return;
        var thread = new Thread(() -> {
            try {
                var versions = lookUp();
                Files.createDirectories(folder());
                var json = new com.google.gson.JsonArray(); versions.forEach(json::add);
                Path part = folder().resolve("catalog.json.part");
                Files.writeString(part, json.toString());
                Files.move(part, catalog, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
                var list = readCatalog(folder());
                Minecraft.getInstance().execute(() -> { adopt(list); discovered = list; reload(); });
            } catch (Exception failed) { /* keep the previous or built-in list */ }
            finally { LOOKING.set(false); }
        }, "Vortex panorama list");
        thread.setDaemon(true); thread.start();
    }
    static List<Choice> readCatalog(Path folder) {
        try {
            var array = JsonParser.parseString(Files.readString(folder.resolve("catalog.json"))).getAsJsonArray();
            var result = new java.util.ArrayList<Choice>();
            for (var item : array) { String version = item.getAsString(); if (valid(version) && result.size() < 64) result.add(new Choice(version, titleFor(version))); }
            return result;
        } catch (Exception missing) { return List.of(); }
    }
    /**
     * Release versions (newest first, back to 1.7.10) labelled by the oldest release that shows each distinct
     * panorama. Version files are small; each asset index is fetched once and checked against its SHA-1.
     */
    static List<String> lookUp() throws Exception {
        var manifest = JsonParser.parseString(text(MANIFEST)).getAsJsonObject();
        var releases = new java.util.ArrayList<JsonObject>();
        for (var entry : manifest.getAsJsonArray("versions")) {
            var item = entry.getAsJsonObject();
            if (!"release".equals(item.get("type").getAsString()) || !valid(item.get("id").getAsString())) continue;
            releases.add(item);
            if (item.get("id").getAsString().equals("1.7.10")) break;
        }
        var pool = java.util.concurrent.Executors.newFixedThreadPool(6, r -> { var t = new Thread(r, "Vortex panorama lookup"); t.setDaemon(true); return t; });
        try {
            // Version file -> asset index (id, url, sha1).
            var indexOf = new java.util.ArrayList<java.util.concurrent.Future<String[]>>();
            for (var release : releases) indexOf.add(pool.submit(() -> {
                String url = release.get("url").getAsString();
                if (!mojang(url)) return null;
                String body = text(url);
                if (!sha1Hex(body.getBytes(java.nio.charset.StandardCharsets.UTF_8)).equals(release.get("sha1").getAsString())) return null;
                var index = JsonParser.parseString(body).getAsJsonObject().getAsJsonObject("assetIndex");
                return index == null ? null : new String[] {index.get("id").getAsString(), index.get("url").getAsString(), index.get("sha1").getAsString()};
            }));
            var faces = new java.util.HashMap<String, java.util.concurrent.Future<String>>();
            var indexes = new java.util.ArrayList<String[]>();
            for (var future : indexOf) {
                String[] index = future.get();
                indexes.add(index);
                if (index == null || faces.containsKey(index[0]) || !mojang(index[1])) continue;
                faces.put(index[0], pool.submit(() -> {
                    String body = text(index[1]);
                    if (!sha1Hex(body.getBytes(java.nio.charset.StandardCharsets.UTF_8)).equals(index[2])) return null;
                    var objects = JsonParser.parseString(body).getAsJsonObject().getAsJsonObject("objects");
                    var face = objects == null ? null : objects.getAsJsonObject("minecraft/" + PANORAMA + "_0.png");
                    return face == null ? null : face.get("hash").getAsString();
                }));
            }
            // Newest first: a later (older) release with the same panorama takes over the label.
            var label = new java.util.LinkedHashMap<String, String>();
            for (int i = 0; i < releases.size(); i++) {
                String[] index = indexes.get(i);
                String hash = index == null ? null : faces.get(index[0]).get();
                if (hash != null) label.put(hash, releases.get(i).get("id").getAsString());
            }
            return new java.util.ArrayList<>(label.values());
        } finally { pool.shutdownNow(); }
    }
    /** Only Mojang's own HTTPS hosts are used. */
    static boolean mojang(String url) {
        try {
            var uri = URI.create(url);
            String host = uri.getHost() == null ? "" : uri.getHost().toLowerCase(Locale.ROOT);
            return "https".equals(uri.getScheme()) && (host.equals("piston-meta.mojang.com") || host.equals("piston-data.mojang.com") || host.equals("launchermeta.mojang.com")
                || host.equals("launcher.mojang.com") || host.equals("resources.download.minecraft.net"));
        } catch (Exception bad) { return false; }
    }
    private static String text(String url) throws Exception {
        var response = HTTP.send(HttpRequest.newBuilder(URI.create(url)).timeout(Duration.ofSeconds(20)).build(), HttpResponse.BodyHandlers.ofString());
        if (response.statusCode() != 200 || response.body().length() > 8_000_000) throw new IllegalStateException("HTTP " + response.statusCode());
        return response.body();
    }
    private static void download(String url, Path target, long size, String sha1) throws Exception { download(url, target, size, sha1, 0, size); }
    private static void download(String url, Path target, long size, String sha1, long before, long total) throws Exception {
        var response = HTTP.send(HttpRequest.newBuilder(URI.create(url)).timeout(Duration.ofMinutes(5)).build(), HttpResponse.BodyHandlers.ofInputStream());
        if (response.statusCode() != 200) throw new IllegalStateException("HTTP " + response.statusCode());
        var digest = MessageDigest.getInstance("SHA-1");
        long read = 0;
        try (InputStream in = response.body(); var out = Files.newOutputStream(target)) {
            byte[] buffer = new byte[65536];
            for (int n; (n = in.read(buffer)) > 0; ) {
                read += n;
                if (read > size) throw new SecurityException("Client jar is larger than announced");
                digest.update(buffer, 0, n); out.write(buffer, 0, n);
                progress = (int)((before + read) * 100 / Math.max(1, total));
            }
        }
        if (read != size || !HexFormat.of().formatHex(digest.digest()).equals(sha1)) throw new SecurityException("Client jar checksum mismatch");
    }
    static String sha1Hex(byte[] data) throws Exception { return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-1").digest(data)); }
    /** Copies panorama_0..5.png out of a client jar; all six must be readable PNGs of one size, or nothing is kept. */
    static void extract(Path jar, Path destination) throws Exception {
        Path staging = destination.resolveSibling(destination.getFileName() + ".staging");
        deleteTree(staging); Files.createDirectories(staging);
        try (var zip = new ZipFile(jar.toFile())) {
            for (int i = 0; i < 6; i++) {
                var entry = zip.getEntry("assets/minecraft/" + PANORAMA + "_" + i + ".png");
                if (entry == null || entry.getSize() > MAX_IMAGE) throw new IllegalStateException("Panorama image missing");
                byte[] bytes;
                try (var in = zip.getInputStream(entry)) { bytes = in.readNBytes((int)MAX_IMAGE + 1); }
                if (bytes.length > MAX_IMAGE) throw new IllegalStateException("Panorama image too large");
                Files.write(staging.resolve("panorama_" + i + ".png"), bytes);
            }
            checkFaces(staging);
            deleteTree(destination);
            Files.move(staging, destination, StandardCopyOption.ATOMIC_MOVE);
        } finally { deleteTree(staging); }
    }
    private static void deleteTree(Path path) throws java.io.IOException {
        if (!Files.exists(path)) return;
        try (var walk = Files.walk(path)) {
            for (var item : walk.sorted(java.util.Comparator.reverseOrder()).toList()) Files.deleteIfExists(item);
        }
    }
}
