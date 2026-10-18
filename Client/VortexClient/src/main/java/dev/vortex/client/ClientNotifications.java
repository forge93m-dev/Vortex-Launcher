package dev.vortex.client;

import java.util.*;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;

/** Original compact bottom-right cards, above vanilla HUD. Never handles input or blurs the world. */
public final class ClientNotifications {
    public static final NotificationQueue QUEUE=new NotificationQueue();
    private static final Map<Long,Float> positions=new HashMap<>();
    private static UUID profile;
    private static long frame=System.nanoTime();
    private ClientNotifications(){ }
    public static void success(String key,String title,String body){post(key,title,body,NotificationQueue.Kind.SUCCESS);}
    public static void error(String key,String title,String body){post(key,title,body,NotificationQueue.Kind.ERROR);}
    public static void info(String key,String title,String body){post(key,title,body,NotificationQueue.Kind.INFO);}
    private static void post(String key,String title,String body,NotificationQueue.Kind kind){if(VortexClient.config.clientNotifications)QUEUE.offer(key,title,body,kind,System.nanoTime()/1_000_000);}
    public static void context(UUID id){if(!Objects.equals(profile,id)){profile=id;QUEUE.clear();positions.clear();}}
    public record Card(int x,int y,int width,int height){ }
    public static List<Card> layout(int width,int height,boolean inWorld,List<HudModules.Bounds> obstacles){
        int w=Math.min(244,Math.max(104,width/2-32)),x=width-w-8,bottom=height-(inWorld?64:8);
        var result=new ArrayList<Card>();
        for(int slot=0;slot<3;slot++){
            int y=bottom-56;
            for(int attempt=0;attempt<obstacles.size()+1;attempt++){
                int next=y;
                for(var b:obstacles)if(x<b.x()+b.width() && x+w>b.x() && y<b.y()+b.height() && y+56>b.y())next=Math.min(next,b.y()-62);
                if(next==y)break;y=next;
            }
            if(y<8 || x<8)break;result.add(new Card(x,y,w,56));bottom=y-6;
        }
        return List.copyOf(result);
    }
    public static void draw(GuiGraphicsExtractor g,boolean renderHud,boolean renderScreen){
        var mc=Minecraft.getInstance();long now=System.nanoTime();double dt=(now-frame)/1e9;frame=now;
        if(!VortexClient.config.clientNotifications){QUEUE.clear();positions.clear();return;}
        if(QUEUE.size()==0){positions.clear();return;}
        boolean inWorld=mc.level!=null && mc.gui.screen()==null;
        boolean shown=(renderHud||renderScreen) && mc.gui.overlay()==null && mc.isWindowActive() && !(mc.gui.screen() instanceof HudEditorScreen) && !(inWorld&&mc.gui.hud.isHidden());
        if(!shown){QUEUE.advance(now/1_000_000,0,false);positions.clear();return;}
        var obstacles=new ArrayList<HudModules.Bounds>();
        if(inWorld){for(var element:HudModules.Element.values())if(HudModules.enabled(element))obstacles.add(HudModules.bounds(element,g.guiWidth(),g.guiHeight()));
            obstacles.add(new HudModules.Bounds(g.guiWidth()/2-18,g.guiHeight()/2-18,36,36));}
        var slots=layout(g.guiWidth(),g.guiHeight(),inWorld,obstacles);
        var notices=QUEUE.advance(now/1_000_000,slots.size(),true);
        var live=new HashSet<Long>();for(var n:notices)live.add(n.id);positions.keySet().retainAll(live);
        if(notices.isEmpty())return;
        g.nextStratum();var font=UiFonts.body();
        for(int i=0;i<notices.size();i++){
            var n=notices.get(i);var slot=slots.get(i);float appear=n.visibility(VortexClient.config.uiAnimations);
            float previous=positions.getOrDefault(n.id,(float)slot.y()),y=ClientUi.approach(previous,slot.y(),dt);positions.put(n.id,y);
            for(var b:obstacles)if(slot.x()<b.x()+b.width() && slot.x()+slot.width()>b.x() && y<b.y()+b.height() && y+56>b.y()){y=slot.y();positions.put(n.id,y);break;}
            int x=slot.x()+Math.round((1-appear)*(slot.width()+8));
            g.pose().pushMatrix();g.pose().translate(x-slot.x(),y-slot.y());
            int accent=switch(n.kind){case SUCCESS->0xFF93C6A4;case ERROR->0xFFE39198;case INFO->0xFFA7B4CA;};
            ClientUi.panel(g,slot.x(),slot.y(),slot.width(),56,6,alpha(ClientUi.BORDER,appear),alpha(0xFF15171D,appear));
            ClientUi.rounded(g,slot.x()+7,slot.y()+10,2,34,1,alpha(accent,appear));
            g.text(font,font.plainSubstrByWidth(n.title,slot.width()-25),slot.x()+15,slot.y()+9,alpha(ClientUi.TEXT,appear),false);
            String first=font.plainSubstrByWidth(n.body,slot.width()-25),rest=n.body.substring(first.length()).strip();
            g.text(font,first,slot.x()+15,slot.y()+24,alpha(ClientUi.MUTED,appear),false);
            if(!rest.isEmpty()){String second=font.plainSubstrByWidth(rest,slot.width()-33);if(second.length()<rest.length())second+="…";
                g.text(font,second,slot.x()+15,slot.y()+35,alpha(ClientUi.MUTED,appear),false);}
            ClientUi.rounded(g,slot.x()+7,slot.y()+52,Math.round((slot.width()-14)*n.remaining()),1,0,alpha(accent,appear*.65f));
            g.pose().popMatrix();
        }
    }
    private static int alpha(int color,float amount){return (color&0xFFFFFF)|(Math.round(255*amount)<<24);}
}
