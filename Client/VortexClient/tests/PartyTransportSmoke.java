import dev.vortex.client.PartyConnection;

public final class PartyTransportSmoke {
    public static void main(String[] args) throws Exception {
        for (String unsafe : new String[] {"http://example.com", "https://user:pass@example.com", "https://example.com/?token=x", "file:///tmp/test", "https://example.com/path"}) {
            try { PartyConnection.endpoint(unsafe); throw new AssertionError("Unsafe endpoint accepted"); }
            catch (IllegalArgumentException expected) { }
        }
        var first = new PartyConnection(args[0]); var second = new PartyConnection(args[0]);
        var owner = first.create("JavaOwner"); var guest = second.join(owner.invite(), "JavaGuest");
        if (owner.token().equals(guest.token()) || second.read(guest.token()).members().size() != 2) throw new AssertionError("Group/session handshake");
        first.mark(owner.token(), "a".repeat(64), "minecraft:overworld", 42, 70, -10, "HERE");
        var marks = second.read(guest.token()).markers();
        if (marks.size() != 1 || marks.getFirst().x() != 42 || !marks.getFirst().name().equals("JavaOwner")) throw new AssertionError("Shared point lost");
        try { second.read(owner.invite()); throw new AssertionError("Invite used as token"); }
        catch (PartyConnection.Failure expected) { if (expected.status != 401) throw expected; }
        first.leave(owner.token());
        try { second.read(guest.token()); throw new AssertionError("Closed party remains usable"); }
        catch (PartyConnection.Failure expected) { if (expected.status != 401) throw expected; }
        try (var fixture = new TransportFixture()) {
            try { new PartyConnection(fixture.url()).create("JavaOwner"); throw new AssertionError("Oversized response accepted"); }
            catch (java.util.concurrent.ExecutionException expected) { }
            fixture.mode = 1;
            try { new PartyConnection(fixture.url()).create("JavaOwner"); throw new AssertionError("Redirect was followed"); }
            catch (PartyConnection.Failure expected) { if (expected.status != 302) throw expected; }
            if (fixture.trap != 0) throw new AssertionError("Redirect reached credential trap");
        }
        System.out.println("PASS Java transport: endpoint safety, two independent members, private markers, credentials and owner close");
        System.out.println("PASS Java transport: response size bound and no credential-bearing redirects");
    }
    private static final class TransportFixture implements AutoCloseable {
        final com.sun.net.httpserver.HttpServer server;
        volatile int mode, trap;
        TransportFixture() throws Exception {
            server = com.sun.net.httpserver.HttpServer.create(new java.net.InetSocketAddress("127.0.0.1", 0), 0);
            server.createContext("/v1/parties", exchange -> {
                if (mode == 1) { exchange.getResponseHeaders().add("Location", url() + "/trap"); exchange.sendResponseHeaders(302, -1); }
                else {
                    byte[] bytes = "x".repeat(70000).getBytes(java.nio.charset.StandardCharsets.UTF_8);
                    exchange.sendResponseHeaders(200, bytes.length);
                    try { exchange.getResponseBody().write(bytes); } catch (java.io.IOException ignored) { }
                }
                exchange.close();
            });
            server.createContext("/trap", exchange -> { trap++; exchange.sendResponseHeaders(500, -1); exchange.close(); });
            server.start();
        }
        String url() { return "http://127.0.0.1:" + server.getAddress().getPort(); }
        @Override public void close() { server.stop(0); }
    }
}
