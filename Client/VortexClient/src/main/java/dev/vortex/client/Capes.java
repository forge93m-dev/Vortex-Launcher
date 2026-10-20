package dev.vortex.client;

import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.ConcurrentHashMap;
import net.minecraft.client.Minecraft;
import net.minecraft.world.entity.player.PlayerSkin;

public final class Capes {
    private static final java.util.concurrent.ExecutorService IO = Executors.newSingleThreadExecutor(r -> {
        var thread = new Thread(r,"Vortex Capes"); thread.setDaemon(true); return thread;
    });
    private static final AtomicBoolean BUSY = new AtomicBoolean();
    private static boolean choosing;
    public static String progressMessage="Connecting...";
    private static final java.util.concurrent.ExecutorService TEXTURE_IO=Executors.newFixedThreadPool(4,r->{var thread=new Thread(r,"Vortex Cape Pixels"); thread.setDaemon(true); return thread;});
    private static volatile Map<UUID,String> visible = Map.of();
    private static final Map<UUID,Patched> patched = new ConcurrentHashMap<>();
    private static volatile UUID self;
    private static Object level;
    private static long nextPoll, lastSuccess;
    private static int generation;
    private static String restoredCustom="";
    private static final java.util.Set<UUID> accountRestored=new java.util.HashSet<>();
    public static final CapePublication PUBLICATION=new CapePublication();
    public static CapePublication.Status status(boolean draft){
        return PUBLICATION.status(LocalSkins.profile(Minecraft.getInstance()),VortexClient.config.selectedCape,VortexClient.config.capesEnabled,draft,System.nanoTime()/1_000_000);
    }
    private static java.nio.file.Path customFile() {
        return net.fabricmc.loader.api.FabricLoader.getInstance().getConfigDir().resolve("vortex-cape.rgba");
    }
    public static String message = "Choose a design in Wardrobe. Apply syncs online when signed in to Vortex.";
    private record Patched(PlayerSkin source, String id, PlayerSkin skin) { }
    private Capes() { }
    public static boolean busy() { return BUSY.get() || choosing; }
    public static String customPreview() {return CapeCatalog.custom(restoredCustom) ? restoredCustom : "";}
    public static UUID profile() { var mc = Minecraft.getInstance(); return mc.player == null ? null : mc.player.getUUID(); }
    public static PlayerSkin skin(UUID player, PlayerSkin original) {
        if (!VortexClient.config.capesEnabled || original == null) return original;
        String cape = player.equals(self) ? VortexClient.config.selectedCape : visible.get(player);
        if (cape == null || "none".equals(cape) || !CapeCatalog.valid(cape)) return original;
        var old = patched.get(player);
        if (old != null && old.source() == original && cape.equals(old.id())) return old.skin();
        var changed = CapeCatalog.patch(original,cape);
        if (patched.size() < 64 || patched.containsKey(player)) patched.put(player,new Patched(original,cape,changed));
        return changed;
    }
    public static PlayerSkin original(UUID player,PlayerSkin skin) {
        var old=patched.get(player);return old!=null && old.skin()==skin?old.source():skin;
    }
    public static boolean enabled(boolean enabled) {
        var config = VortexClient.config; boolean previous = config.capesEnabled;
        config.capesEnabled = enabled;
        if (!config.save()) { config.capesEnabled = previous; message = "Could not save settings.";ClientNotifications.error("cape-settings","Settings not saved","Could not save cape settings. Your previous selection was kept."); return false; }
        generation++; nextPoll = 0; visible = Map.of(); patched.clear();
        PUBLICATION.cancelPending();
        if(!enabled) {CapeImages.retain(java.util.Set.of()); restoredCustom="";}
        message = enabled ? "Enabled. Visible player UUIDs are sent to the Vortex cape service." : "Hidden here. To remove yours online, save Vanilla before disabling.";
        ClientNotifications.info("cape-enabled","Vortex capes",enabled?"Enabled. Selecting a cape does not publish it automatically.":"Hidden here. A previously published cape remains online.");
        return true;
    }
    public static boolean preview(String cape) {
        if (!CapeCatalog.valid(cape)) return false;
        var config = VortexClient.config; String old = config.selectedCape;
        config.selectedCape = cape;
        if (!config.save()) { config.selectedCape = old; message = "Could not save preview.";ClientNotifications.error("cape-save-error","Cape not saved","Could not save your selection. Please try again."); return false; }
        patched.clear(); message = "Local preview: " + cape + ". Save online to share with others.";
        ClientNotifications.success("cape-applied","Cape applied","Local selection saved. Check the wardrobe for online sync status.");return true;
    }
    public static void tick(Minecraft mc) {
        if (level != mc.level) {
            level = mc.level; self = profile(); generation++; visible = Map.of(); patched.clear(); nextPoll = 0;
            PUBLICATION.cancelPending();
            AccountSkinTextures.install(Map.of(),Map.of());
        }
        boolean wardrobePreview=mc.gui.screen() instanceof WardrobeScreen;
        long now = System.nanoTime();
        String chosen=VortexClient.config.selectedCape;
        if(CapeCatalog.custom(chosen) && !chosen.equals(restoredCustom) && !busy()) {
            restoredCustom=chosen;
            submit(()-> {
                byte[] pixels=loadSelection(chosen);
                if(!chosen.equals(CapePixels.id(pixels))) throw new IllegalArgumentException("Choose your custom PNG again.");
                try{CosmeticCollection.local().addCape(pixels);}catch(Exception collectionUnavailable){ /* Preserve the old selection. */ }
                return ()->install(chosen,pixels);
            },true);
            return;
        }
        self = LocalSkins.profile(mc);
        if (self == null || (mc.level == null && !AccountWardrobe.connected())) return;
        if (lastSuccess != 0 && now - lastSuccess > 300_000_000_000L) { visible = Map.of(); patched.clear(); lastSuccess = 0; }
        if (now < nextPoll || busy()) return;
        nextPoll = now + 60_000_000_000L;
        List<UUID> ids = mc.level == null || mc.player == null ? List.of(self) : mc.level.players().stream().sorted(java.util.Comparator.comparingDouble(p -> p.distanceToSqr(mc.player)))
            .limit(32).map(p -> p.getUUID()).distinct().toList();
        String selectionAtRequest=VortexClient.config.selectedCape;
        if (ids.isEmpty()) return;
        submit(() -> {
            var connection=new CapeConnection();
            var found = new java.util.HashMap<UUID,String>();boolean legacyAvailable=true;
            try{found.putAll(connection.lookup(ids));}catch(Exception unavailable){legacyAvailable=false;}
            Map<UUID,AccountWardrobe.Profile> wardrobe;
            try{wardrobe=AccountWardrobe.lookup(ids);}catch(Exception unavailable){if(!legacyAvailable)throw unavailable;wardrobe=Map.of();}
            var accountProfiles=wardrobe;
            accountProfiles.forEach((player,appearance)->found.put(player,appearance.cape()));
            var accountCapes=accountProfiles.values().stream().map(AccountWardrobe.Profile::cape).collect(java.util.stream.Collectors.toSet());
            var images=new ConcurrentHashMap<String,byte[]>();
            var skins=new ConcurrentHashMap<String,byte[]>();
            var pending=new java.util.ArrayList<java.util.concurrent.CompletableFuture<Void>>();
            for(String cape:new java.util.HashSet<>(found.values())) if(CapeCatalog.custom(cape) && CapeImages.asset(cape)==null) {
                pending.add(java.util.concurrent.CompletableFuture.runAsync(()-> {
                    try {images.put(cape,accountCapes.contains(cape)?AccountWardrobe.texture(cape.substring(cape.indexOf(':')+1),CapePixels.MAX_PACKED):connection.download(cape));} catch(Exception unavailable) { /* Other capes stay usable. */ }
                },TEXTURE_IO));
            }
            for(String skin:accountProfiles.entrySet().stream().filter(e->!AccountSkinTextures.has(e.getKey(),e.getValue().skin(),e.getValue().slim())).map(e->e.getValue().skin()).filter(s->!s.isEmpty()).distinct().toList()){
                pending.add(java.util.concurrent.CompletableFuture.runAsync(()->{try{byte[] data=AccountWardrobe.texture(skin,SkinPixels.SIZE);if(data.length==SkinPixels.SIZE)skins.put(skin,data);}catch(Exception unavailable){}},TEXTURE_IO));
            }
            java.util.concurrent.CompletableFuture.allOf(pending.toArray(java.util.concurrent.CompletableFuture[]::new)).join();
            var own=accountProfiles.get(self);
            if(own!=null && CapeCatalog.custom(own.cape()) && images.containsKey(own.cape()))try{CosmeticCollection.local().addCape(images.get(own.cape()));}catch(Exception unavailable){}
            return () -> {
                var keep=new java.util.HashSet<>(found.values()); keep.add(VortexClient.config.selectedCape);
                CapeImages.retain(keep);
                images.forEach(Capes::install);
                AccountSkinTextures.install(accountProfiles,skins);
                if(own!=null && !own.skin().isEmpty())LocalSkins.restoreOnline(self,own.slim(),skins.get(own.skin()));
                if(own!=null && !accountRestored.contains(self) && java.util.Objects.equals(selectionAtRequest,VortexClient.config.selectedCape)){
                    if(!CapeCatalog.custom(own.cape()) || CapeImages.asset(own.cape())!=null){accountRestored.add(self);if(!java.util.Objects.equals(own.cape(),VortexClient.config.selectedCape))preview(own.cape());restoredCustom=CapeCatalog.custom(own.cape())?own.cape():"";}
                }
                visible = found; patched.clear(); lastSuccess = System.nanoTime();
                if(ids.contains(self))PUBLICATION.observe(self,found.get(self),System.nanoTime()/1_000_000);
            };
        });
    }
    private static void install(String id,byte[] pixels) {
        try {CapeImages.install(id,pixels); patched.clear();}
        catch(Exception error) {message="Could not display cape texture. Vanilla remains available.";}
    }
    public static void choosePng() {
        if(busy()) {message="Wait for the current cape request."; return;}
        choosing=true; progressMessage="Choose a PNG or JPEG in the file window...";
        int version=generation;
        var mc=Minecraft.getInstance();
        boolean shown=CapeFilePicker.show(mc.getWindow().handle(),result->mc.execute(()-> {
            choosing=false;
            if(version!=generation) return;
            if(result.failed()) {message="Could not open the file picker. Try windowed mode and choose PNG again."; return;}
            if(result.path()==null) {message="Selection cancelled. Nothing uploaded."; return;}
            importPng(result.path());
        }));
        if(!shown) {choosing=false; message="A file picker is already open.";}
    }
    private static void importPng(java.nio.file.Path path) {
        var mc=Minecraft.getInstance(); var parent=mc.gui.screen();
        submit(()-> {
            var source=CapeArtwork.read(path);
            return ()->{if(mc.gui.screen()==parent) mc.gui.setScreen(new CapeCropScreen(parent,source));};
        },true);
    }
    public static void acceptArtwork(CapeArtwork.Source source,double zoom,double panX,double panY,boolean fit,boolean textureMode) {
        if(busy()) {message="Wait for the current cape request."; return;}
        var wardrobe=Minecraft.getInstance().gui.screen() instanceof WardrobeScreen screen?screen:null;
        submit(()-> {
            byte[] pixels=textureMode?CapeArtwork.texture(source):CapeArtwork.compose(source,zoom,panX,panY,fit); String id=CapePixels.id(pixels);
            CosmeticCollection.local().addCape(pixels);
            if(wardrobe==null)CapePixels.save(customFile(),pixels);
            return ()->{
                if(wardrobe!=null){if(Minecraft.getInstance().gui.screen()==wardrobe)wardrobe.stageCape(pixels);return;}
                var keep=new java.util.HashSet<>(visible.values()); keep.add(id); CapeImages.retain(keep);
                install(id,pixels); restoredCustom=id;
                if(preview(id)) message="Custom cape preview. Click Save online to upload it publicly.";
            };
        },true);
    }
    @FunctionalInterface private interface Work { Runnable run() throws Exception; }
    private static byte[] loadSelection(String id) throws Exception {
        try{return CosmeticCollection.local().readCape(new CosmeticCollection.Entry(CosmeticCollection.Kind.CAPE,id.replace(':','_')));}
        catch(java.nio.file.NoSuchFileException legacy){
            byte[] pixels=CapePixels.load(customFile());
            if(!id.equals(CapePixels.id(pixels)))throw new IllegalArgumentException("Choose your custom PNG again.");
            return pixels;
        }
    }
    /** Install only a preview texture: no config, applied file or online changes. */
    public static boolean prepareDraft(byte[] pixels) {
        try {
            String id=CapePixels.id(pixels);var keep=new java.util.HashSet<>(visible.values());
            keep.add(VortexClient.config.selectedCape);keep.add(id);CapeImages.retain(keep);
            CapeImages.install(id,pixels);return true;
        }catch(Exception error){message="Could not display this cape. Applied cape was not changed.";return false;}
    }
    public static void applyDraft(String cape,byte[] pixels,java.util.function.Consumer<Boolean> completion) {
        if(!CapeCatalog.custom(cape)){completion.accept(preview(cape));return;}
        if(busy()){message="Wait for the current cape operation.";completion.accept(false);return;}
        submit(()->{
            byte[] data=pixels==null?loadSelection(cape):pixels;
            if(!cape.equals(CapePixels.id(data)))throw new IllegalArgumentException("Cape checksum mismatch.");
            CosmeticCollection.local().addCape(data);
            return ()->{boolean saved=preview(cape);if(saved){install(cape,data);restoredCustom=cape;message="Cape applied locally. Online sharing is a separate action.";}completion.accept(saved);};
        },true);
    }
    public static void selectSaved(CosmeticCollection.Entry entry) {
        if(busy()){message="Wait for the current cape operation.";return;}
        submit(()->{
            byte[] pixels=CosmeticCollection.local().readCape(entry);String id=CapePixels.id(pixels);
            CapePixels.save(customFile(),pixels);
            return ()->{var keep=new java.util.HashSet<>(visible.values());keep.add(id);CapeImages.retain(keep);
                install(id,pixels);restoredCustom=id;preview(id);message="Saved cape selected. Save online to share this selection.";};
        },true);
    }
    private static void submit(Work task) {
        submit(task,false);
    }
    private static void submit(Work task,boolean local) {
        submit(task,local,local,()->{});
    }
    private static void submit(Work task,boolean local,boolean notify,Runnable failed) {
        if (!BUSY.compareAndSet(false,true)) return;
        progressMessage=local?"Preparing cape image...":"Connecting...";
        int version = generation;
        IO.execute(() -> {
            try {
                var apply = task.run();
                Minecraft.getInstance().execute(() -> {
                    try {if(version==generation)apply.run();}
                    catch(Exception error){if(version==generation){message=failure(error,local);failed.run();if(notify)notifyFailure(local);}}
                    finally{BUSY.set(false);}
                });
            } catch (Exception error) {
                // Never log request bodies, activation codes, keys or arbitrary server output.
                Minecraft.getInstance().execute(() -> {
                    if (version == generation) {message = failure(error,local);failed.run();if(notify)notifyFailure(local);}
                    BUSY.set(false);
                });
            }
        });
    }
    private static void notifyFailure(boolean local){
        ClientNotifications.error(local?"cape-image-error":"cape-online-error",local?"Could not prepare cape":"No server confirmation",
            local?"Check the image and file permissions. Nothing was published.":"Check your cape status for details and retry options.");
    }
    public static String failure(Throwable error,boolean local) {
        for(int i=0;i<8 && error.getCause()!=null && (error instanceof java.util.concurrent.ExecutionException || error instanceof java.util.concurrent.CompletionException || error instanceof java.lang.reflect.InvocationTargetException);i++) error=error.getCause();
        if(error instanceof IllegalStateException || error instanceof IllegalArgumentException) return error.getMessage();
        if(local) return "Could not read the cape PNG. Check the file and its permissions; nothing uploaded.";
        if(error instanceof javax.net.ssl.SSLException) return "Cape TLS connection failed. Check the system date and Java installation.";
        if(error instanceof java.net.UnknownHostException || error instanceof java.net.ConnectException) return "Cannot reach the cape service. Check your connection.";
        if(error instanceof java.net.http.HttpTimeoutException || error instanceof java.util.concurrent.TimeoutException) return "Cape service timed out. Please retry.";
        return "Cape connection failed. Vanilla capes remain available; retry later.";
    }
    public static void activate(String code) {
        UUID id = profile();
        if (busy()) { message = "Another cape request is in progress. Wait a moment."; return; }
        if (!VortexClient.config.capesEnabled || id == null) { message = "Enable capes and join a world/server first."; return; }
        submit(() -> {
            String owner = CapeKeys.local().ensure(id); // Save before claim; a lost reply is safely retryable.
            new CapeConnection().claim(id,code.trim().toLowerCase(java.util.Locale.ROOT),owner);
            return () -> { message = "Profile activated. Choose a cape and Save online."; nextPoll = 0;ClientNotifications.success("cape-access","Access activated","You can now publish capes using Online."); };
        },false,true,()->{});
    }
    public static void publish() {
        UUID id = LocalSkins.profile(Minecraft.getInstance()); String cape = VortexClient.config.selectedCape;
        if (busy()) { message = "Another cape request is in progress. Wait a moment."; return; }
        if (!VortexClient.config.capesEnabled || id == null) { message = "Enable capes and choose a valid launch profile first.";ClientNotifications.info("cape-publish-needs-profile","Publish cape","Enable capes and choose a valid launch profile first."); return; }
        var request=PUBLICATION.begin(id,cape);
        submit(() -> {
            if(AccountWardrobe.connected()){
                byte[] pixels=CapeCatalog.custom(cape)?loadSelection(cape):null;
                AccountWardrobe.cape(id,cape,pixels);
                return ()->{message="Cape saved to your Vortex account. No activation code required.";nextPoll=0;
                    if(PUBLICATION.complete(request,true,System.nanoTime()/1_000_000))ClientNotifications.success("cape-published","Cape published","Confirmed by your Vortex account. Updated clients refresh within about a minute.");};
            }
            String owner = CapeKeys.local().get(id);
            if (owner.isEmpty()) throw new IllegalStateException("No saved access for this profile. Open Online access for first activation.");
            var connection=new CapeConnection();
            if(CapeCatalog.custom(cape)) {
                byte[] pixels=loadSelection(cape);
                if(!cape.equals(CapePixels.id(pixels))) throw new IllegalArgumentException("Choose your custom PNG again before uploading.");
                connection.upload(id,pixels,owner);
            } else connection.select(id,cape,owner);
            return () -> {message = "Cape saved online. Other updated Vortex clients refresh within about a minute.";nextPoll=0;
                if(PUBLICATION.complete(request,true,System.nanoTime()/1_000_000)){ClientNotifications.QUEUE.dismiss("cape-publish-error");ClientNotifications.success("cape-published","Cape published","Confirmed by the server. Other Vortex clients refresh in about a minute.");}};
        },false,false,()->{if(PUBLICATION.complete(request,false,System.nanoTime()/1_000_000))ClientNotifications.error("cape-publish-error","Publication unconfirmed","Check your access and connection. Click Retry in the wardrobe.");});
    }
}
