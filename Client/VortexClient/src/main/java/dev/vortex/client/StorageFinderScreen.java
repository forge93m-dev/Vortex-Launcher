package dev.vortex.client;

import java.util.*;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

public final class StorageFinderScreen extends Screen {
    private final Screen parent;
    private final long opened=System.nanoTime();
    private String query="",message="Last-seen contents may have changed. History lasts until you leave this world.";
    private List<StorageMemory.Match> results=List.of();
    private boolean confirmClear;
    private int scroll;
    private int ticks;
    public StorageFinderScreen(Screen parent){super(Minecraft.getInstance(),UiFonts.body(),Component.literal("Storage Finder"));this.parent=parent;}
    public record Layout(int x,int y,int width,int height){public int rows(){return Math.max(1,(height-134)/28);}}
    public static Layout layout(int width,int height){int w=Math.min(560,width-16),h=Math.min(340,height-16);return new Layout((width-w)/2,(height-h)/2,w,h);}
    @Override protected void init(){
        var l=layout(width,height);results=StorageMemory.find(query);scroll=Math.clamp(scroll,0,Math.max(0,results.size()-l.rows()));
        addRenderableWidget(new ClientUi.ActionButton(l.x()+l.width()-68,l.y()+8,56,20,Component.literal("Back"),b->onClose()));
        var search=new EditBox(font,l.x()+12,l.y()+44,l.width()-24,20,Component.literal("Item name or ID"));search.setMaxLength(128);search.setTextShadow(false);search.setValue(query);search.setHint(Component.literal("Search previously opened storage..."));
        search.setResponder(value->{query=value;scroll=0;results=StorageMemory.find(query);refreshRows();});addRenderableWidget(search);
        addRenderableWidget(new ClientUi.ActionButton(l.x()+12,l.y()+l.height()-44,106,20,Component.literal(confirmClear?"Confirm clear":"Clear history"),b->{
            if(!confirmClear){confirmClear=true;b.setMessage(Component.literal("Confirm clear"));return;}StorageMemory.clear();confirmClear=false;results=List.of();rebuildWidgets();
        }));
        addRenderableWidget(new ClientUi.ActionButton(l.x()+124,l.y()+l.height()-44,106,20,Component.literal("Clear marker"),b->{StorageMemory.clearMarker();message="Temporary storage marker removed.";}));
        refreshRows();
    }
    private final List<net.minecraft.client.gui.components.Button> rows=new ArrayList<>();
    private void refreshRows(){
        for(var button:rows)removeWidget(button);rows.clear();var l=layout(width,height);
        scroll=Math.clamp(scroll,0,Math.max(0,results.size()-l.rows()));
        for(int i=0;i<l.rows() && scroll+i<results.size();i++){
            var match=results.get(scroll+i);var p=match.storage().pos();
            rows.add(addRenderableWidget(new ClientUi.ActionButton(l.x()+l.width()-136,l.y()+76+i*28,52,22,Component.literal("Mark"),b->{
                message=StorageMemory.mark(match)?"Marker set. Close the menu to follow it; contents may have changed.":"This storage is no longer available in the current world.";
            })));
            var button=new ClientUi.ActionButton(l.x()+l.width()-78,l.y()+76+i*28,66,22,Component.literal("Copy XYZ"),b->{minecraft.keyboardHandler.setClipboard(p.getX()+" "+p.getY()+" "+p.getZ());message="Coordinates copied. Contents are from the last visit.";});
            rows.add(addRenderableWidget(button));
        }
    }
    @Override public void tick(){if(++ticks%5==0){var fresh=StorageMemory.find(query);if(!fresh.equals(results)){results=fresh;refreshRows();}}}
    @Override public boolean mouseScrolled(double mx,double my,double horizontal,double vertical){var l=layout(width,height);if(Double.isFinite(vertical) && mx>=l.x() && mx<l.x()+l.width() && my>=l.y()+72 && my<l.y()+l.height()-48){scroll=(int)Math.clamp(scroll-vertical,0,Math.max(0,results.size()-l.rows()));refreshRows();return true;}return super.mouseScrolled(mx,my,horizontal,vertical);}
    @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){
        var l=layout(width,height);ClientUi.beginPage(g,width,height,l.x(),l.y(),l.width(),l.height(),"Storage Finder","storage_memory",opened);
        String status=!VortexClient.config.storageMemoryEnabled?"Enable Storage Finder in Modules to record opened storage.":minecraft.player==null?"Join a world first.":StorageMemory.size()+" remembered · "+VortexClient.config.storageMemoryRadius+" m radius · "+results.size()+" matches";
        ClientUi.text(g,status,l.x()+12,l.y()+l.height()-58,l.width()-24,ClientUi.MUTED);
        if(results.isEmpty())ClientUi.text(g,query.isBlank()?"Open a chest, barrel or shulker, then search here.":"No remembered matches nearby.",l.x()+12,l.y()+80,l.width()-24,ClientUi.MUTED);
        for(int i=0;i<l.rows() && scroll+i<results.size();i++){
            var match=results.get(scroll+i);var seen=match.storage();var p=seen.pos();int y=l.y()+77+i*28;
            long seconds=Math.max(0,(System.currentTimeMillis()-seen.time())/1000);
            ClientUi.text(g,seen.title()+"  ·  "+match.count()+" items  ·  "+Math.round(match.distance())+" m",l.x()+12,y,l.width()-160,ClientUi.TEXT);
            ClientUi.text(g,p.getX()+", "+p.getY()+", "+p.getZ()+"  ·  seen "+(seconds<60?seconds+"s":seconds/60+"m")+" ago",l.x()+12,y+12,l.width()-160,ClientUi.MUTED);
        }
        ClientUi.text(g,message,l.x()+12,l.y()+l.height()-16,l.width()-24,ClientUi.MUTED);
        super.extractRenderState(g,mx,my,delta);g.pose().popMatrix();
    }
    @Override public boolean isPauseScreen(){return false;}
    @Override public boolean isInGameUi(){return minecraft.level!=null;}
    @Override public void extractBackground(GuiGraphicsExtractor g,int mx,int my,float delta){if(isInGameUi())minecraft.gui.hud.extractDeferredSubtitles();else extractPanorama(g,delta);}
    @Override public void onClose(){minecraft.gui.setScreen(parent);}
}
