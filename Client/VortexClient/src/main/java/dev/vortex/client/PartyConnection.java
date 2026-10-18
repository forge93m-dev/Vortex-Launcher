package dev.vortex.client;

import com.google.gson.Gson;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;
import java.util.List;
import java.util.Map;
import java.nio.ByteBuffer;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;
import java.util.concurrent.Flow;
import java.util.concurrent.TimeUnit;

/** Bounded, explicit HTTP transport. No game packets, redirects, telemetry or persisted tokens. */
public final class PartyConnection {
    private static final Gson JSON = new Gson();
    private static final HttpClient HTTP = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(4))
        .followRedirects(HttpClient.Redirect.NEVER).build();
    public record Member(String memberId, String name, boolean owner) { }
    public record Marker(String memberId, String name, String serverKey, String dimension, int x, int y, int z, String kind, String expiresUtc) { }
    public record Snapshot(List<Member> members, List<Marker> markers) { }
    public record Session(String partyId, String token, String invite, Snapshot snapshot) { }
    public static final class Failure extends Exception {
        public final int status;
        Failure(int status, String message) { super(message); this.status = status; }
    }
    private final URI base;
    public PartyConnection(String url) { base = endpoint(url); }
    public static URI endpoint(String input) {
        try {
            URI uri = URI.create(input.trim());
            String host = uri.getHost();
            boolean local = "127.0.0.1".equals(host) || "localhost".equalsIgnoreCase(host) || "[::1]".equals(host);
            if (host == null || uri.getRawUserInfo() != null || uri.getRawQuery() != null || uri.getRawFragment() != null ||
                !(uri.getPath().isEmpty() || uri.getPath().equals("/")) ||
                !("https".equalsIgnoreCase(uri.getScheme()) || local && "http".equalsIgnoreCase(uri.getScheme())))
                throw new IllegalArgumentException();
            return URI.create(uri.toString().replaceAll("/+$", "") + "/");
        } catch (RuntimeException ex) { throw new IllegalArgumentException("Enter an HTTPS service URL (HTTP is allowed only on localhost)."); }
    }
    public Session create(String name) throws Exception { return request("POST", "v1/parties", "", Map.of("name", name), Session.class); }
    public Session join(String invite, String name) throws Exception {
        if (!invite.matches("[a-f0-9]{32}")) throw new IllegalArgumentException("Paste a 32-character invite code.");
        return request("POST", "v1/join", "", Map.of("invite", invite, "name", name), Session.class);
    }
    public Snapshot read(String token) throws Exception { return request("GET", "v1/session", token, null, Snapshot.class); }
    public Snapshot mark(String token, String server, String dimension, int x, int y, int z, String kind) throws Exception {
        return request("POST", "v1/markers", token, Map.of("serverKey", server, "dimension", dimension, "x", x, "y", y, "z", z, "kind", kind), Snapshot.class);
    }
    public void leave(String token) throws Exception { request("DELETE", "v1/session", token, null, Object.class); }
    private <T> T request(String method, String path, String token, Object body, Class<T> type) throws Exception {
        var builder = HttpRequest.newBuilder(base.resolve(path)).timeout(Duration.ofSeconds(5)).header("Accept", "application/json");
        if (!token.isEmpty()) builder.header("Authorization", "Bearer " + token);
        builder.method(method, body == null ? HttpRequest.BodyPublishers.noBody() : HttpRequest.BodyPublishers.ofString(JSON.toJson(body)));
        if (body != null) builder.header("Content-Type", "application/json");
        var pending = HTTP.sendAsync(builder.build(), info -> new BoundedBody());
        HttpResponse<byte[]> response;
        try { response = pending.get(6, TimeUnit.SECONDS); }
        catch (Exception ex) { pending.cancel(true); throw ex; }
        String text = new String(response.body(), java.nio.charset.StandardCharsets.UTF_8);
        if (response.statusCode() != 200) {
            // Do not display arbitrary HTML or exception details from an untrusted service.
            String message = switch (response.statusCode()) {
                case 401 -> "Party session expired. Leave and join again.";
                case 404 -> "Invite is invalid or expired.";
                case 409 -> "Party is full or this name is already in use.";
                case 429 -> "Too many requests. Wait a moment.";
                default -> "Party service request failed (" + response.statusCode() + ").";
            };
            throw new Failure(response.statusCode(), message);
        }
        T result = JSON.fromJson(text, type);
        if (result == null) throw new Failure(502, "Invalid party response.");
        return result;
    }
    static final class BoundedBody implements HttpResponse.BodySubscriber<byte[]> {
        private final int limit;
        BoundedBody() {this(65536);}
        BoundedBody(int limit) {this.limit=limit;}
        private final CompletableFuture<byte[]> result = new CompletableFuture<>();
        private final java.io.ByteArrayOutputStream buffer = new java.io.ByteArrayOutputStream();
        private Flow.Subscription subscription;
        @Override public CompletionStage<byte[]> getBody() { return result; }
        @Override public void onSubscribe(Flow.Subscription value) { subscription = value; value.request(1); }
        @Override public void onNext(List<ByteBuffer> chunks) {
            for (var chunk : chunks) {
                if (chunk.remaining() > limit - buffer.size()) {
                    subscription.cancel(); result.completeExceptionally(new Failure(502, "Oversized party response.")); return;
                }
                byte[] bytes = new byte[chunk.remaining()]; chunk.get(bytes); buffer.writeBytes(bytes);
            }
            subscription.request(1);
        }
        @Override public void onError(Throwable error) { result.completeExceptionally(error); }
        @Override public void onComplete() { result.complete(buffer.toByteArray()); }
    }
}
