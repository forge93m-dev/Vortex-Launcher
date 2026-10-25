package dev.vortex.client;

import java.util.*;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

/** Searchable waypoint list with explicit visibility and a separate draft form. */
public final class WaypointsScreen extends Screen {
    private final long opened=System.nanoTime();
    private final Screen parent;
    private final WaypointsFeature.Scope scope;
    private String search="",group="";
    private boolean nearest=true,wasBusy,restoreSearch,searchDirty;
    private int scroll,searchCursor;
    private double wheel;
    private EditBox searchBox;
    private List<WaypointsStore.Point> shown=List.of();
    public record Layout(int x,int y,int width,int height){public int rows(){return Math.max(1,(height-160)/44);}public int listY(){return 128;}}
    public static Layout layout(int width,int height){int w=Math.min(660,width-16),h=Math.min(420,height-16);return new Layout((width-w)/2,(height-h)/2,w,h);}
    public WaypointsScreen(Screen parent,boolean create){super(Minecraft.getInstance(),UiFonts.body(),Component.literal("Waypoints"));this.parent=parent;scope=WaypointsFeature.scope();}
    public static WaypointEditScreen at(Screen parent,int x,int y,int z){return WaypointEditScreen.at(parent,x,y,z);}
    void group(String value){group=value;scroll=0;}
    String selectedGroup(){return group;}
    private void button(int x,int y,int w,int h,String label,Runnable action,boolean primary,boolean enabled){var b=new ClientUi.ActionButton(x,y,w,h,Component.literal(label),ignored->action.run(),primary);b.active=enabled;addRenderableWidget(b);}
    public static List<WaypointsStore.Point> matching(List<WaypointsStore.Point> points,String query,String group){
        String[] words=query.strip().toLowerCase(Locale.ROOT).split("\\s+");
        return points.stream().filter(p->group.isEmpty()||p.group().equals(group)).filter(p->{String text=(p.name()+" "+p.group()+" "+p.x()+" "+p.y()+" "+p.z()).toLowerCase(Locale.ROOT);for(String word:words)if(!text.contains(word))return false;return true;}).toList();
    }
    private double distance(WaypointsStore.Point p){var player=minecraft.player;if(player==null)return 0;double dx=p.x()-player.getX(),dy=p.y()-player.getY(),dz=p.z()-player.getZ();return Math.sqrt(dx*dx+dy*dy+dz*dz);}
    private List<WaypointsStore.Point> filtered(){return matching(WaypointsFeature.points(),search,group).stream().sorted(nearest?Comparator.comparingDouble(this::distance):Comparator.comparing(WaypointsStore.Point::name,String.CASE_INSENSITIVE_ORDER)).toList();}
    private void edit(WaypointsStore.Point point){if(scope==null||!scope.equals(WaypointsFeature.scope())){WaypointsFeature.message="World changed. Reopen Waypoints.";return;}minecraft.gui.setScreen(new WaypointEditScreen(this,point));}
    @Override protected void setInitialFocus(){if(getFocused()==null)super.setInitialFocus();}
    @Override protected void init(){
        var l=layout(width,height);int x=l.x()+16,y=l.y(),w=l.width()-32;wasBusy=WaypointsFeature.busy();shown=filtered();scroll=Math.clamp(scroll,0,Math.max(0,shown.size()-l.rows()));
        button(x+w-50,y+10,50,22,"Done",this::onClose,false,true);
        searchBox=new WaypointUi.Input(font,x+9,y+55,w-132,Component.literal("Search waypoints"));searchBox.setMaxLength(128);searchBox.setValue(search);searchBox.setResponder(value->{search=value;scroll=0;searchDirty=true;});addRenderableWidget(searchBox);searchDirty=false;
        if(restoreSearch){setFocused(searchBox);searchBox.setCursorPosition(Math.min(searchCursor,search.length()));restoreSearch=false;}
        button(x+w-106,y+55,106,26,"+ New waypoint",()->edit(null),true,!wasBusy);
        // Sort is a two-way segmented control; groups open their own screen.
        addRenderableWidget(new VortexScreen.FilterPill(x,y+88,70,22,Component.literal("Nearest"),()->nearest,b->{if(!nearest){nearest=true;scroll=0;rebuildWidgets();}}));
        addRenderableWidget(new VortexScreen.FilterPill(x+74,y+88,60,22,Component.literal("Name"),()->!nearest,b->{if(nearest){nearest=false;scroll=0;rebuildWidgets();}}));
        button(x+w-120,y+88,120,22,group.isEmpty()?"Groups · All":"Groups · "+group,()->minecraft.gui.setScreen(new WaypointGroupsScreen(this)),false,true);
        for(int i=0;i<l.rows()&&scroll+i<shown.size();i++){
            var point=shown.get(scroll+i);int rowY=y+l.listY()+i*44;
            addRenderableWidget(new Row(x,rowY,w-64,point,()->edit(point)));
            boolean groupHidden=WaypointsFeature.groupHidden(point.group());
            var visibility=new Visibility(x+w-58,rowY+7,52,26,point,groupHidden,()->{if(groupHidden)minecraft.gui.setScreen(new WaypointGroupsScreen(this));else WaypointsFeature.save(point.hidden(!point.hidden()),null,ok->{if(minecraft.gui.screen()==this)rebuildWidgets();});});
            visibility.active=!wasBusy;addRenderableWidget(visibility);
        }
    }
    /** Show/hide as a switch; a hidden group points to the groups screen instead. Narration keeps the words. */
    private static final class Visibility extends ClientUi.ActionButton {
        private final WaypointsStore.Point point;private final boolean groupHidden;private float state;
        Visibility(int x,int y,int w,int h,WaypointsStore.Point point,boolean groupHidden,Runnable action){
            super(x,y,w,h,Component.literal(groupHidden?"Group hidden":point.hidden()?"Show":"Hide"),b->action.run());this.point=point;this.groupHidden=groupHidden;state=point.hidden()||groupHidden?0:1;
            setTooltip(net.minecraft.client.gui.components.Tooltip.create(Component.literal(groupHidden?"This group is hidden. Open groups to show it.":point.hidden()?"Show this waypoint in the world":"Hide this waypoint in the world")));
        }
        @Override protected void extractContents(GuiGraphicsExtractor g,int mx,int my,float delta){
            state=ClientUi.approach(state,point.hidden()||groupHidden?0:1,1/60.0);
            UiMotion.begin(g,getX(),getY(),getWidth(),getHeight(),pressScale(System.nanoTime()),0);
            if(groupHidden)ClientUi.text(g,"Group",getX()+6,getY()+(getHeight()-9)/2,getWidth()-8,isHoveredOrFocused()?ClientUi.TEXT:ClientUi.MUTED);
            else ClientUi.toggle(g,getX()+getWidth()-30,getY()+(getHeight()-14)/2,state);
            g.pose().popMatrix();
        }
    }
    private final class Row extends ClientUi.ActionButton {
        private final WaypointsStore.Point point;
        Row(int x,int y,int w,WaypointsStore.Point point,Runnable action){super(x,y,w,40,Component.literal("Edit "+point.name()+", "+point.group()),b->action.run());this.point=point;}
        @Override protected void extractContents(GuiGraphicsExtractor g,int mx,int my,float delta){
            UiMotion.begin(g,getX(),getY(),getWidth(),getHeight(),pressScale(System.nanoTime()),0);
            boolean hidden=point.hidden()||WaypointsFeature.groupHidden(point.group());int x=getX(),y=getY(),w=getWidth();
            ClientUi.rounded(g,x,y,w,40,12,isHoveredOrFocused()?ClientUi.RAISED:ClientUi.SURFACE);
            ClientUi.rounded(g,x+8,y+14,12,12,6,(hidden?0x55000000:0xFF000000)|point.color());
            int right=w>330?96:0;
            ClientUi.text(g,point.name(),x+28,y+7,w-40-right,hidden?ClientUi.MUTED:ClientUi.TEXT);
            ClientUi.text(g,point.group()+" · "+point.x()+" / "+point.y()+" / "+point.z(),x+28,y+23,w-40-right,ClientUi.MUTED);
            if(right>0){ClientUi.text(g,WaypointProjection.distance(distance(point)),x+w-90,y+7,80,ClientUi.TEXT);ClientUi.text(g,hidden?"Hidden":"Edit",x+w-90,y+23,80,ClientUi.MUTED);}
            g.pose().popMatrix();
        }
    }
    @Override public void tick(){if(searchDirty||wasBusy!=WaypointsFeature.busy()||!shown.equals(filtered())){restoreSearch=searchBox!=null&&searchBox.isFocused();if(restoreSearch)searchCursor=searchBox.getCursorPosition();rebuildWidgets();}}
    @Override public boolean mouseScrolled(double x,double y,double horizontal,double vertical){var l=layout(width,height);if(Double.isFinite(vertical)&&vertical!=0&&x>=l.x()&&x<l.x()+l.width()&&y>=l.y()+l.listY()&&y<l.y()+l.height()-30){wheel+=vertical*2;int steps=(int)wheel;if(steps!=0){wheel-=steps;scroll=Math.clamp(scroll-steps,0,Math.max(0,shown.size()-l.rows()));rebuildWidgets();}return true;}return super.mouseScrolled(x,y,horizontal,vertical);}
    @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){
        var l=layout(width,height);int x=l.x()+16,y=l.y(),w=l.width()-32;
        String world=VortexMenuChrome.worldName(minecraft);
        String subtitle=(world.isBlank()?"This world":world)+" · "+WaypointsFeature.points().size()+(WaypointsFeature.points().size()==1?" marker":" markers");
        WaypointUi.begin(g,width,height,l.x(),y,l.width(),l.height(),"Waypoints",subtitle,opened);
        WaypointUi.field(g,x,y+55,w-114,26,searchBox!=null&&searchBox.isFocused());
        if(search.isEmpty()&&(searchBox==null||!searchBox.isFocused()))ClientUi.text(g,"Search name, group, coordinates…",x+9,y+63,w-132,ClientUi.MUTED);
        ClientUi.text(g,"SAVED PLACES",x+2,y+116,w-64,ClientUi.MUTED);
        if(shown.isEmpty()){
            ClientUi.text(g,WaypointsFeature.busy()?"Loading waypoints…":search.isEmpty()&&group.isEmpty()?"Your next adventure starts here.":"No matching places.",x+12,y+l.listY()+12,w-24,ClientUi.TEXT);
            if(l.height()>270)ClientUi.text(g,"Create a waypoint at your position with + New waypoint.",x+12,y+l.listY()+30,w-24,ClientUi.MUTED);
        }
        if(shown.size()>l.rows()){
            int track=l.rows()*44-4,thumb=Math.max(14,track*l.rows()/shown.size()),sy=y+l.listY()+(track-thumb)*scroll/(shown.size()-l.rows());
            ClientUi.rounded(g,l.x()+l.width()-7,y+l.listY(),3,track,1,ClientUi.TRACK);ClientUi.rounded(g,l.x()+l.width()-7,sy,3,thumb,1,ClientUi.ACCENT);
        }
        ClientUi.text(g,shown.size()+" places · "+(group.isEmpty()?"All groups":group),x,y+l.height()-26,w,ClientUi.MUTED);
        ClientUi.text(g,WaypointsFeature.message,x,y+l.height()-13,w,ClientUi.MUTED);
        super.extractRenderState(g,mx,my,delta);g.pose().popMatrix();
    }
    @Override public void extractBackground(GuiGraphicsExtractor g,int mx,int my,float delta){if(minecraft.level!=null)minecraft.gui.hud.extractDeferredSubtitles();else extractPanorama(g,delta);}
    @Override public boolean isPauseScreen(){return false;}
    @Override public boolean isInGameUi(){return minecraft.level!=null;}
    @Override public void onClose(){minecraft.gui.setScreen(parent);}
}
