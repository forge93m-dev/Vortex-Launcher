package dev.vortex.client;

import java.util.EnumMap;
import java.util.Objects;
import java.util.UUID;
import net.minecraft.client.Minecraft;

/** Coalesce confirmed local applies, never previews. Dispatched once; failures need explicit retry. */
public final class WardrobeAutoSync {
    public enum Kind { SKIN, CAPE }
    private record Pending(UUID profile,Object selection){}
    public static final class Queue {
        private final EnumMap<Kind,Pending> pending=new EnumMap<>(Kind.class);
        public void applied(Kind kind,UUID profile,Object selection){if(profile!=null)pending.put(kind,new Pending(profile,selection));}
        public Kind next(UUID profile,boolean busy,Object skin,Object cape){
            pending.entrySet().removeIf(e->!Objects.equals(e.getValue().profile(),profile) || !Objects.equals(e.getValue().selection(),e.getKey()==Kind.SKIN?skin:cape));
            if(busy || pending.isEmpty())return null;
            Kind kind=pending.keySet().iterator().next();pending.remove(kind);return kind;
        }
    }
    private static final Queue QUEUE=new Queue();
    public static void skinApplied(UUID profile){
        if(!AccountWardrobe.connected()){LocalSkins.message="Applied locally. Sign in to Vortex and relaunch Minecraft for automatic online sync.";return;}
        QUEUE.applied(Kind.SKIN,profile,LocalSkins.saved());LocalSkins.message="Applied. Waiting to sync with your Vortex account…";
    }
    public static void capeApplied(UUID profile,String cape){
        if(!AccountWardrobe.connected()){Capes.message="Applied locally. Sign in to Vortex and relaunch Minecraft for automatic online sync.";return;}
        QUEUE.applied(Kind.CAPE,profile,cape);Capes.message="Applied. Waiting to sync with your Vortex account…";
    }
    public static void tick(Minecraft mc){
        tick(mc,kind->{
            if(kind==Kind.SKIN)LocalSkins.publishApplied();
            if(kind==Kind.CAPE){if(!VortexClient.config.capesEnabled && !Capes.enabled(true))return;Capes.publish();}
        });
    }
    static void tick(Minecraft mc,java.util.function.Consumer<Kind> publish){
        // The authenticated launch profile exists before joining a world.
        var kind=QUEUE.next(LocalSkins.profile(mc),LocalSkins.busy()||Capes.busy(),LocalSkins.saved(),VortexClient.config.selectedCape);
        if(kind!=null)publish.accept(kind);
    }
}
