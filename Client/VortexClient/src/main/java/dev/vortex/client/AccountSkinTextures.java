package dev.vortex.client;

import java.util.*;
import net.minecraft.world.entity.player.PlayerSkin;
import net.minecraft.core.ClientAsset;

/** Render-thread-only bounded remote textures. Network and checksums happen off-thread. */
public final class AccountSkinTextures {
    private record Entry(String hash,boolean slim,ClientAsset.Texture texture){}
    private static final Map<UUID,Entry> entries=new java.util.concurrent.ConcurrentHashMap<>();
    private record Patched(PlayerSkin source,Entry entry,PlayerSkin result){}
    private static final Map<UUID,Patched> patched=new HashMap<>();
    private static java.util.Set<UUID> accountProfiles=java.util.Set.of();
    public static void install(Map<UUID,AccountWardrobe.Profile> profiles,Map<String,byte[]> pixels){
        patched.clear();
        accountProfiles=java.util.Set.copyOf(profiles.keySet());
        var iterator=entries.entrySet().iterator();while(iterator.hasNext()){
            var row=iterator.next();var next=profiles.get(row.getKey());if(next==null || !row.getValue().hash().equals(next.skin())){LocalSkins.release(row.getValue().texture());iterator.remove();}
        }
        for(var row:profiles.entrySet()){
            var p=row.getValue();var old=entries.get(row.getKey());
            if(p.skin().isEmpty() || old!=null && old.hash().equals(p.skin()) && old.slim()==p.slim())continue;
            byte[] data=pixels.get(p.skin());if(data==null)continue;
            try{var texture=LocalSkins.texture(data);entries.put(row.getKey(),new Entry(p.skin(),p.slim(),texture));if(old!=null)LocalSkins.release(old.texture());}catch(Exception unavailable){}
        }
    }
    public static boolean has(UUID player,String hash,boolean slim){var entry=entries.get(player);return entry!=null && entry.hash().equals(hash) && entry.slim()==slim;}
    public static PlayerSkin skin(UUID id,PlayerSkin original){
        boolean own=id.equals(LocalSkins.profile(net.minecraft.client.Minecraft.getInstance()));
        boolean preferAccount=own && AccountWardrobe.connected() && !LocalSkins.changedThisSession() && accountProfiles.contains(id);
        if(preferAccount)original=LocalSkins.original(original);
        var entry=entries.get(id);if(entry==null || own && LocalSkins.saved()!=null && !preferAccount)return original;
        var previous=patched.get(id);if(previous!=null && previous.source()==original && previous.entry()==entry)return previous.result();
        var result=LocalSkins.patch(original,entry.texture(),entry.slim());patched.put(id,new Patched(original,entry,result));return result;
    }
}
