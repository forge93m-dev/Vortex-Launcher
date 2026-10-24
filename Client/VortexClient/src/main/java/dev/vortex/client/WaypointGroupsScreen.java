package dev.vortex.client;

import java.util.*;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

/** Explicit group selection and visibility, not an opaque cycling button. */
public final class WaypointGroupsScreen extends Screen {
    private final WaypointsScreen parent;private int scroll;private final long opened=System.nanoTime();
    private List<String> groups=List.of();
    public WaypointGroupsScreen(WaypointsScreen parent){super(Minecraft.getInstance(),UiFonts.body(),Component.literal("Waypoint groups"));this.parent=parent;}
    private int pw(){return Math.min(400,width-16);}private int ph(){return Math.min(340,height-16);}private int px(){return (width-pw())/2;}private int py(){return (height-ph())/2;}private int rows(){return Math.max(1,(ph()-108)/32);}
    @Override protected void init(){
        groups=WaypointsFeature.groups();scroll=Math.clamp(scroll,0,Math.max(0,groups.size()-rows()));int x=px()+16,y=py(),w=pw()-32;
        addRenderableWidget(new ClientUi.ActionButton(x+w-52,y+8,52,22,Component.literal("Back"),b->onClose()));
        addRenderableWidget(new ClientUi.ActionButton(x,y+55,w,26,Component.literal("All groups"),b->{parent.group("");onClose();},parent.selectedGroup().isEmpty()));
        for(int i=0;i<rows()&&scroll+i<groups.size();i++){
            String name=groups.get(scroll+i);int yy=y+88+i*32;
            addRenderableWidget(new ClientUi.ActionButton(x,yy,w-58,25,Component.literal(name),b->{parent.group(name);onClose();},name.equals(parent.selectedGroup())));
            addRenderableWidget(new ClientUi.ActionButton(x+w-52,yy,52,25,Component.literal(WaypointsFeature.groupHidden(name)?"Show":"Hide"),b->{WaypointsFeature.toggleGroup(name);rebuildWidgets();}));
        }
    }
    @Override public boolean mouseScrolled(double x,double y,double horizontal,double vertical){if(Double.isFinite(vertical)&&vertical!=0){scroll=(int)Math.clamp(scroll-vertical,0,Math.max(0,groups.size()-rows()));rebuildWidgets();return true;}return super.mouseScrolled(x,y,horizontal,vertical);}
    @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){WaypointUi.begin(g,width,height,px(),py(),pw(),ph(),"Groups","Filter your places or hide an entire group.",opened);if(groups.isEmpty())ClientUi.text(g,"Groups appear when you save a waypoint.",px()+16,py()+100,pw()-32,ClientUi.MUTED);super.extractRenderState(g,mx,my,delta);g.pose().popMatrix();}
    @Override public boolean isPauseScreen(){return false;}
    @Override public boolean isInGameUi(){return minecraft.level!=null;}
    @Override public void extractBackground(GuiGraphicsExtractor g,int mx,int my,float delta){}
    @Override public void onClose(){minecraft.gui.setScreen(parent);}
}
