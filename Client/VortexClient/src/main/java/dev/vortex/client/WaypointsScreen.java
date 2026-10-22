package dev.vortex.client;

import java.util.*;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

public final class WaypointsScreen extends Screen {
    private final long opened = System.nanoTime();
    private final Screen parent;
    private final WaypointsFeature.Scope scope;
    private UUID selected;
    private String name="Waypoint",x="0",y="64",z="0";
    private String group="General",filter="";
    private int color=0xBDA5FF,scroll;
    private boolean confirmDelete,wasBusy;
    private List<WaypointsStore.Point> shown=List.of();
    public record Layout(int x,int y,int width,int height,int sidebar){public int editorX(){return x+sidebar+12;}public int editorWidth(){return width-sidebar-24;}public int rows(){return Math.max(1,(height-128)/32);}}
    public static Layout layout(int width,int height){int w=Math.min(620,width-16),h=Math.min(360,height-16);return new Layout((width-w)/2,(height-h)/2,w,h,Math.min(250,Math.max(125,w*44/100)));}
    public WaypointsScreen(Screen parent,boolean create){super(Minecraft.getInstance(),UiFonts.body(),Component.literal("Waypoints"));this.parent=parent;scope=WaypointsFeature.scope();newHere();}
    public static WaypointsScreen at(Screen parent,int x,int y,int z){var screen=new WaypointsScreen(parent,true);screen.x=""+x;screen.y=""+y;screen.z=""+z;return screen;}
    private void newHere(){selected=null;confirmDelete=false;name="Waypoint";var mc=Minecraft.getInstance();if(mc.player!=null){var p=mc.player.blockPosition();x=""+p.getX();y=""+p.getY();z=""+p.getZ();}}
    private void button(int x,int y,int w,int h,String label,Runnable action,boolean primary){addRenderableWidget(new ClientUi.ActionButton(x,y,w,h,Component.literal(label),b->{if(!WaypointsFeature.busy() || label.equals("Back"))action.run();},primary));}
    private void field(int x,int y,int w,String value,int max,String label,java.util.function.Consumer<String> changed){var input=new EditBox(font,x,y,w,20,Component.literal(label));input.setMaxLength(max);input.setTextShadow(false);input.setValue(value);input.setResponder(changed);addRenderableWidget(input);}
    private void select(WaypointsStore.Point p){selected=p.id();confirmDelete=false;name=p.name();group=p.group();x=""+p.x();y=""+p.y();z=""+p.z();color=p.color();rebuildWidgets();}
    @Override protected void init(){
        var l=layout(width,height);wasBusy=WaypointsFeature.busy();shown=filtered();scroll=Math.clamp(scroll,0,Math.max(0,shown.size()-l.rows()));
        button(l.x()+l.width()-64,l.y()+10,52,22,"Back",this::onClose,false);
        button(l.x()+12,l.y()+34,l.sidebar()-24,22,"+  Current position",()->{newHere();rebuildWidgets();},false);
        button(l.x()+12,l.y()+60,l.sidebar()-64,22,filter.isEmpty()?"All groups":filter,()->{var groups=new ArrayList<String>();groups.add("");groups.addAll(WaypointsFeature.groups());filter=groups.get((Math.max(0,groups.indexOf(filter))+1)%groups.size());scroll=0;rebuildWidgets();},false);
        button(l.x()+l.sidebar()-48,l.y()+60,36,22,filter.isEmpty()?"Group":WaypointsFeature.groupHidden(filter)?"Show":"Hide",()->{if(filter.isEmpty()){WaypointsFeature.message="Choose a group first.";return;}WaypointsFeature.toggleGroup(filter);rebuildWidgets();},false);
        for(int row=0;row<l.rows() && scroll+row<shown.size();row++){
            var p=shown.get(scroll+row);int yy=l.y()+88+row*32;
            button(l.x()+16,yy,l.sidebar()-66,28,p.name(),()->select(p),selected!=null && selected.equals(p.id()));
            button(l.x()+l.sidebar()-46,yy,34,28,p.hidden()?"Show":"Hide",()->WaypointsFeature.save(p.hidden(!p.hidden()),null,ok->{if(minecraft.gui.screen()==this)rebuildWidgets();}),false);
        }
        int xx=l.editorX(),w=l.editorWidth();field(xx,l.y()+49,w,name,32,"Waypoint name",v->name=v);
        int third=(w-8)/3;field(xx,l.y()+92,third,x,10,"X coordinate",v->x=v);field(xx+third+4,l.y()+92,third,y,6,"Y coordinate",v->y=v);field(xx+2*(third+4),l.y()+92,w-2*(third+4),z,10,"Z coordinate",v->z=v);
        field(xx,l.y()+126,w-50,group,32,"Group (General, Bases, Farms…)",v->group=v);
        button(xx+w-46,l.y()+126,46,20,"Color",()->{int found=0;for(int i=0;i<WaypointsFeature.COLORS.length;i++)if(WaypointsFeature.COLORS[i]==color)found=i;color=WaypointsFeature.COLORS[(found+1)%WaypointsFeature.COLORS.length];rebuildWidgets();},false);
        button(xx,l.y()+l.height()-66,w,22,"Save waypoint",this::save,true);
        button(xx,l.y()+l.height()-38,w,22,confirmDelete?"Confirm removal":"Remove waypoint",()->{if(selected==null){WaypointsFeature.message="Select a saved waypoint first.";return;}if(!confirmDelete){confirmDelete=true;rebuildWidgets();return;}WaypointsFeature.save(null,selected,ok->{if(ok)newHere();if(minecraft.gui.screen()==this)rebuildWidgets();});},false);
    }
    private void save(){
        if(scope==null || !scope.equals(WaypointsFeature.scope())){WaypointsFeature.message="World changed. Reopen Waypoints before saving.";return;}
        final WaypointsStore.Point point;
        try{String clean=WaypointsStore.cleanName(name),cleanGroup=WaypointsStore.cleanName(group);if(cleanGroup.isEmpty())throw new IllegalArgumentException();var old=shown.stream().filter(p->p.id().equals(selected)).findFirst().orElse(null);point=new WaypointsStore.Point(selected==null?UUID.randomUUID():selected,scope.world(),scope.dimension(),clean,Integer.parseInt(x.strip()),Integer.parseInt(y.strip()),Integer.parseInt(z.strip()),color,old!=null && old.hidden(),cleanGroup);if(!WaypointsStore.valid(point))throw new IllegalArgumentException();}
        catch(Exception invalid){WaypointsFeature.message="Enter a name, integer XYZ coordinates and valid world bounds.";return;}
        WaypointsFeature.save(point,null,ok->{if(ok)selected=point.id();if(minecraft.gui.screen()==this)rebuildWidgets();});
    }
    @Override public boolean mouseScrolled(double x,double y,double horizontal,double vertical){var l=layout(width,height);if(Double.isFinite(vertical) && x>=l.x() && x<l.x()+l.sidebar() && y>=l.y()+60 && y<l.y()+l.height()-20){scroll=(int)Math.clamp(scroll-vertical,0,Math.max(0,shown.size()-l.rows()));rebuildWidgets();return true;}return super.mouseScrolled(x,y,horizontal,vertical);}
    private List<WaypointsStore.Point> filtered(){return WaypointsFeature.points().stream().filter(p->filter.isEmpty() || filter.equals(p.group())).toList();}
    @Override public void tick(){if(wasBusy!=WaypointsFeature.busy() || !shown.equals(filtered()))rebuildWidgets();}
    @Override public void extractBackground(GuiGraphicsExtractor g,int mx,int my,float delta){if(minecraft.level!=null)minecraft.gui.hud.extractDeferredSubtitles();else extractPanorama(g,delta);}
    @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){
        var l=layout(width,height);ClientUi.beginPage(g,width,height,l.x(),l.y(),l.width(),l.height(),"Waypoints","waypoints",opened);
        g.fill(l.x()+l.sidebar(),l.y()+35,l.x()+l.sidebar()+1,l.y()+l.height()-22,ClientUi.BORDER);
        ClientUi.text(g,selected==null?"New waypoint":"Edit waypoint",l.editorX(),l.y()+38,l.editorWidth(),ClientUi.MUTED);
        ClientUi.text(g,"Group",l.editorX(),l.y()+114,l.editorWidth()-50,ClientUi.MUTED);
        int third=(l.editorWidth()-8)/3;for(int i=0;i<3;i++)ClientUi.text(g,new String[]{"X","Y","Z"}[i],l.editorX()+i*(third+4),l.y()+79,third,ClientUi.MUTED);
        for(int row=0;row<l.rows() && scroll+row<shown.size();row++){var p=shown.get(scroll+row);ClientUi.rounded(g,l.x()+11,l.y()+93+row*32,3,18,1,(p.hidden() || WaypointsFeature.groupHidden(p.group())?0x66000000:0xFF000000)|p.color());}
        ClientUi.text(g,WaypointsFeature.busy()?"Saving / loading…":WaypointsFeature.message,l.x()+12,l.y()+l.height()-14,l.width()-24,ClientUi.MUTED);
        super.extractRenderState(g,mx,my,delta);
        ClientUi.rounded(g,l.editorX()+l.editorWidth()-4,l.y()+131,3,10,1,0xFF000000|color);
        g.pose().popMatrix();
    }
    @Override public boolean isPauseScreen(){return false;}
    @Override public boolean isInGameUi(){return minecraft.level!=null;}
    @Override public void onClose(){minecraft.gui.setScreen(parent);}
}
