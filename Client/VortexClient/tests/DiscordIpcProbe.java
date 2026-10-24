package dev.vortex.client;

import com.google.gson.JsonParser;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.concurrent.*;

/** Explicit manual diagnostic: handshake + IPC ping only; never SET_ACTIVITY or user data output. */
public final class DiscordIpcProbe {
    public static void main(String[] args) {
        var work = Executors.newCachedThreadPool(r -> { var t = new Thread(r,"Discord IPC probe"); t.setDaemon(true); return t; });
        int result = 1;
        try {
            var transport = DiscordRpc.connectDesktop();
            transport.write(DiscordRpc.frame(0,("{\"v\":1,\"client_id\":\""+DiscordRpc.APPLICATION_ID+"\"}").getBytes(StandardCharsets.UTF_8)));
            var first = work.submit(() -> DiscordRpc.readFrame(transport)).get(5,TimeUnit.SECONDS);
            if(first.opcode()!=1 || !JsonParser.parseString(new String(first.bytes(),StandardCharsets.UTF_8)).getAsJsonObject().get("evt").getAsString().equals("READY"))
                throw new IllegalStateException("Handshake was not acknowledged");
            System.out.println("PASS native Discord IPC handshake READY (no activity submitted)");
            byte[] ping = "{\"probe\":true}".getBytes(StandardCharsets.UTF_8);
            var read = work.submit(() -> {
                while(true) {
                    var frame = DiscordRpc.readFrame(transport);
                    if(frame.opcode()==3) { transport.write(DiscordRpc.frame(4,frame.bytes())); continue; }
                    if(frame.opcode()==4) return Arrays.equals(frame.bytes(),ping);
                    if(frame.opcode()==2) {
                        var close = JsonParser.parseString(new String(frame.bytes(),StandardCharsets.UTF_8)).getAsJsonObject();
                        System.out.println("Discord probe CLOSE code="+(close.has("code")?close.get("code").getAsInt():-1));
                        return false;
                    }
                }
            });
            Thread.sleep(200); // Ensure a real blocking pipe read is pending before writing.
            work.submit(() -> { transport.write(DiscordRpc.frame(3,ping)); return null; }).get(3,TimeUnit.SECONDS);
            if(!read.get(3,TimeUnit.SECONDS)) throw new IllegalStateException("Ping not acknowledged");
            System.out.println("PASS native pipe reads and writes operate concurrently; PONG received");
            var pending = work.submit(() -> { try { DiscordRpc.readFrame(transport); } catch(java.io.IOException expected) { return true; } return false; });
            Thread.sleep(200);
            work.submit(() -> { transport.close(); return null; }).get(2,TimeUnit.SECONDS);
            if(!pending.get(2,TimeUnit.SECONDS)) throw new IllegalStateException("Pending read did not stop");
            System.out.println("PASS close cancels and drains a pending native read without blocking");
            System.out.println("DISCORD_NATIVE_IPC_OK (no actual activity/status changed)"); result=0;
        } catch(Exception ex) {
            System.out.println("DISCORD_NATIVE_IPC_FAILED: "+ex.getClass().getSimpleName()+" "+ex.getMessage());
        } finally { work.shutdownNow(); }
        // A diagnostic must terminate even if a broken native synchronous read cannot be interrupted.
        System.exit(result);
    }
}
