import java.lang.reflect.*;
import java.nio.file.*;
import java.net.*;
import java.util.*;
import java.util.concurrent.*;
import com.sun.net.httpserver.HttpServer;

public final class CapeSmoke {
    public static void run(ClassLoader target, Path root) throws Exception {
        try (var input=target.getResourceAsStream("assets/vortex_client/textures/ui/icons/brand.png")) {
            var logo=javax.imageio.ImageIO.read(input);
            int visible=0, transparent=0, highlights=0, shadows=0, neutral=0;
            if(logo.getWidth()!=1254 || logo.getHeight()!=1254) throw new AssertionError("White logo resolution");
            for(int yy=0;yy<1254;yy++)for(int xx=0;xx<1254;xx++) {
                int pixel=logo.getRGB(xx,yy);
                if((pixel>>>24)==0) transparent++;
                else {
                    visible++;
                    int r=(pixel>>>16)&255, green=(pixel>>>8)&255, b=pixel&255;
                    if(Math.max(r,Math.max(green,b))-Math.min(r,Math.min(green,b))<24)neutral++;
                    if(r>210 && green>210 && b>210)highlights++;
                    if(r<140 && green<140 && b<140)shadows++;
                }
            }
            if(visible<20000 || transparent<20000)throw new AssertionError("Brand is blank or lacks transparent background");
            if(highlights<10000 || shadows<10000 || neutral<visible*.97)throw new AssertionError("White logo lost ribbon shading or retained a color cast");
        }
        PhotoSmoke.run(target,root);
        Class<?> configType=Class.forName("dev.vortex.client.ClientConfig",true,target);
        Object config=Class.forName("dev.vortex.client.VortexClient",true,target).getField("config").get(null);
        if(configType.getField("capesEnabled").getBoolean(config)) throw new AssertionError("Capes must be opt-in");
        Class<?> player=Class.forName("net.minecraft.client.player.AbstractClientPlayer",false,target);
        if(Arrays.stream(player.getDeclaredMethods()).noneMatch(m->m.getName().contains("vortex$cape"))) throw new AssertionError("Missing real cape skin mixin");
        Class<?> entry=Class.forName("net.minecraft.client.multiplayer.PlayerInfo",false,target);
        if(Arrays.stream(entry.getDeclaredMethods()).noneMatch(m->m.getName().contains("vortex$skin"))) throw new AssertionError("Missing tab-list skin mixin (chat heads would show the default skin)");
        Class<?> calls=Class.forName("dev.vortex.client.SkinCalls",true,target);
        Method enter=calls.getMethod("enter"),leave=calls.getMethod("leave"),inside=calls.getMethod("inside");
        if((boolean)inside.invoke(null)) throw new AssertionError("Skin call guard starts closed");
        enter.invoke(null);enter.invoke(null);if(!(boolean)inside.invoke(null)) throw new AssertionError("Skin call guard while the entity reads the skin");
        leave.invoke(null);if(!(boolean)inside.invoke(null)) throw new AssertionError("Skin call guard must stay on until the outer read ends");
        leave.invoke(null);leave.invoke(null);if((boolean)inside.invoke(null)) throw new AssertionError("Skin call guard must reopen and never go negative");
        Class<?> screen=Class.forName("dev.vortex.client.CapeScreen",false,target);
        for(int[] dims:new int[][]{{320,240},{430,300},{640,360},{1920,1080}}) {
            Object layout=screen.getMethod("layout",int.class,int.class).invoke(null,dims[0],dims[1]);
            Class<?> t=layout.getClass();
            int x=(int)t.getMethod("x").invoke(layout),y=(int)t.getMethod("y").invoke(layout);
            int w=(int)t.getMethod("width").invoke(layout),h=(int)t.getMethod("height").invoke(layout);
            if(x<0 || y<0 || x+w>dims[0] || y+h>dims[1]) throw new AssertionError("Cape screen clips");
            int pw=(int)t.getMethod("previewWidth").invoke(layout),lx=(int)t.getMethod("listX").invoke(layout);
            int lw=(int)t.getMethod("listWidth").invoke(layout),step=(int)t.getMethod("rowStep").invoke(layout);
            if(pw<80 || lw<140 || lx<x+12+pw+8 || lx+lw>x+w-12 || 44+5*step>h-52 || step-3<18)
                throw new AssertionError("Cape preview/list/actions overlap at GUI scale");
        }
        Class<?> catalog=Class.forName("dev.vortex.client.CapeCatalog",true,target);
        Class<?> pixelType=Class.forName("dev.vortex.client.CapePixels",true,target);
        Method decode=pixelType.getMethod("decode",byte[].class),pixelId=pixelType.getMethod("id",byte[].class);
        byte[] png;
        try(var input=target.getResourceAsStream("assets/vortex_client/textures/capes/vortex.png")) {png=input.readAllBytes();}
        byte[] pixels=(byte[])decode.invoke(null,(Object)png);
        if(pixels.length!=8192) throw new AssertionError("Custom cape pixels are not bounded");
        var expectedImage=javax.imageio.ImageIO.read(new java.io.ByteArrayInputStream(png));
        int color=expectedImage.getRGB(13,2),offset=(2*64+13)*4;
        if((pixels[offset]&255)!=(color>>16&255) || (pixels[offset+1]&255)!=(color>>8&255) || (pixels[offset+2]&255)!=(color&255) || (pixels[offset+3]&255)!=(color>>>24))
            throw new AssertionError("Custom cape RGBA channel order");
        byte[] tooWide=png.clone(); java.nio.ByteBuffer.wrap(tooWide,16,4).putInt(1000000);
        fails(()->decode.invoke(null,(Object)tooWide));
        fails(()->decode.invoke(null,(Object)new byte[65537]));
        fails(()->decode.invoke(null,(Object)new byte[12]));
        Path pixelPath=root.resolve("custom-cape-fixture/cape.rgba");
        pixelType.getMethod("save",Path.class,byte[].class).invoke(null,pixelPath,pixels);
        if(!Arrays.equals(pixels,(byte[])pixelType.getMethod("load",Path.class).invoke(null,pixelPath))) throw new AssertionError("Custom pixels did not persist");
        fails(()->pixelType.getMethod("save",Path.class,byte[].class).invoke(null,pixelPath,new byte[1]));
        if(!Arrays.equals(Files.readAllBytes(pixelPath),pixels)) throw new AssertionError("Bad cape overwrote working preview");
        for(String name:new String[]{"vortex","aurora","ember"}) {
            try(var input=target.getResourceAsStream("assets/vortex_client/textures/capes/"+name+".png")) {
                if(input==null) throw new AssertionError("Cape texture missing");
                var bundledImage=javax.imageio.ImageIO.read(input);
                if(bundledImage.getWidth()!=64 || bundledImage.getHeight()!=32 || bundledImage.getRGB(13,2)>>>24!=255) throw new AssertionError("Cape UV size/alpha");
            }
        }
        Class<?> identifier=Class.forName("net.minecraft.resources.Identifier",true,target);
        Object resourceId=identifier.getMethod("parse",String.class).invoke(null,"minecraft:test-original");
        Class<?> texture=Class.forName("net.minecraft.core.ClientAsset$Texture",true,target);
        Object body=Class.forName("net.minecraft.core.ClientAsset$ResourceTexture",true,target).getConstructor(identifier).newInstance(resourceId);
        Class<?> skinType=Class.forName("net.minecraft.world.entity.player.PlayerSkin",true,target);
        Class<?> modelType=Class.forName("net.minecraft.world.entity.player.PlayerModelType",true,target);
        Object skin=skinType.getConstructor(texture,texture,texture,modelType,boolean.class).newInstance(body,body,body,modelType.getEnumConstants()[0],true);
        Method patch=catalog.getMethod("patch",skinType,String.class);
        Object changed=patch.invoke(null,skin,"vortex");
        for(String component:new String[]{"body","elytra","model","secure"})
            if(!Objects.equals(skinType.getMethod(component).invoke(changed),skinType.getMethod(component).invoke(skin))) throw new AssertionError("Cape replaced "+component);
        if(patch.invoke(null,skin,"none")!=skin || patch.invoke(null,skin,"https://bad.test/cape")!=skin) throw new AssertionError("Vanilla/unknown cape fallback");
        Class<?> capes=Class.forName("dev.vortex.client.Capes",true,target);
        // Reproduce the real Main setting; selection must not depend on AWT windows.
        System.setProperty("java.awt.headless","true");
        Class<?> picker=Class.forName("dev.vortex.client.CapeFilePicker",true,target);
        Method decodeSelection=picker.getMethod("decode",long.class);
        Object failedSelection=decodeSelection.invoke(null,0L);
        Class<?> selectionType=failedSelection.getClass();
        if(!(boolean)selectionType.getMethod("failed").invoke(failedSelection)) throw new AssertionError("SDL null file list did not report error");
        Class<?> memory=Class.forName("org.lwjgl.system.MemoryUtil",true,target);
        var pointers=(java.nio.ByteBuffer)memory.getMethod("memCalloc",int.class).invoke(null,16);
        var filename=(java.nio.ByteBuffer)memory.getMethod("memUTF8",CharSequence.class).invoke(null,root.resolve("плащ test.png").toString());
        Method address=memory.getMethod("memAddress",java.nio.ByteBuffer.class),free=memory.getMethod("memFree",java.nio.Buffer.class);
        try {
            long list=(long)address.invoke(null,pointers);
            Object cancelled=decodeSelection.invoke(null,list);
            if((boolean)selectionType.getMethod("failed").invoke(cancelled) || selectionType.getMethod("path").invoke(cancelled)!=null) throw new AssertionError("Cancel confused with error");
            memory.getMethod("memPutAddress",long.class,long.class).invoke(null,list,(long)address.invoke(null,filename));
            Object selected=decodeSelection.invoke(null,list);
            if(!root.resolve("плащ test.png").equals(selectionType.getMethod("path").invoke(selected))) throw new AssertionError("Unicode SDL selection changed");
            int[] completed={0};
            java.util.function.Consumer<Object> consumer=value->{if(value==selected)completed[0]++;};
            Method begin=picker.getMethod("begin",java.util.function.Consumer.class),complete=picker.getMethod("complete",selectionType);
            if(!(boolean)begin.invoke(null,consumer) || (boolean)begin.invoke(null,consumer)) throw new AssertionError("Duplicate picker was accepted");
            complete.invoke(null,selected); complete.invoke(null,selected);
            if(completed[0]!=1 || !(boolean)begin.invoke(null,consumer)) throw new AssertionError("Picker completion did not release busy state once");
            complete.invoke(null,cancelled);
        } finally {free.invoke(null,pointers); free.invoke(null,filename);}
        String localFailure=(String)capes.getMethod("failure",Throwable.class,boolean.class).invoke(null,new java.io.IOException("private path"),true);
        if(localFailure.contains("connection") || localFailure.contains("private path") || !localFailure.contains("PNG")) throw new AssertionError("Local file error mislabeled as network failure");
        try(var bytes=target.getResourceAsStream("dev/vortex/client/Capes.class")) {
            if(new String(bytes.readAllBytes(),java.nio.charset.StandardCharsets.ISO_8859_1).contains("java/awt/")) throw new AssertionError("AWT file picker returned to headless Minecraft");
        }
        System.out.println("PASS Headless Minecraft: SDL picker Unicode/selection/cancel/error, one pending dialog, single completion and local-file error isolation");
        UUID mine=UUID.fromString("11111111-1111-4111-8111-111111111111"), friend=UUID.fromString("22222222-2222-4222-8222-222222222222");
        Method skinMethod=capes.getMethod("skin",UUID.class,skinType);
        if(skinMethod.invoke(null,mine,skin)!=skin) throw new AssertionError("Disabled capes changed skin");
        Field self=capes.getDeclaredField("self"),visible=capes.getDeclaredField("visible"); self.setAccessible(true); visible.setAccessible(true);
        self.set(null,mine); visible.set(null,Map.of(friend,"aurora"));
        configType.getField("selectedCape").set(config,"vortex"); configType.getField("capesEnabled").setBoolean(config,true);
        Object first=skinMethod.invoke(null,mine,skin);
        if(first==skin || skinMethod.invoke(null,mine,skin)!=first || skinMethod.invoke(null,friend,skin)==skin || skinMethod.invoke(null,UUID.randomUUID(),skin)!=skin)
            throw new AssertionError("Cape UUID isolation/cache/fallback");
        // The cape you chose shows on your own character even with the online cape setting off; nobody else's does.
        configType.getField("capesEnabled").setBoolean(config,false);
        if(skinMethod.invoke(null,mine,skin)==skin || skinMethod.invoke(null,friend,skin)!=skin) throw new AssertionError("Own cape shows locally with capes off; other players' capes need the setting");
        // Animated capes are not cached: the cape texture moves on to the next frame.
        configType.getField("selectedCape").set(config,"nebula");
        Method capeOf=skinType.getMethod("cape");
        Object frameA=capeOf.invoke(skinMethod.invoke(null,mine,skin)); Thread.sleep(300); Object frameB=capeOf.invoke(skinMethod.invoke(null,mine,skin));
        if(frameA==null || Objects.equals(frameA,frameB)) throw new AssertionError("Animated cape stuck on one frame");
        configType.getField("capesEnabled").setBoolean(config,false); configType.getField("selectedCape").set(config,"none"); visible.set(null,Map.of()); self.set(null,null);

        Class<?> keys=Class.forName("dev.vortex.client.CapeKeys",true,target);
        Path keyPath=root.resolve("cape-key-fixture/keys.json");
        Object store=keys.getConstructor(Path.class).newInstance(keyPath);
        Method ensure=keys.getMethod("ensure",UUID.class),get=keys.getMethod("get",UUID.class);
        String owner=(String)ensure.invoke(store,mine);
        if(!owner.matches("[a-f0-9]{64}") || !get.invoke(store,mine).equals(owner) || !ensure.invoke(store,mine).equals(owner) || !get.invoke(store,friend).equals("")) throw new AssertionError("Local cape key persistence");
        var ids=new ArrayList<UUID>(); var futures=new ArrayList<CompletableFuture<?>>();
        for(int i=0;i<6;i++) { UUID id=UUID.randomUUID(); ids.add(id); futures.add(CompletableFuture.runAsync(()->{
            try { ensure.invoke(keys.getConstructor(Path.class).newInstance(keyPath),id); } catch(Exception ex) { throw new RuntimeException(ex); }
        })); }
        CompletableFuture.allOf(futures.toArray(CompletableFuture[]::new)).join();
        for(UUID id:ids) if(!((String)get.invoke(store,id)).matches("[a-f0-9]{64}")) throw new AssertionError("Concurrent key save lost another profile");
        Files.writeString(keyPath,"invalid original");
        fails(()->ensure.invoke(store,friend));
        if(!Files.readString(keyPath).equals("invalid original")) throw new AssertionError("Corrupt private keys overwritten");

        Class<?> transport=Class.forName("dev.vortex.client.CapeConnection",true,target);
        fails(()->transport.getConstructor(String.class).newInstance("https://wrong.test"));
        fails(()->transport.getConstructor(String.class).newInstance("http://example.com"));
        try(var fixture=new Fixture(mine)) {
            Object connection=transport.getConstructor(String.class).newInstance(fixture.origin());
            Method lookup=transport.getMethod("lookup",List.class),claim=transport.getMethod("claim",UUID.class,String.class,String.class),select=transport.getMethod("select",UUID.class,String.class,String.class);
            Object found=lookup.invoke(connection,List.of(mine));
            if(!found.equals(Map.of(mine,"aurora"))) throw new AssertionError("Actual HTTP lookup bytes");
            claim.invoke(connection,mine,"a".repeat(64),owner); select.invoke(connection,mine,"ember",owner);
            if(!fixture.authorization.equals("Bearer "+owner)) throw new AssertionError("Owner-only selection transport");
            byte[] customPixels=new byte[8192]; Arrays.fill(customPixels,(byte)127);
            String customId=(String)pixelId.invoke(null,(Object)customPixels);
            transport.getMethod("upload",UUID.class,byte[].class,String.class).invoke(connection,mine,customPixels,owner);
            if(!fixture.authorization.equals("Bearer "+owner)) throw new AssertionError("Custom upload omitted owner authorization");
            byte[] received=(byte[])transport.getMethod("download",String.class).invoke(connection,customId);
            if(!Arrays.equals(received,customPixels)) throw new AssertionError("Custom pixels changed in transport");
            byte[] hd=PhotoSmoke.hdFixture; String hdId=(String)pixelId.invoke(null,(Object)hd);
            transport.getMethod("upload",UUID.class,byte[].class,String.class).invoke(connection,mine,hd,owner);
            if(!Arrays.equals(hd,(byte[])transport.getMethod("download",String.class).invoke(connection,hdId))) throw new AssertionError("HD HTTP data changed or still has legacy 64KB limit");
            fixture.mode=5; fails(()->transport.getMethod("download",String.class).invoke(connection,customId));
            fixture.mode=6; fails(()->transport.getMethod("download",String.class).invoke(connection,customId));
            fixture.mode=0;
            int requests=fixture.requests;
            fails(()->select.invoke(connection,mine,"https://bad.test/cape",owner));
            if(fixture.requests!=requests) throw new AssertionError("Untrusted texture ID reached network");
            fixture.mode=1; fails(()->lookup.invoke(connection,List.of(mine)));
            fixture.mode=2; fails(()->lookup.invoke(connection,List.of(mine)));
            if(fixture.traps!=0) throw new AssertionError("Cape credentials followed redirect");
            fixture.mode=3; fails(()->lookup.invoke(connection,List.of(mine)));
            fixture.mode=4; fails(()->lookup.invoke(connection,List.of(mine)));
        }
        System.out.println("PASS Capes: real skin hook, opt-in, 64x32 PNG decoding and RGBA order, bounded dimensions, atomic pixels/keys, owner-only custom transport, checksum/size rejection, skin/elytra isolation, cached UUID mapping, UI bounds and no redirects");
    }
    interface Checked { void run() throws Exception; }
    static void fails(Checked action) throws Exception { try { action.run(); } catch(InvocationTargetException expected) { return; } throw new AssertionError("Unsafe cape operation succeeded"); }
    static final class Fixture implements AutoCloseable {
        final HttpServer server; final UUID id; volatile int mode,requests,traps; volatile String authorization="";
        volatile byte[] hdPixels=new byte[0];
        Fixture(UUID id) throws Exception {
            this.id=id; server=HttpServer.create(new InetSocketAddress("127.0.0.1",0),0);
            server.createContext("/",exchange->{
                requests++; byte[] requestBytes=exchange.getRequestBody().readAllBytes();
                String route=exchange.getRequestURI().getPath();
                if(route.equals("/trap")) traps++;
                String text="{\"capes\":[{\"uuid\":\""+id.toString().replace("-","")+"\",\"cape\":\"aurora\"}]}";
                if(route.equals("/v1/claim")) text="{\"claimed\":true}";
                if(route.equals("/v1/cape")) { authorization=exchange.getRequestHeaders().getFirst("Authorization"); text="{\"saved\":true,\"cape\":\"ember\"}"; }
                byte[] customPixels=new byte[8192]; Arrays.fill(customPixels,(byte)127);
                String customHash;
                try {customHash=java.util.HexFormat.of().formatHex(java.security.MessageDigest.getInstance("SHA-256").digest(customPixels));}
                catch(java.security.NoSuchAlgorithmException impossible) {throw new AssertionError(impossible);}
                if(route.equals("/v1/custom")) {authorization=exchange.getRequestHeaders().getFirst("Authorization"); text="{\"saved\":true,\"cape\":\"custom:"+customHash+"\"}";}
                if(route.equals("/v2/custom")) {
                    hdPixels=Base64.getDecoder().decode(com.google.gson.JsonParser.parseString(new String(requestBytes,java.nio.charset.StandardCharsets.UTF_8)).getAsJsonObject().get("pixels").getAsString());
                    String hash;
                    try {hash=java.util.HexFormat.of().formatHex(java.security.MessageDigest.getInstance("SHA-256").digest(hdPixels));} catch(Exception impossible) {throw new AssertionError(impossible);}
                    authorization=exchange.getRequestHeaders().getFirst("Authorization"); text="{\"saved\":true,\"cape\":\"hd:"+hash+"\"}";
                }
                int status=200;
                if(mode==1) text="x".repeat(65537);
                if(mode==2) { status=302; exchange.getResponseHeaders().set("Location",origin()+"/trap"); }
                if(mode==3) text="{\"capes\":[{\"uuid\":\"99999999999949998999999999999999\",\"cape\":\"vortex\"}]}";
                if(mode==4) { status=503; text="<html>untrusted</html>"; }
                byte[] bytes=text.getBytes(java.nio.charset.StandardCharsets.UTF_8);
                if(route.startsWith("/v1/textures/")) {
                    bytes=customPixels;
                    if(mode==5) bytes=new byte[8192];
                    if(mode==6) bytes=new byte[8193];
                }
                if(route.startsWith("/v2/textures/")) bytes=hdPixels;
                exchange.getResponseHeaders().set("Content-Type","application/json"); exchange.sendResponseHeaders(status,bytes.length);
                try(var output=exchange.getResponseBody()) { output.write(bytes); } catch(java.io.IOException disconnected) { }
                exchange.close();
            }); server.start();
        }
        String origin() { return "http://127.0.0.1:"+server.getAddress().getPort(); }
        public void close() { server.stop(0); }
    }
}
