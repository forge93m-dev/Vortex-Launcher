package dev.vortex.client;

import java.util.*;
import java.util.concurrent.Executors;
import java.util.function.Consumer;
import net.minecraft.client.Minecraft;

/** Small thumbnails prepared once off-thread. Never decodes files in the draw loop. */
public final class WardrobeGallery {
    public record Item(CosmeticCollection.Entry entry,String label,byte[] thumbnail,boolean favorite) { }
    public record Result(List<Item> skins,List<Item> capes,String notice) { }
    private static final java.util.concurrent.ExecutorService IO=Executors.newSingleThreadExecutor(r->{var t=new Thread(r,"Vortex wardrobe collection");t.setDaemon(true);return t;});
    private WardrobeGallery(){ }
    public static void refresh(Consumer<Result> completion) {
        IO.execute(()->{
            var skins=new ArrayList<Item>();var capes=new ArrayList<Item>();int bad=0;String notice="";
            try {
                var store=CosmeticCollection.local();
                for(var kind:CosmeticCollection.Kind.values())for(var entry:store.entries(kind)) {
                    try {
                        byte[] thumb=new byte[40*64*4];String label;
                        if(kind==CosmeticCollection.Kind.SKIN) {
                            var skin=store.readSkin(entry);label=(skin.slim()?"Slim ":"Skin ")+entry.id().substring(0,4);
                            for(int y=0;y<40;y++)for(int x=0;x<40;x++) {
                                int source=((8+y/5)*64+8+x/5)*4,hat=((8+y/5)*64+40+x/5)*4,dest=((y+12)*40+x)*4;
                                int alpha=skin.rgba()[hat+3]&255;
                                for(int c=0;c<3;c++)thumb[dest+c]=(byte)(((skin.rgba()[hat+c]&255)*alpha+(skin.rgba()[source+c]&255)*(255-alpha))/255);
                                thumb[dest+3]=(byte)255;
                            }
                        } else {
                            var cape=CapePixels.unpack(store.readCape(entry));label="Cape "+entry.id().substring(entry.id().indexOf('_')+1,entry.id().indexOf('_')+5);
                            for(int y=0;y<64;y++)for(int x=0;x<40;x++) {
                                int sx=(int)((12+(x+.5)*10/40)*cape.width()/64),sy=(int)((1+(y+.5)*16/64)*cape.width()/64);
                                System.arraycopy(cape.rgba(),(sy*cape.width()+sx)*4,thumb,(y*40+x)*4,4);
                            }
                        }
                        CosmeticCollection.Details details;
                        try{details=store.details(entry);}catch(Exception invalidLabel){details=new CosmeticCollection.Details("",false);bad++;}
                        if(!details.name().isEmpty())label=details.name();
                        (kind==CosmeticCollection.Kind.SKIN?skins:capes).add(new Item(entry,label,thumb,details.favorite()));
                    } catch(Exception invalid){bad++;}
                }
                if(bad>0)notice=bad+" unreadable collection entries skipped; originals kept.";
            } catch(Exception error){notice="Could not open the collection. Existing files were kept.";}
            var order=Comparator.comparing(Item::favorite).reversed().thenComparing(Item::label,String.CASE_INSENSITIVE_ORDER).thenComparing(i->i.entry().id());
            skins.sort(order);capes.sort(order);
            var result=new Result(List.copyOf(skins),List.copyOf(capes),notice);Minecraft.getInstance().execute(()->completion.accept(result));
        });
    }
    public static void skin(CosmeticCollection.Entry entry,Consumer<SkinPixels.Selection> completion) {
        IO.execute(()->{try {var skin=CosmeticCollection.local().readSkin(entry);Minecraft.getInstance().execute(()->completion.accept(skin));}
            catch(Exception error){Minecraft.getInstance().execute(()->{LocalSkins.message="Could not read this saved skin. Original was kept.";completion.accept(null);});}});
    }
    public static void cape(CosmeticCollection.Entry entry,Consumer<byte[]> completion) {
        IO.execute(()->{try{byte[] pixels=CosmeticCollection.local().readCape(entry);Minecraft.getInstance().execute(()->completion.accept(pixels));}
            catch(Exception error){Minecraft.getInstance().execute(()->{Capes.message="Could not read this cape. Original was kept.";completion.accept(null);});}});
    }
    public static void edit(CosmeticCollection.Entry entry,String name,boolean favorite,boolean delete,Consumer<String> completion) {
        IO.execute(()->{String error="";
            try{var store=CosmeticCollection.local();if(delete)store.remove(entry);else store.describe(entry,name,favorite);}
            catch(Exception failed){error="Could not save changes. Check the collection folder and retry.";}
            String result=error;Minecraft.getInstance().execute(()->{
                if(result.isEmpty())ClientNotifications.success("wardrobe-edit",delete?"Removed from wardrobe":"Wardrobe saved",delete?"A recoverable copy of the design was kept.":"Name and favorite status updated.");
                else ClientNotifications.error("wardrobe-edit-error","Changes not saved","Check access to your wardrobe folder and try again.");
                completion.accept(result);
            });
        });
    }
}
