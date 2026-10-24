package dev.vortex.client;

import java.nio.charset.StandardCharsets;
import java.util.Base64;
import java.util.Locale;

/** Untrusted routing hint, not an identity or an encrypted/authenticated capability. */
public record ServerInvite(String version, String address, long expires, String nonce) {
    public ServerInvite {
        if (version == null || !version.matches("[0-9][A-Za-z0-9_.+-]{0,23}") || nonce == null || !nonce.matches("[a-f0-9]{16}"))
            throw new IllegalArgumentException("Invalid server invitation.");
        address = normalizeAddress(address);
    }
    public static String normalizeAddress(String address) {
        if (address == null || address.length() > 253 || !address.equals(address.trim())) throw new IllegalArgumentException("Invalid server address.");
        String host, port = "";
        if (address.startsWith("[")) {
            int end = address.indexOf(']');
            if (end < 0) throw new IllegalArgumentException("Invalid IPv6 address.");
            host = address.substring(0, end + 1);
            if (!host.matches("\\[[0-9a-fA-F:]+\\]")) throw new IllegalArgumentException("Invalid IPv6 address.");
            try { if (!(java.net.InetAddress.getByName(host.substring(1, host.length()-1)) instanceof java.net.Inet6Address)) throw new IllegalArgumentException(); }
            catch (Exception ex) { throw new IllegalArgumentException("Invalid IPv6 address."); }
            if (end + 1 < address.length()) { if (address.charAt(end + 1) != ':') throw new IllegalArgumentException("Invalid port."); port = address.substring(end + 2); if (port.isEmpty()) throw new IllegalArgumentException("Invalid port."); }
        } else {
            int colon = address.indexOf(':');
            host = colon < 0 ? address : address.substring(0, colon);
            if (colon >= 0) { port = address.substring(colon + 1); if (port.isEmpty()) throw new IllegalArgumentException("Invalid port."); }
            if (host.isEmpty() || host.length() > 253) throw new IllegalArgumentException("Invalid hostname.");
            for (String label : host.split("\\.", -1)) if (!label.matches("[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?")) throw new IllegalArgumentException("Invalid hostname.");
        }
        if (!port.isEmpty()) {
            if (!port.matches("[0-9]{1,5}")) throw new IllegalArgumentException("Invalid port.");
            int number = Integer.parseInt(port);
            if (number < 1 || number > 65535) throw new IllegalArgumentException("Invalid port.");
            port = Integer.toString(number);
        }
        return host.toLowerCase(Locale.ROOT) + (port.isEmpty() ? "" : ":" + port);
    }
    public String encode() {
        String payload = version + "|" + address + "|" + Long.toString(expires, 36) + "|" + nonce;
        String result = "vxi1." + Base64.getUrlEncoder().withoutPadding().encodeToString(payload.getBytes(StandardCharsets.US_ASCII));
        if (result.length() > 128) throw new IllegalArgumentException("Server address is too long for a Discord invitation.");
        return result;
    }
    public static ServerInvite decode(String token, long now) {
        try {
            if (token == null || token.length() > 128 || !token.matches("vxi1\\.[A-Za-z0-9_-]+")) throw new IllegalArgumentException();
            String[] parts = new String(Base64.getUrlDecoder().decode(token.substring(5)), StandardCharsets.US_ASCII).split("\\|", -1);
            if (parts.length != 4 || !parts[2].matches("[0-9a-z]{1,10}")) throw new IllegalArgumentException();
            var invite = new ServerInvite(parts[0], parts[1], Long.parseLong(parts[2], 36), parts[3]);
            if (invite.expires <= now || invite.expires - now > 1860 || !invite.encode().equals(token)) throw new IllegalArgumentException();
            return invite;
        } catch (RuntimeException ex) { throw new IllegalArgumentException("Invalid or expired server invitation."); }
    }
}
