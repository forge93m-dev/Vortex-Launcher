package dev.vortex.client;

import com.google.gson.*;
import java.io.*;
import java.nio.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;

public final class DiscordRpcSmoke {
    static void check(boolean value,String name){if(!value)throw new AssertionError(name);System.out.println("PASS "+name);}
    static void await(java.util.function.BooleanSupplier condition,String name)throws Exception{
        long end=System.nanoTime()+TimeUnit.SECONDS.toNanos(7);
        while(!condition.getAsBoolean()&&System.nanoTime()<end)Thread.sleep(10);
        check(condition.getAsBoolean(),name);
    }
    static final class Fake implements DiscordRpc.Transport {
        final BlockingQueue<Integer> incoming=new LinkedBlockingQueue<>();
        final List<JsonObject> activities=new CopyOnWriteArrayList<>();
        volatile boolean closed,pong,handshake,reject;
        void queue(int op,byte[] bytes)throws IOException{for(byte b:DiscordRpc.frame(op,bytes))incoming.add(b&255);}
        public int read(byte[] bytes,int offset,int length)throws IOException{
            try{int first=incoming.take();if(first==-1)return -1;bytes[offset]=(byte)first;int count=1;
                while(count<Math.min(length,3)){Integer next=incoming.poll();if(next==null)break;if(next==-1){incoming.add(-1);break;}bytes[offset+count++]=(byte)(int)next;}
                return count;
            }catch(InterruptedException ex){Thread.currentThread().interrupt();throw new IOException(ex);}
        }
        public void write(byte[] bytes)throws IOException{
            var frame=ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN);int op=frame.getInt(),length=frame.getInt();
            check(length==bytes.length-8,"one complete little-endian frame per write");
            byte[] payload=Arrays.copyOfRange(bytes,8,bytes.length);
            if(op==4){pong=Arrays.equals(payload,new byte[]{1,2,3});return;}
            var json=JsonParser.parseString(new String(payload,StandardCharsets.UTF_8)).getAsJsonObject();
            if(op==0){handshake=DiscordRpc.APPLICATION_ID.equals(json.get("client_id").getAsString())&&json.get("v").getAsInt()==1;
                queue(1,"{\"cmd\":\"DISPATCH\",\"evt\":\"READY\",\"data\":{}}".getBytes(StandardCharsets.UTF_8));queue(3,new byte[]{1,2,3});
            }else{
                activities.add(json);
                var reply=new JsonObject();reply.addProperty("cmd","SET_ACTIVITY");reply.addProperty("nonce",json.get("nonce").getAsString());
                if(reject)reply.addProperty("evt","ERROR");else reply.add("evt",JsonNull.INSTANCE);
                queue(1,reply.toString().getBytes(StandardCharsets.UTF_8));
            }
        }
        public void close(){closed=true;incoming.add(-1);}
    }
    public static void main(String[] args)throws Exception{
        check(DiscordRpc.APPLICATION_ID.equals("1547929448165281924"),"public application ID matches the owner's supplied ID");
        check(DiscordRpc.windowsPipePath(0).equals("\\\\.\\pipe\\discord-ipc-0") && DiscordRpc.windowsPipePath(9).equals("\\\\.\\pipe\\discord-ipc-9"),"Windows uses the named-pipe device namespace");
        for(var mode:DiscordRpc.Mode.values()){
            var json=DiscordRpc.activityPayload(new DiscordRpc.Activity("26.3",mode),123,456,"fixture");
            check(json.get("cmd").getAsString().equals("SET_ACTIVITY")&&json.getAsJsonObject("args").get("pid").getAsLong()==456,"activity includes command and PID");
            var activity=json.getAsJsonObject("args").getAsJsonObject("activity");
            check(activity.keySet().equals(Set.of("type","details","state","timestamps","assets")),"default privacy allowlist has no server, nickname, coordinates, world or join secret");
            check(activity.getAsJsonObject("assets").get("large_image").getAsString().equals("vortex"),"fixed portal icon key");
        }
        long inviteNow=System.currentTimeMillis()/1000;
        var invite=new ServerInvite("26.3","MC.Example.org:25565",inviteNow+1800,"0123456789abcdef");
        String token=invite.encode();
        check(token.length()<=128 && ServerInvite.decode(token,inviteNow).address().equals("mc.example.org:25565"),"bounded canonical invitation round trip");
        var shared=DiscordRpc.activityPayload(new DiscordRpc.Activity("26.3",DiscordRpc.Mode.MULTIPLAYER,token),123,456,"invite-fixture").getAsJsonObject("args").getAsJsonObject("activity");
        check(shared.getAsJsonObject("secrets").get("join").getAsString().equals(token) && !shared.get("state").getAsString().contains("example"),"explicit opt-in adds join secret, not address to visible state");
        boolean menuBlocked=false;try{new DiscordRpc.Activity("26.3",DiscordRpc.Mode.MENU,token);}catch(IllegalArgumentException ex){menuBlocked=true;}
        check(menuBlocked,"main menu cannot publish server invitation");
        for(String bad:List.of(token+"=","https://example.org",new ServerInvite("26.3","mc.example.org",inviteNow-1,"0123456789abcdef").encode(),new ServerInvite("26.3","mc.example.org",inviteNow+10000,"0123456789abcdef").encode())){
            boolean rejectedInvite=false;try{ServerInvite.decode(bad,inviteNow);}catch(IllegalArgumentException ex){rejectedInvite=true;}check(rejectedInvite,"malformed or expired invitation rejected");
        }
        for(String bad:List.of("https://example.org","host/path","host --flag", "user@host", "host:0", "host:65536","host:","[not-ip]","host\nnext")){
            boolean rejectedAddress=false;try{ServerInvite.normalizeAddress(bad);}catch(IllegalArgumentException ex){rejectedAddress=true;}check(rejectedAddress,"unsafe address rejected");
        }
        check(ServerInvite.normalizeAddress("[2001:db8::1]:25565").equals("[2001:db8::1]:25565"),"IPv6 invitation address");
        String vector=new ServerInvite("26.3","mc.example.org:25565",2000001800L,"0123456789abcdef").encode();
        check(vector.equals("vxi1.MjYuM3xtYy5leGFtcGxlLm9yZzoyNTU2NXx4MnF6OWt8MDEyMzQ1Njc4OWFiY2RlZg"),"shared Java/.NET protocol vector");
        boolean invalid=false;try{new DiscordRpc.Activity("26.3\nsecret",DiscordRpc.Mode.MENU);}catch(IllegalArgumentException ex){invalid=true;}
        check(invalid,"unexpected version text is rejected");
        var fake=new Fake();var calls=new AtomicInteger();
        try(var rpc=new DiscordRpc(()->{calls.incrementAndGet();return fake;})){
            Thread.sleep(1100);check(calls.get()==0,"disabled presence does not contact Discord");
            rpc.update(true,new DiscordRpc.Activity("26.3",DiscordRpc.Mode.MENU));
            await(()->rpc.status().startsWith("Connected"),"only an acknowledged SET_ACTIVITY is reported as connected");
            await(()->fake.pong,"fragmented frames and ping/pong work");
            check(fake.handshake&&fake.activities.size()==1,"handshake uses the supplied application ID before publishing");
            for(int i=0;i<100;i++)rpc.update(true,new DiscordRpc.Activity("26.3",DiscordRpc.Mode.MULTIPLAYER));
            Thread.sleep(1200);check(fake.activities.size()==1,"rapid changes are coalesced rather than flooding Discord");
            rpc.update(false,new DiscordRpc.Activity("26.3",DiscordRpc.Mode.MENU));
            await(()->fake.closed,"turning presence off closes the IPC connection");
            check(rpc.status().equals("Disabled"),"disabled state does not retain a connected badge");
        }
        var rejected=new Fake();rejected.reject=true;
        try(var rpc=new DiscordRpc(()->rejected)){
            rpc.update(true,new DiscordRpc.Activity("26.3",DiscordRpc.Mode.SINGLEPLAYER));
            await(()->rpc.status().contains("rejected"),"Discord rejection is not falsely shown as success");
        }
        var missingCalls=new AtomicInteger();
        try(var rpc=new DiscordRpc(()->{missingCalls.incrementAndGet();throw new IOException("fixture unavailable");})){
            rpc.update(true,new DiscordRpc.Activity("26.3",DiscordRpc.Mode.MENU));
            await(()->rpc.status().contains("unavailable"),"missing Discord is a recoverable status, not a game error");
            Thread.sleep(1200);check(missingCalls.get()==1,"unavailable Discord uses retry backoff");
        }
        for(byte[] bad:List.of(new byte[]{1},ByteBuffer.allocate(8).order(ByteOrder.LITTLE_ENDIAN).putInt(1).putInt(65537).array(),ByteBuffer.allocate(8).order(ByteOrder.LITTLE_ENDIAN).putInt(99).putInt(0).array())){
            var input=new ByteArrayInputStream(bad);boolean blocked=false;
            try{DiscordRpc.readFrame(new DiscordRpc.Transport(){public int read(byte[]b,int o,int l){return input.read(b,o,l);}public void write(byte[]b){}public void close(){}});}catch(IOException ex){blocked=true;}
            check(blocked,"truncated, oversized or unknown frames fail safely");
        }
        System.out.println("DISCORD_RPC_SMOKE_OK (fake local transport only; no actual Discord status changed)");
    }
}
