package dev.vortex.client;

import java.util.*;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

/** Only Save changes a waypoint; legacy block data remains inert. */
public final class WaypointEditScreen extends Screen {
    private final Screen parent;
    private final WaypointsFeature.Scope scope;
    private final WaypointsStore.Point original;
    private final long opened=System.nanoTime();
    private String name="Waypoint",group="General",x="0",y="64",z="0",message="";
    private int color=0xBDA5FF;
    private boolean confirmDelete,wasBusy;
    private final List<Field> fields=new ArrayList<>();
    private record Field(EditBox widget,int x,int y,int width){}
    public record Layout(int x,int y,int width,int height){public boolean compact(){return height<270;}public int actionsY(){return height-36;}}
    public static Layout layout(int width,int height){int w=Math.min(430,width-16),h=Math.min(292,height-16);return new Layout((width-w)/2,(height-h)/2,w,h);}
    public WaypointEditScreen(Screen parent,WaypointsStore.Point original){
        super(Minecraft.getInstance(),UiFonts.body(),Component.literal(original==null?"New waypoint":"Edit waypoint"));this.parent=parent;this.original=original;scope=WaypointsFeature.scope();
        if(original!=null){name=original.name();group=original.group();x=""+original.x();y=""+original.y();z=""+original.z();color=original.color();}
        else if(minecraft.player!=null)currentPosition();
        if(original==null&&parent instanceof WaypointsScreen list&&!list.selectedGroup().isEmpty())group=list.selectedGroup();
    }
    private void currentPosition(){var p=minecraft.player.blockPosition();x=""+p.getX();y=""+p.getY();z=""+p.getZ();}
    public static WaypointEditScreen at(Screen parent,int x,int y,int z){var result=new WaypointEditScreen(parent,null);result.x=""+x;result.y=""+y;result.z=""+z;return result;}
    private void button(int x,int y,int w,int h,String title,Runnable action,boolean primary){var b=new ClientUi.ActionButton(x,y,w,h,Component.literal(title),ignored->action.run(),primary);b.active=!WaypointsFeature.busy();addRenderableWidget(b);}
    private void field(int x,int y,int w,String value,int max,String title,java.util.function.Consumer<String> consumer){
        var input=new WaypointUi.Input(font,x+8,y,w-16,Component.literal(title));input.setMaxLength(max);input.setValue(value);input.setResponder(consumer);input.active=!WaypointsFeature.busy();addRenderableWidget(input);fields.add(new Field(input,x,y,w));
    }
    @Override protected void init(){
        fields.clear();var l=layout(width,height);int xx=l.x()+16,yy=l.y(),w=l.width()-32;wasBusy=WaypointsFeature.busy();
        addRenderableWidget(new ClientUi.ActionButton(xx+w-50,yy+10,50,22,Component.literal("Cancel"),b->onClose()));
        int half=(w-10)/2;
        field(xx,yy+60,half,name,32,"Name",v->name=v);field(xx+half+10,yy+60,w-half-10,group,32,"Group",v->group=v);
        int third=(w-16)/3;
        field(xx,yy+114,third,x,10,"X coordinate",v->x=v);field(xx+third+8,yy+114,third,y,6,"Y coordinate",v->y=v);field(xx+2*(third+8),yy+114,w-2*(third+8),z,10,"Z coordinate",v->z=v);
        button(xx+w-92,yy+92,92,18,"Use my position",()->{if(minecraft.player!=null){currentPosition();rebuildWidgets();}},false);
        int swatch=Math.min(30,(w-7*6)/8);
        for(int i=0;i<WaypointsFeature.COLORS.length;i++){
            final int picked=WaypointsFeature.COLORS[i];
            var b=new ClientUi.ActionButton(xx+i*(swatch+6),yy+158,swatch,20,Component.literal("Color "+(i+1)),ignored->{color=picked;}){
                @Override protected void extractContents(GuiGraphicsExtractor g,int mx,int my,float delta){ClientUi.panel(g,getX(),getY(),getWidth(),getHeight(),5,color==picked?0xFFF1F2F6:0xFF292B32,0xFF191B20);ClientUi.rounded(g,getX()+4,getY()+4,getWidth()-8,getHeight()-8,3,0xFF000000|picked);if(color==picked)ClientUi.rounded(g,getX()+getWidth()/2-2,getY()+8,4,4,2,0xFF111216);}
            };b.active=!wasBusy;addRenderableWidget(b);
        }
        int bottom=yy+l.actionsY();
        if(original!=null)button(xx,bottom,confirmDelete?110:76,22,confirmDelete?"Confirm delete":"Delete",()->{
            if(!sameWorld())return;
            if(!confirmDelete){confirmDelete=true;message="Press Confirm delete to remove this waypoint.";rebuildWidgets();return;}
            WaypointsFeature.save(null,original.id(),ok->{if(ok&&minecraft.gui.screen()==this)onClose();else if(!ok){message=WaypointsFeature.message;rebuildWidgets();}});
        },false);
        button(xx+w-100,bottom,100,22,original==null?"Create waypoint":"Save changes",this::save,true);
    }
    private boolean sameWorld(){if(scope!=null&&scope.equals(WaypointsFeature.scope()))return true;message="World changed. Reopen the waypoint editor.";return false;}
    private void save(){
        if(!sameWorld())return;
        final WaypointsStore.Point point;
        try{point=new WaypointsStore.Point(original==null?UUID.randomUUID():original.id(),scope.world(),scope.dimension(),WaypointsStore.cleanName(name),Integer.parseInt(x.strip()),Integer.parseInt(y.strip()),Integer.parseInt(z.strip()),color,original!=null&&original.hidden(),WaypointsStore.cleanName(group),original==null?List.of():original.blocks());if(!WaypointsStore.valid(point)||point.group().isEmpty())throw new IllegalArgumentException();}
        catch(Exception e){message="Enter a name, group and integer XYZ coordinates.";return;}
        WaypointsFeature.save(point,null,ok->{if(ok&&minecraft.gui.screen()==this)onClose();else{message=WaypointsFeature.message;if(minecraft.gui.screen()==this)rebuildWidgets();}});
    }
    @Override public void tick(){if(wasBusy!=WaypointsFeature.busy())rebuildWidgets();}
    @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){
        var l=layout(width,height);int xx=l.x()+16,yy=l.y(),w=l.width()-32;
        WaypointUi.begin(g,width,height,l.x(),yy,l.width(),l.height(),original==null?"New waypoint":"Edit waypoint","A name, a position, a color.",opened);
        int half=(w-10)/2;ClientUi.text(g,"NAME",xx,yy+48,half,ClientUi.MUTED);ClientUi.text(g,"GROUP",xx+half+10,yy+48,half,ClientUi.MUTED);
        ClientUi.text(g,"POSITION  ·  X / Y / Z",xx,yy+100,w-100,ClientUi.MUTED);
        for(var field:fields)WaypointUi.field(g,field.x(),field.y(),field.width(),26,field.widget().isFocused());
        ClientUi.text(g,"MARKER COLOR",xx,yy+146,w,ClientUi.MUTED);
        if(!l.compact()){
            ClientUi.rounded(g,xx,yy+191,w,42,6,0xFF1B1D23);ClientUi.rounded(g,xx+12,yy+207,8,8,4,0xFF000000|color);
            ClientUi.text(g,name.isBlank()?"Waypoint":name,xx+30,yy+200,w-42,ClientUi.TEXT);
            ClientUi.text(g,x+" / "+y+" / "+z+"  ·  "+group,xx+30,yy+215,w-42,ClientUi.MUTED);
        }
        ClientUi.text(g,WaypointsFeature.busy()?"Saving / loading…":message,xx,yy+l.height()-11,w,ClientUi.MUTED);super.extractRenderState(g,mx,my,delta);g.pose().popMatrix();
    }
    @Override public boolean isPauseScreen(){return false;}
    @Override public boolean isInGameUi(){return minecraft.level!=null;}
    @Override public void extractBackground(GuiGraphicsExtractor g,int mx,int my,float delta){if(minecraft.level!=null)minecraft.gui.hud.extractDeferredSubtitles();else extractPanorama(g,delta);}
    @Override public void onClose(){minecraft.gui.setScreen(parent);}
}
