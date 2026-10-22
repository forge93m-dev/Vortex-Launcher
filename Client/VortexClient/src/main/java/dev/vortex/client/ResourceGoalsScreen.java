package dev.vortex.client;

import java.util.*;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.chat.Component;

public final class ResourceGoalsScreen extends Screen {
    private final Screen parent;
    private final long opened=System.nanoTime();
    private String item="minecraft:stone",amount="640",message="Counts items in your inventory and offhand, not lifetime pickups.";
    public ResourceGoalsScreen(Screen parent){super(Minecraft.getInstance(),UiFonts.body(),Component.literal("Resource goals"));this.parent=parent;}
    public record Layout(int x,int y,int width,int height) { }
    public static Layout layout(int width,int height){int w=Math.min(500,width-16),h=Math.min(300,height-16);return new Layout((width-w)/2,(height-h)/2,w,h);}
    private void button(int x,int y,int w,String label,Runnable action){addRenderableWidget(new ClientUi.ActionButton(x,y,w,20,Component.literal(label),b->action.run()));}
    private EditBox field(int x,int y,int w,String value,int max,String label,java.util.function.Consumer<String> changed){
        var box=new EditBox(font,x,y,w,20,Component.literal(label));box.setMaxLength(max);box.setValue(value);box.setResponder(changed);box.setTextShadow(false);addRenderableWidget(box);return box;
    }
    @Override protected void init(){
        var l=layout(width,height);int x=l.x()+12,y=l.y(),w=l.width()-24;
        button(l.x()+l.width()-68,y+8,56,"Back",this::onClose);
        field(x,y+46,w-82,item,160,"Item registry ID",v->item=v);
        field(x+w-76,y+46,76,amount,7,"Target quantity",v->amount=v);
        button(x,y+72,(w-6)/2,"Use held item",()->{
            if(minecraft.player==null || minecraft.player.getMainHandItem().isEmpty()){message="Hold the item in your main hand first.";return;}
            item=BuiltInRegistries.ITEM.getKey(minecraft.player.getMainHandItem().getItem()).toString();rebuildWidgets();
        });
        button(x+(w+6)/2,y+72,(w-6)/2,"Save goal",this::add);
        int row=0;for(var goal:VortexClient.config.resourceGoals){
            int index=row++;button(x+w-64,y+100+index*24,64,"Remove",()->{
                var updated=new ArrayList<>(VortexClient.config.resourceGoals);updated.remove(goal);commit(updated);
            });
        }
    }
    private void add(){
        String id=item.strip().toLowerCase(Locale.ROOT);if(!id.contains(":"))id="minecraft:"+id;
        final ResourceCounter.Goal goal;
        try{goal=new ResourceCounter.Goal(id,Integer.parseInt(amount.strip()));if(!ResourceCounter.valid(goal) || ResourceCounter.icon(id).isEmpty())throw new IllegalArgumentException();}
        catch(Exception invalid){message="Enter a known item ID and a target from 1 to 1,000,000.";return;}
        var updated=new ArrayList<>(VortexClient.config.resourceGoals);updated.removeIf(g->g.itemId().equals(goal.itemId()));
        if(updated.size()>=4){message="Up to four goals. Remove one first.";return;}
        updated.add(goal);commit(updated);
    }
    private void commit(List<ResourceCounter.Goal> goals){
        var previous=VortexClient.config.resourceGoals;VortexClient.config.resourceGoals=List.copyOf(goals);
        if(!VortexClient.config.save()){VortexClient.config.resourceGoals=previous;message="Could not save goals. Check the game log.";return;}
        message=VortexClient.config.resourceCounterEnabled?"Goals saved. Position the counter in HUD Editor.":"Goals saved. Enable Resource Counter in Modules to show the HUD.";rebuildWidgets();
    }
    @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){
        var l=layout(width,height);int x=l.x()+12,y=l.y(),w=l.width()-24;
        ClientUi.beginPage(g,width,height,l.x(),y,l.width(),l.height(),"Resource goals","resource_counter",opened);
        ClientUi.text(g,"Item ID",x,y+36,w-82,ClientUi.MUTED);ClientUi.text(g,"Quantity",x+w-76,y+36,76,ClientUi.MUTED);
        int row=0;for(var goal:VortexClient.config.resourceGoals){
            int yy=y+105+row++*24;
            ClientUi.text(g,ResourceCounter.name(goal.itemId())+"  ·  "+ResourceCounter.current(goal.itemId())+" / "+goal.target(),x,yy,w-72,ClientUi.TEXT);
        }
        if(row==0)ClientUi.text(g,"No goals yet. Choose an item above.",x,y+108,w,ClientUi.MUTED);
        ClientUi.text(g,message,x,y+l.height()-17,w,ClientUi.MUTED);
        super.extractRenderState(g,mx,my,delta);g.pose().popMatrix();
    }
    @Override public boolean isPauseScreen(){return false;}
    @Override public boolean isInGameUi(){return minecraft.level!=null;}
    @Override public void extractBackground(GuiGraphicsExtractor g,int mx,int my,float delta){if(isInGameUi())minecraft.gui.hud.extractDeferredSubtitles();else extractPanorama(g,delta);}
    @Override public void onClose(){minecraft.gui.setScreen(parent);}
}
