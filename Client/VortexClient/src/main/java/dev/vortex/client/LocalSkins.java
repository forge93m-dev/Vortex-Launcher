package dev.vortex.client;

import java.nio.file.*;
import java.util.UUID;
import java.util.concurrent.Executors;
import java.util.function.Consumer;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.texture.DynamicTexture;
import com.mojang.blaze3d.platform.NativeImage;
import net.minecraft.core.ClientAsset;
import net.minecraft.resources.Identifier;
import net.minecraft.world.entity.player.PlayerModelType;
import net.minecraft.world.entity.player.PlayerSkin;

/** Local-only skin override. No network, no other players and no vanilla account modifications. */
public final class LocalSkins {
    private static final java.util.concurrent.ExecutorService IO=Executors.newSingleThreadExecutor(r->{var t=new Thread(r,"Vortex local skins");t.setDaemon(true);return t;});
    private static volatile boolean busy;
    private static boolean changedThisSession;
    public static boolean changedThisSession(){return changedThisSession;}
    private static UUID profile;
    private static int generation,textureSerial;
    private static SkinPixels.Selection saved;
    private static ClientAsset.Texture applied,draft;
    private static PlayerSkin vanilla,cached;
    public static String message="Choose a skin. Apply syncs online when signed in to Vortex.";
    private LocalSkins() { }
    public static UUID profile(Minecraft mc) {return mc.player!=null?mc.player.getUUID():mc.getUser().getProfileId();}
    public static boolean busy() {return busy;}
    public static SkinPixels.Selection saved() {return saved;}
    public static void restoreOnline(UUID player,boolean slim,byte[] pixels){
        if(pixels==null || pixels.length!=SkinPixels.SIZE || !java.util.Objects.equals(player,profile) || saved!=null || busy)return;
        int version=generation;var selection=new SkinPixels.Selection(true,slim,pixels.clone());var mc=Minecraft.getInstance();
        IO.execute(()->{try{
            // Never replace an existing local selection or an unreadable saved file.
            if(Files.exists(file(player)))return;
            CosmeticCollection.local().addSkin(selection);SkinPixels.save(file(player),selection);
            mc.execute(()->{if(version!=generation || saved!=null)return;try{applied=texture(selection.rgba());saved=selection;vanilla=cached=null;message="Skin restored from your Vortex account.";}catch(Exception unavailable){message="Online skin saved. Restart Minecraft to display it.";}});
        }catch(Exception unavailable){ /* Remote rendering remains usable without local persistence. */ }});
    }
    private static Path file(UUID id) {return Path.of(System.getProperty("user.home"),".vortex","skins",id.toString()+".skin");}
    public static void tick(Minecraft mc) {
        UUID id=profile(mc);if(java.util.Objects.equals(id,profile))return;
        profile=id;int version=++generation;saved=null;vanilla=cached=null;changedThisSession=false;
        release(applied);if(draft!=applied)release(draft);applied=draft=null;
        if(id==null)return;
        IO.execute(()->{try {var selection=SkinPixels.load(file(id));
            if(selection!=null && selection.enabled())try{CosmeticCollection.local().addSkin(selection);}catch(Exception collectionUnavailable){ /* Existing selection still works. */ }
            mc.execute(()->{
            if(version!=generation)return;
            try {saved=selection;if(selection!=null && selection.enabled())applied=texture(selection.rgba());}
            catch(Exception error){message="Could not display saved skin. Vanilla remains available.";}
        });}catch(Exception error){mc.execute(()->{if(version==generation)message="Saved skin could not be read. Original file was preserved.";});}});
    }
    public static PlayerSkin skin(UUID id,PlayerSkin original) {
        if(original==null || !java.util.Objects.equals(id,profile) || saved==null || !saved.enabled() || applied==null)return original;
        if(original==vanilla && cached!=null)return cached;
        vanilla=original;return cached=patch(original,applied,saved.slim());
    }
    public static PlayerSkin original(PlayerSkin skin) {return skin==cached && vanilla!=null?vanilla:skin;}
    public static PlayerSkin patch(PlayerSkin base,ClientAsset.Texture texture,boolean slim) {
        return texture==null?base:new PlayerSkin(texture,base.cape(),base.elytra(),slim?PlayerModelType.SLIM:PlayerModelType.WIDE,false);
    }
    public static ClientAsset.Texture draft(byte[] pixels) throws Exception {
        if(draft!=applied)release(draft);return draft=texture(pixels);
    }
    public static void discardDraft() {if(draft!=applied)release(draft);draft=null;}
    static ClientAsset.Texture texture(byte[] pixels) throws Exception {
        if(pixels.length!=SkinPixels.SIZE)throw new IllegalArgumentException("Invalid skin pixels.");
        var image=new NativeImage(64,64,false);DynamicTexture texture=null;
        try {
            for(int y=0;y<64;y++)for(int x=0;x<64;x++){int p=(y*64+x)*4;image.setPixel(x,y,((pixels[p+3]&255)<<24)|((pixels[p]&255)<<16)|((pixels[p+1]&255)<<8)|(pixels[p+2]&255));}
            texture=new DynamicTexture(()->"Vortex local skin",image);
            var id=Identifier.fromNamespaceAndPath("vortex_client","local_skin/"+(++textureSerial));
            Minecraft.getInstance().getTextureManager().register(id,texture);return new ClientAsset.ResourceTexture(id,id);
        }catch(Exception error){if(texture!=null)texture.close();else image.close();throw error;}
    }
    static void release(ClientAsset.Texture texture) {if(texture!=null)Minecraft.getInstance().getTextureManager().release(texture.texturePath());}
    public static void publish(){publish(false);}
    static void publishApplied(){publish(true);}
    private static void publish(boolean appliedVanilla){
        if(busy || Capes.busy()){message="Wait for the current image operation.";return;}
        var mc=Minecraft.getInstance();UUID player=profile(mc);var selection=saved;
        if(player==null){message="Join a world first.";return;}
        if(selection==null && !appliedVanilla){message="Choose and Apply a skin first, or wait for your online skin to restore.";return;}
        if(!AccountWardrobe.connected()){message="Sign in to Vortex in the launcher, then launch Minecraft again.";return;}
        busy=true;message="Syncing skin with your Vortex account…";int version=generation;
        IO.execute(()->{try{AccountWardrobe.skin(player,selection);mc.execute(()->{busy=false;if(version==generation){message="Skin saved to your Vortex account. Updated Vortex clients refresh within about a minute.";ClientNotifications.success("skin-online","Skin published","Confirmed by your Vortex account. No activation code required.");}});}
            catch(Exception error){mc.execute(()->{busy=false;if(version==generation){message=Capes.failure(error,false);ClientNotifications.error("skin-online-error","Skin not published",message);}});}});
    }
    public static void choose(Consumer<byte[]> callback) {choose(false,callback);}
    public static void choose(boolean slim,Consumer<byte[]> callback) {
        var mc=Minecraft.getInstance();if(busy || Capes.busy()){message="Wait for the current image operation.";return;}
        busy=true;
        boolean opened=CapeFilePicker.show(mc.getWindow().handle(),result->mc.execute(()->{
            if(result.failed() || result.path()==null){busy=false;message=result.failed()?"Could not open image selection.":"Selection cancelled.";return;}
            IO.execute(()->{try {
                if(Files.size(result.path())>1024*1024)throw new IllegalArgumentException("Choose a 64x64 skin PNG up to 1 MB.");
                byte[] bytes;try(var input=Files.newInputStream(result.path())){bytes=input.readNBytes(1024*1024+1);}
                byte[] pixels=SkinPixels.decode(bytes);
                CosmeticCollection.local().addSkin(new SkinPixels.Selection(true,slim,pixels));
                mc.execute(()->{busy=false;callback.accept(pixels);});
            }catch(Exception error){mc.execute(()->{busy=false;message=error instanceof IllegalArgumentException?error.getMessage():"Could not read this skin. Original image was not changed.";});}});
        }));
        if(!opened){busy=false;message="An image picker is already open.";}
    }
    public static void apply(byte[] pixels,boolean slim,boolean enabled) {
        apply(pixels,slim,enabled,ignored->{});
    }
    public static void apply(byte[] pixels,boolean slim,boolean enabled,Consumer<Boolean> completion) {
        if(busy){completion.accept(false);return;}var mc=Minecraft.getInstance();UUID id=profile(mc);if(id==null){message="Choose a game profile first.";completion.accept(false);return;}
        if(pixels==null){if(saved==null){message=enabled?"Choose a skin image first.":"Vanilla skin is already active.";completion.accept(!enabled);return;}pixels=saved.rgba();}
        var next=new SkinPixels.Selection(enabled,slim,pixels.clone());busy=true;int version=generation;
        IO.execute(()->{try {
            if(enabled)CosmeticCollection.local().addSkin(next);
            SkinPixels.save(file(id),next);mc.execute(()->{
            busy=false;if(version!=generation){completion.accept(false);return;}changedThisSession=true;
            try {var replacement=enabled?texture(next.rgba()):null;var previous=applied;applied=replacement;saved=next;vanilla=cached=null;release(previous);message=enabled?"Skin applied locally. Other players cannot see it yet.":"Vanilla skin restored locally.";
                ClientNotifications.success("skin-applied","Skin saved","Local save complete. Online sync status is shown in the wardrobe.");}
            catch(Exception error){message="Skin saved; restart Minecraft to display it.";ClientNotifications.info("skin-restart","Skin saved","Restart Minecraft to see the changes.");}
            completion.accept(true);
        });}catch(Exception error){mc.execute(()->{busy=false;message="Could not save skin. Previous selection was preserved.";ClientNotifications.error("skin-save-error","Skin not saved","Check disk space and file permissions. Your previous selection was kept.");completion.accept(false);});}});
    }
}
