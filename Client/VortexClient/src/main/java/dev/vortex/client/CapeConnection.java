package dev.vortex.client;

import com.google.gson.Gson;
import com.google.gson.JsonObject;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.TimeUnit;

public final class CapeConnection {
    public static final String ORIGIN = "https://vortex-cosmetics.forge93-m.workers.dev";
    private static final Gson JSON = new Gson();
    private static final HttpClient HTTP = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(4))
        .followRedirects(HttpClient.Redirect.NEVER).build();
    private final URI base;
    public CapeConnection() { this(ORIGIN); }
    public CapeConnection(String url) {
        base = PartyConnection.endpoint(url);
        boolean fixture = "http".equals(base.getScheme()) && List.of("localhost","127.0.0.1","[::1]").contains(base.getHost());
        if (!fixture && !(ORIGIN + "/").equals(base.toString())) throw new IllegalArgumentException("Untrusted cape service address.");
    }
    public static String id(UUID id) { return id.toString().replace("-", ""); }
    public static UUID uuid(String text) {
        if (text == null || !text.matches("[a-fA-F0-9]{32}")) throw new IllegalArgumentException("Invalid cape profile.");
        return UUID.fromString(text.substring(0,8)+"-"+text.substring(8,12)+"-"+text.substring(12,16)+"-"+text.substring(16,20)+"-"+text.substring(20));
    }
    public Map<UUID,String> lookup(List<UUID> ids) throws Exception {
        if (ids.size() > 32) throw new IllegalArgumentException("Too many cape profiles.");
        var result = request("POST", "v1/lookup", "", Map.of("uuids", ids.stream().map(CapeConnection::id).toList(),"custom",true,"hd",true));
        if (!result.has("capes") || !result.get("capes").isJsonArray() || result.getAsJsonArray("capes").size() > 32)
            throw new IllegalArgumentException("Invalid cape response.");
        var capes = new HashMap<UUID,String>();
        for (var item : result.getAsJsonArray("capes")) {
            var row = item.getAsJsonObject(); UUID player = uuid(row.get("uuid").getAsString());
            String cape = row.get("cape").getAsString();
            if (!ids.contains(player) || !CapeCatalog.valid(cape) || capes.put(player,cape) != null)
                throw new IllegalArgumentException("Invalid cape response.");
        }
        return Map.copyOf(capes);
    }
    public void claim(UUID id, String code, String owner) throws Exception {
        if (!secret(code) || !secret(owner)) throw new IllegalArgumentException("Paste the 64-character activation code.");
        var result = request("POST", "v1/claim", "", Map.of("uuid",id(id),"code",code,"owner",owner));
        if (!result.has("claimed") || !result.get("claimed").getAsBoolean()) throw new IllegalArgumentException("Invalid activation response.");
    }
    public void select(UUID id, String cape, String owner) throws Exception {
        if (!CapeCatalog.valid(cape) || CapeCatalog.custom(cape) || !secret(owner)) throw new IllegalArgumentException("Activate this profile first; custom capes must be uploaded.");
        var result = request("PUT","v1/cape",owner,Map.of("uuid",id(id),"cape",cape));
        if (!result.has("saved") || !result.get("saved").getAsBoolean() || !cape.equals(result.get("cape").getAsString()))
            throw new IllegalArgumentException("Invalid save response.");
    }
    public void upload(UUID id, byte[] pixels, String owner) throws Exception {
        String cape=CapePixels.id(pixels);
        CapePixels.unpack(pixels);
        if(!secret(owner)) throw new IllegalArgumentException("Activate this profile first.");
        var result=request("PUT",CapePixels.hd(pixels)?"v2/custom":"v1/custom",owner,Map.of("uuid",id(id),"pixels",java.util.Base64.getEncoder().encodeToString(pixels)));
        if(!result.has("saved") || !result.get("saved").getAsBoolean() || !cape.equals(result.get("cape").getAsString()))
            throw new IllegalArgumentException("Invalid upload response.");
    }
    public byte[] download(String cape) throws Exception {
        if(!CapeCatalog.custom(cape)) throw new IllegalArgumentException("Invalid custom cape ID.");
        boolean hd=cape.startsWith("hd:");
        var builder=HttpRequest.newBuilder(base.resolve((hd?"v2":"v1")+"/textures/"+cape.substring(cape.indexOf(':')+1)))
            .timeout(Duration.ofSeconds(12)).header("Accept","application/octet-stream").GET();
        var response=send(builder,hd?CapePixels.MAX_PACKED:65536);
        byte[] pixels=response.body();
        if(!cape.equals(CapePixels.id(pixels))) throw new IllegalArgumentException("Cape checksum mismatch.");
        CapePixels.unpack(pixels);
        return pixels;
    }
    public static boolean secret(String value) { return value != null && value.matches("[a-f0-9]{64}"); }
    private JsonObject request(String method, String path, String token, Object body) throws Exception {
        var builder = HttpRequest.newBuilder(base.resolve(path)).timeout(Duration.ofSeconds(path.startsWith("v2/")?12:5))
            .header("Accept","application/json").header("Content-Type","application/json")
            .method(method,HttpRequest.BodyPublishers.ofString(JSON.toJson(body)));
        if (!token.isEmpty()) builder.header("Authorization","Bearer " + token);
        var response=send(builder);
        if (response.body().length > 16384) throw new IllegalArgumentException("Oversized cape response.");
        var result = JSON.fromJson(new String(response.body(),java.nio.charset.StandardCharsets.UTF_8),JsonObject.class);
        if (result == null) throw new IllegalArgumentException("Invalid cape response.");
        return result;
    }
    private HttpResponse<byte[]> send(HttpRequest.Builder builder) throws Exception {
        return send(builder,65536);
    }
    private HttpResponse<byte[]> send(HttpRequest.Builder builder,int limit) throws Exception {
        var pending = HTTP.sendAsync(builder.build(), info -> new PartyConnection.BoundedBody(limit));
        HttpResponse<byte[]> response;
        try { response = pending.get(13, TimeUnit.SECONDS); }
        catch (Exception error) { pending.cancel(true); throw error; }
        if (response.statusCode() != 200) throw new IllegalStateException(switch(response.statusCode()) {
            case 401,403 -> "Activation code or profile key was rejected. Check the in-game UUID.";
            case 429 -> "Cape service is busy. Wait a minute.";
            default -> "Cape service unavailable (" + response.statusCode() + ").";
        });
        return response;
    }
}
