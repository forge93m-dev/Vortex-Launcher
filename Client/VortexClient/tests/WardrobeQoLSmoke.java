import java.nio.file.*;
import java.util.*;

public final class WardrobeQoLSmoke {
    public static void run(ClassLoader target,Path root) throws Exception {
        Class<?> storeType=Class.forName("dev.vortex.client.CosmeticCollection",true,target);
        Class<?> kind=Class.forName("dev.vortex.client.CosmeticCollection$Kind",true,target);
        Class<?> entryType=Class.forName("dev.vortex.client.CosmeticCollection$Entry",true,target);
        Class<?> detailsType=Class.forName("dev.vortex.client.CosmeticCollection$Details",true,target);
        Path folder=root.resolve("wardrobe-qol");Object store=storeType.getConstructor(Path.class).newInstance(folder);
        byte[] a=new byte[8192],b=new byte[8192];b[4]=26;
        var add=storeType.getMethod("addCape",byte[].class);
        Object first=add.invoke(store,(Object)a),second=add.invoke(store,(Object)b);
        var describe=storeType.getMethod("describe",entryType,String.class,boolean.class);
        var details=storeType.getMethod("details",entryType);
        describe.invoke(store,first,"  Мой кот 🐈  ",true);
        Object restarted=storeType.getConstructor(Path.class).newInstance(folder);
        Object meta=details.invoke(restarted,first);
        if(!"Мой кот 🐈".equals(detailsType.getMethod("name").invoke(meta)) || !(boolean)detailsType.getMethod("favorite").invoke(meta))
            throw new AssertionError("Unicode label/favorite not persistent");
        add.invoke(store,(Object)a);
        if(!(boolean)detailsType.getMethod("favorite").invoke(details.invoke(store,first)))throw new AssertionError("Repeated import resets favorites");
        String clean=(String)storeType.getMethod("cleanName",String.class).invoke(null,"A\nB\u0000C§bad"+"z".repeat(100));
        if(clean.codePointCount(0,clean.length())>40 || clean.contains("\n") || clean.contains("§") || clean.contains("\u0000"))throw new AssertionError("Unsafe/unbounded display name");
        Path removed=(Path)storeType.getMethod("remove",entryType).invoke(store,first);
        if(!Arrays.equals(a,(byte[])Class.forName("dev.vortex.client.CapePixels",true,target).getMethod("load",Path.class).invoke(null,removed)))
            throw new AssertionError("Removal not recoverable byte-for-byte");
        if(!Files.exists(removed.resolveSibling(entryType.getMethod("id").invoke(first)+".meta")))throw new AssertionError("Deletion loses label/favorite recovery");
        if(((List<?>)storeType.getMethod("entries",kind).invoke(store,kind.getEnumConstants()[1])).size()!=1 ||
            !Arrays.equals(b,(byte[])storeType.getMethod("readCape",entryType).invoke(store,second)))throw new AssertionError("Removal changes another design");
        Object bad=entryType.getConstructor(kind,String.class).newInstance(kind.getEnumConstants()[1],"../../foreign");
        reject(()->describe.invoke(store,bad,"Oops",false));reject(()->storeType.getMethod("remove",entryType).invoke(store,bad));
        String id=(String)entryType.getMethod("id").invoke(second);Path metadata=folder.resolve("capes/"+id+".meta");
        Files.write(metadata,new byte[600]);reject(()->details.invoke(store,second));
        if(Files.size(metadata)!=600 || !Arrays.equals(b,(byte[])storeType.getMethod("readCape",entryType).invoke(store,second)))throw new AssertionError("Bad metadata damages image");
        verifyEditor(target);
        verifyPreview(target);
        verifyBuiltinCapesDuringNetworkWork(target);
        verifyTileMouseButtons(target);
        verifyAutoSyncQueue(target);
        verifyMainMenuSync(target);
        System.out.println("PASS Wardrobe labels/favorites persist, dedup preserves metadata, recoverable confirmed-removal storage, bounded names/path safety; HUD sibling guides, exact undo/anchoring, bounded history and Ctrl+Z routing");
    }
    private static void verifyMainMenuSync(ClassLoader target) throws Exception {
        Class<?> mcType=Class.forName("net.minecraft.client.Minecraft",false,target),userType=Class.forName("net.minecraft.client.User",true,target);
        // Unconstructed fixture only: no game window, sound engine, worlds or real credentials.
        var unsafeField=sun.misc.Unsafe.class.getDeclaredField("theUnsafe");unsafeField.setAccessible(true);
        var unsafe=(sun.misc.Unsafe)unsafeField.get(null);Object mc=unsafe.allocateInstance(mcType);
        UUID profile=UUID.randomUUID();Object user=userType.getConstructor(String.class,UUID.class,String.class,Optional.class,Optional.class).newInstance("MenuFixture",profile,"fixture",Optional.empty(),Optional.empty());
        var userField=mcType.getDeclaredField("user");userField.setAccessible(true);userField.set(mc,user);
        if(mcType.getField("player").get(mc)!=null || mcType.getField("level").get(mc)!=null)throw new AssertionError("Fixture must remain in the main menu");
        Class<?> sync=Class.forName("dev.vortex.client.WardrobeAutoSync",true,target),kind=Class.forName("dev.vortex.client.WardrobeAutoSync$Kind",true,target);
        var queueField=sync.getDeclaredField("QUEUE");queueField.setAccessible(true);Object queue=queueField.get(null);
        var applied=queue.getClass().getMethod("applied",kind,UUID.class,Object.class);
        Class<?> local=Class.forName("dev.vortex.client.LocalSkins",true,target),app=Class.forName("dev.vortex.client.VortexClient",true,target);
        Object config=app.getField("config").get(null);String cape=(String)config.getClass().getField("selectedCape").get(config);
        applied.invoke(queue,kind.getEnumConstants()[0],profile,local.getMethod("saved").invoke(null));
        applied.invoke(queue,kind.getEnumConstants()[1],profile,cape);
        var tick=sync.getDeclaredMethod("tick",mcType,java.util.function.Consumer.class);tick.setAccessible(true);
        var sent=new ArrayList<Object>();java.util.function.Consumer<Object> sink=sent::add;
        tick.invoke(null,mc,sink);tick.invoke(null,mc,sink);tick.invoke(null,mc,sink);
        if(sent.size()!=2 || sent.get(0)!=kind.getEnumConstants()[0] || sent.get(1)!=kind.getEnumConstants()[1])throw new AssertionError("Main-menu ticks must dispatch skin and cape exactly once without a player/world");
        System.out.println("PASS Actual auto-sync tick dispatches main-menu skin and cape using launch UUID, with no player/world, network or window");
    }
    private static void verifyAutoSyncQueue(ClassLoader target) throws Exception {
        Class<?> queueType=Class.forName("dev.vortex.client.WardrobeAutoSync$Queue",true,target),kind=Class.forName("dev.vortex.client.WardrobeAutoSync$Kind",true,target);
        Object queue=queueType.getConstructor().newInstance(),skin=kind.getEnumConstants()[0],cape=kind.getEnumConstants()[1];
        UUID profile=UUID.randomUUID(),other=UUID.randomUUID();
        var applied=queueType.getMethod("applied",kind,UUID.class,Object.class);
        var next=queueType.getMethod("next",UUID.class,boolean.class,Object.class,Object.class);
        if(next.invoke(queue,profile,false,"skinA","capeA")!=null)throw new AssertionError("Preview/opening wardrobe must never create publication work");
        applied.invoke(queue,cape,profile,"capeA");applied.invoke(queue,cape,profile,"capeB");
        if(next.invoke(queue,profile,true,"skinA","capeB")!=null)throw new AssertionError("Upload must wait for local image/network work to finish");
        if(next.invoke(queue,profile,false,"skinA","capeB")!=cape || next.invoke(queue,profile,false,"skinA","capeB")!=null)throw new AssertionError("Latest applied cape must dispatch exactly once, without retry loops");
        applied.invoke(queue,skin,profile,"skinA");applied.invoke(queue,cape,profile,"capeA");
        if(next.invoke(queue,profile,false,"skinA","capeA")!=skin || next.invoke(queue,profile,false,"skinA","capeA")!=cape)throw new AssertionError("Skin and cape applies must both sync independently");
        applied.invoke(queue,skin,profile,"oldSkin");if(next.invoke(queue,profile,false,"newSkin","capeA")!=null)throw new AssertionError("A changed local selection must cancel stale queued upload");
        applied.invoke(queue,cape,profile,"capeA");if(next.invoke(queue,other,false,"skinA","capeA")!=null || next.invoke(queue,profile,false,"skinA","capeA")!=null)throw new AssertionError("Profile changes must drop queued uploads permanently");
        applied.invoke(queue,skin,profile,null);if(next.invoke(queue,profile,false,null,"none")!=skin)throw new AssertionError("Explicit Vanilla Apply must sync skin removal too");
        System.out.println("PASS Auto-sync queues only confirmed applies, coalesces rapid changes, survives busy/closed UI, syncs both cosmetics once, drops stale/profile-changed work and supports Vanilla removal");
    }
    private static void verifyTileMouseButtons(ClassLoader target) throws Exception {
        Class<?> tileType=Class.forName("dev.vortex.client.WardrobeTile",true,target);
        Class<?> motionType=Class.forName("dev.vortex.client.UiMotion$Feedback",true,target);
        Class<?> identifier=Class.forName("net.minecraft.resources.Identifier",true,target);
        Class<?> info=Class.forName("net.minecraft.client.input.MouseButtonInfo",true,target);
        Class<?> event=Class.forName("net.minecraft.client.input.MouseButtonEvent",true,target);
        Class<?> inputs=Class.forName("com.mojang.blaze3d.platform.InputConstants",true,target);
        int left=inputs.getField("MOUSE_BUTTON_LEFT").getInt(null),right=inputs.getField("MOUSE_BUTTON_RIGHT").getInt(null);
        var motionConstructor=motionType.getDeclaredConstructor();motionConstructor.setAccessible(true);
        var constructor=tileType.getDeclaredConstructor(int.class,int.class,int.class,int.class,String.class,identifier,String.class,java.util.function.BooleanSupplier.class,Runnable.class,motionType,long.class);constructor.setAccessible(true);
        int[] actions={0,0};
        Object tile=constructor.newInstance(10,20,100,0,"Saved design",null,null,(java.util.function.BooleanSupplier)()->false,(Runnable)()->actions[0]++,motionConstructor.newInstance(),System.nanoTime());
        var clipTop=tileType.getDeclaredField("clipTop");clipTop.setAccessible(true);clipTop.setInt(tile,25);
        var clipBottom=tileType.getDeclaredField("clipBottom");clipBottom.setAccessible(true);clipBottom.setInt(tile,60);
        var valid=tileType.getDeclaredMethod("isValidClickButton",info);valid.setAccessible(true);
        var click=tileType.getMethod("onClick",event,boolean.class);click.setAccessible(true);
        var over=tileType.getMethod("isMouseOver",double.class,double.class);over.setAccessible(true);
        Object leftInfo=info.getConstructor(int.class,int.class).newInstance(left,0),rightInfo=info.getConstructor(int.class,int.class).newInstance(right,0);
        if(!(boolean)valid.invoke(tile,leftInfo) || (boolean)valid.invoke(tile,rightInfo))throw new AssertionError("Built-in tile must accept actual Minecraft left button and reject right button");
        if(!(boolean)over.invoke(tile,40d,40d) || (boolean)over.invoke(tile,40d,22d) || (boolean)over.invoke(tile,40d,61d))throw new AssertionError("Clipped tiles intercept header/footer");
        var eventConstructor=event.getConstructor(double.class,double.class,info);
        click.invoke(tile,eventConstructor.newInstance(40d,40d,leftInfo),false);
        if(actions[0]!=1)throw new AssertionError("Built-in cape cannot be selected by actual left click");
        var manage=tileType.getDeclaredField("manage");manage.setAccessible(true);manage.set(tile,(Runnable)()->actions[1]++);
        if(!(boolean)valid.invoke(tile,leftInfo) || !(boolean)valid.invoke(tile,rightInfo))throw new AssertionError("Saved tile mouse buttons not accepted");
        click.invoke(tile,eventConstructor.newInstance(40d,40d,leftInfo),false);
        if(actions[0]!=2 || actions[1]!=0)throw new AssertionError("Saved skin/cape left click opens management instead of selecting");
        click.invoke(tile,eventConstructor.newInstance(40d,40d,rightInfo),false);
        click.invoke(tile,eventConstructor.newInstance(105d,28d,leftInfo),false);
        if(actions[0]!=2 || actions[1]!=2)throw new AssertionError("Right click / ellipsis must manage without applying a design");
        System.out.println("PASS Real Minecraft mouse-button events select built-in/saved wardrobe tiles; right click and ellipsis only manage; clipped regions reject hits");
    }
    private static void verifyBuiltinCapesDuringNetworkWork(ClassLoader target) throws Exception {
        Class<?> app=Class.forName("dev.vortex.client.VortexClient",true,target),configType=Class.forName("dev.vortex.client.ClientConfig",true,target);
        var configField=app.getField("config");Object previous=configField.get(null),config=configType.getConstructor().newInstance();configField.set(null,config);
        Class<?> capes=Class.forName("dev.vortex.client.Capes",true,target);var field=capes.getDeclaredField("BUSY");field.setAccessible(true);
        var busy=(java.util.concurrent.atomic.AtomicBoolean)field.get(null);boolean wasBusy=busy.getAndSet(true);
        try {
            Class<?> screen=Class.forName("dev.vortex.client.WardrobeScreen",true,target);
            var constructor=screen.getDeclaredConstructor(Class.forName("net.minecraft.client.Minecraft",false,target),Class.forName("net.minecraft.client.gui.Font",false,target),Class.forName("net.minecraft.client.gui.screens.Screen",false,target));constructor.setAccessible(true);
            Object wardrobe=constructor.newInstance(null,null,null);var tab=screen.getDeclaredField("skinTab");tab.setAccessible(true);tab.setBoolean(wardrobe,false);
            Object layout=screen.getMethod("layout",int.class,int.class).invoke(null,640,360);
            var build=screen.getDeclaredMethod("buildTiles",layout.getClass());build.setAccessible(true);build.invoke(wardrobe,layout);
            var tilesField=screen.getDeclaredField("tiles");tilesField.setAccessible(true);var tiles=(List<?>)tilesField.get(wardrobe);
            String[] ids=(String[])Class.forName("dev.vortex.client.CapeCatalog",true,target).getField("IDS").get(null);
            var pending=screen.getDeclaredMethod("pendingCape");pending.setAccessible(true);
            Class<?> input=Class.forName("net.minecraft.client.input.InputWithModifiers",true,target),key=Class.forName("net.minecraft.client.input.KeyEvent",true,target);
            Object enter=key.getConstructor(int.class,int.class,int.class).newInstance(257,0,0);
            for(int i=0;i<ids.length;i++){
                configType.getField("selectedCape").set(config,"none");
                var press=tiles.get(i).getClass().getMethod("onPress",input);press.setAccessible(true);press.invoke(tiles.get(i),enter);
                if(!ids[i].equals(pending.invoke(wardrobe)) || !"none".equals(configType.getField("selectedCape").get(config)))throw new AssertionError("Built-in cape tile blocked by network request or applies without confirmation");
                boolean[] saved={false};capes.getMethod("applyDraft",String.class,byte[].class,java.util.function.Consumer.class)
                    .invoke(null,ids[i],null,(java.util.function.Consumer<Boolean>)(success->saved[0]=success));
                if(!saved[0] || !ids[i].equals(configType.getField("selectedCape").get(config)) || !busy.get() || configType.getField("capesEnabled").getBoolean(config))
                    throw new AssertionError("Built-in local Apply waits for/cancels network work or changes online opt-in");
            }
            System.out.println("PASS Actual Vanilla/Vortex/Aurora/Ember tile selection and explicit local Apply work during cape-network BUSY, without changing opt-in or cancelling the request");
        }finally{busy.set(wasBusy);configField.set(null,previous);}
    }
    private static void verifyPreview(ClassLoader target) throws Exception {
        Class<?> app=Class.forName("dev.vortex.client.VortexClient",true,target),configType=Class.forName("dev.vortex.client.ClientConfig",true,target);
        var configField=app.getField("config");Object previous=configField.get(null),config=configType.getConstructor().newInstance();configField.set(null,config);
        try{
            configType.getField("selectedCape").set(config,"vortex");configType.getField("capesEnabled").setBoolean(config,false);
            Class<?> screen=Class.forName("dev.vortex.client.WardrobeScreen",true,target);
            var constructor=screen.getDeclaredConstructor(Class.forName("net.minecraft.client.Minecraft",false,target),Class.forName("net.minecraft.client.gui.Font",false,target),Class.forName("net.minecraft.client.gui.screens.Screen",false,target));constructor.setAccessible(true);
            Object wardrobe=constructor.newInstance(null,null,null);
            var choose=screen.getDeclaredMethod("previewBuiltinCape",String.class);choose.setAccessible(true);
            var pending=screen.getDeclaredMethod("pendingCape");pending.setAccessible(true);
            choose.invoke(wardrobe,"ember");
            if(!"ember".equals(pending.invoke(wardrobe)) || !"vortex".equals(configType.getField("selectedCape").get(config)) || configType.getField("capesEnabled").getBoolean(config))
                throw new AssertionError("Try-on must not apply cape or enable online lookup");
            var cancel=screen.getDeclaredMethod("cancelCape");cancel.setAccessible(true);cancel.invoke(wardrobe);
            if(!"vortex".equals(pending.invoke(wardrobe)))throw new AssertionError("Cancel cape does not restore applied appearance");
            Class<?> local=Class.forName("dev.vortex.client.LocalSkins",true,target);Object saved=local.getMethod("saved").invoke(null);
            var vanilla=screen.getDeclaredMethod("previewVanillaSkin");vanilla.setAccessible(true);vanilla.invoke(wardrobe);
            if(local.getMethod("saved").invoke(null)!=saved)throw new AssertionError("Vanilla try-on applied skin immediately");
            var cancelSkin=screen.getDeclaredMethod("cancelSkin");cancelSkin.setAccessible(true);cancelSkin.invoke(wardrobe);
            if(local.getMethod("saved").invoke(null)!=saved)throw new AssertionError("Cancel skin changes saved selection");
            boolean[] applied={false};
            Class.forName("dev.vortex.client.Capes",true,target).getMethod("applyDraft",String.class,byte[].class,java.util.function.Consumer.class)
                .invoke(null,"ember",null,(java.util.function.Consumer<Boolean>)(success->applied[0]=success));
            if(!applied[0] || !"ember".equals(configType.getField("selectedCape").get(config)) || configType.getField("capesEnabled").getBoolean(config))
                throw new AssertionError("Explicit local apply must save cape without enabling online services");
            System.out.println("PASS Actual wardrobe try-on/cancel leaves saved skin/cape and online opt-in untouched; explicit Apply commits local cape only");
        }finally{configField.set(null,previous);}
    }
    private static void verifyEditor(ClassLoader target) throws Exception {
        Class<?> snap=Class.forName("dev.vortex.client.HudSnap",true,target);var axis=snap.getMethod("axis",int.class,int.class,int.class,int[].class);
        checkAxis(axis.invoke(null,3,40,320,new int[0]),0,0);
        checkAxis(axis.invoke(null,278,40,320,new int[0]),280,320);
        checkAxis(axis.invoke(null,139,40,320,new int[0]),140,160);
        checkAxis(axis.invoke(null,98,40,320,new int[]{100}),100,100);
        checkAxis(axis.invoke(null,61,40,320,new int[]{100}),60,100);
        checkAxis(axis.invoke(null,113,40,320,new int[]{100}),113,-1);
        checkAxis(axis.invoke(null,30,400,320,new int[]{100}),0,-1);
        Class<?> app=Class.forName("dev.vortex.client.VortexClient",true,target),configType=Class.forName("dev.vortex.client.ClientConfig",true,target);
        var configField=app.getField("config");Object previous=configField.get(null),config=configType.getConstructor().newInstance();configField.set(null,config);
        try {
            Class<?> historyType=Class.forName("dev.vortex.client.HudLayoutHistory",true,target);Object history=historyType.getConstructor().newInstance();
            var begin=historyType.getMethod("begin");var commit=historyType.getMethod("commit");var undo=historyType.getMethod("undo");var count=historyType.getMethod("size");
            begin.invoke(history);commit.invoke(history);if((int)count.invoke(history)!=0)throw new AssertionError("Click without drag creates undo step");
            begin.invoke(history);configType.getField("armorX").setInt(config,120);configType.getField("armorY").setInt(config,64);
            configType.getField("armorScale").setFloat(config,1.5f);commit.invoke(history);
            configType.getField("selectedCape").set(config,"ember");
            if(!(boolean)undo.invoke(history) || configType.getField("armorX").getInt(config)!=-1 || configType.getField("armorY").getInt(config)!=-1 ||
                configType.getField("armorScale").getFloat(config)!=0 || !"ember".equals(configType.getField("selectedCape").get(config)))throw new AssertionError("Undo loses raw anchoring/inheritance or overwrites unrelated config");
            for(int i=0;i<40;i++){begin.invoke(history);configType.getField("fpsX").setInt(config,200+i);commit.invoke(history);}
            if((int)count.invoke(history)!=32)throw new AssertionError("Undo history unbounded");
            for(int i=0;i<32;i++)if(!(boolean)undo.invoke(history))throw new AssertionError("History steps missing");
            if((boolean)undo.invoke(history))throw new AssertionError("Undo underflow");
            Class<?> editorType=Class.forName("dev.vortex.client.HudEditorScreen",true,target);
            Class<?> screen=Class.forName("net.minecraft.client.gui.screens.Screen",true,target);
            var constructor=editorType.getDeclaredConstructor(Class.forName("net.minecraft.client.Minecraft",false,target),Class.forName("net.minecraft.client.gui.Font",false,target),screen);constructor.setAccessible(true);
            Object editor=constructor.newInstance(null,null,null);screen.getField("width").setInt(editor,320);screen.getField("height").setInt(editor,240);
            var selected=editorType.getDeclaredField("selected");selected.setAccessible(true);Class<?> element=Class.forName("dev.vortex.client.HudModules$Element",true,target);
            selected.set(editor,element.getEnumConstants()[0]);configType.getField("fpsX").setInt(config,110);
            Class<?> key=Class.forName("net.minecraft.client.input.KeyEvent",true,target),input=Class.forName("com.mojang.blaze3d.platform.InputConstants",true,target);
            var press=editorType.getMethod("keyPressed",key);
            press.invoke(editor,key.getConstructor(int.class,int.class,int.class).newInstance(input.getField("KEY_RIGHT").getInt(null),0,0));
            if(configType.getField("fpsX").getInt(config)!=111)throw new AssertionError("Real editor nudge failed");
            press.invoke(editor,key.getConstructor(int.class,int.class,int.class).newInstance(input.getField("KEY_Z").getInt(null),0,input.getField("MOD_CONTROL").getInt(null)));
            if(configType.getField("fpsX").getInt(config)!=110)throw new AssertionError("Real editor Ctrl+Z did not restore nudge");
        } finally {configField.set(null,previous);}
    }
    private static void checkAxis(Object axis,int position,int guide) throws Exception {
        if((int)axis.getClass().getMethod("position").invoke(axis)!=position || (int)axis.getClass().getMethod("guide").invoke(axis)!=guide)throw new AssertionError("Incorrect nearest alignment / guide");
    }
    private interface Check{void run() throws Exception;}
    private static void reject(Check check) throws Exception {try{check.run();}catch(java.lang.reflect.InvocationTargetException expected){return;}throw new AssertionError("Unsafe metadata operation accepted");}
}
