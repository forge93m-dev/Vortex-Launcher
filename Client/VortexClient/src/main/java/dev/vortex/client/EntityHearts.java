package dev.vortex.client;

import java.util.*;
import net.minecraft.client.Minecraft;
import net.minecraft.client.DeltaTracker;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.resources.Identifier;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.scores.*;
import net.minecraft.world.scores.criteria.ObjectiveCriteria;
import org.joml.Matrix4f;

public final class EntityHearts {
    public record Health(float current,float maximum,boolean player){}
    private record Entry(LivingEntity entity,Health health){}
    private static List<Entry> entries=List.of();
    private static Object level;
    private static int refresh;
    private static final Identifier EMPTY=Identifier.withDefaultNamespace("hud/heart/container"),FULL=Identifier.withDefaultNamespace("hud/heart/full"),HALF=Identifier.withDefaultNamespace("hud/heart/half");
    public static Health checked(float current,float maximum,boolean player){return !Float.isFinite(current)||!Float.isFinite(maximum)||current<0||maximum<=0?null:new Health(current,Math.max(current,maximum),player);}
    /** Other players' generic client health is not authoritative. Only use a server-published heart score. */
    public static Health playerHealth(Scoreboard board,ScoreHolder player,float maximum){
        for(var slot:new DisplaySlot[]{DisplaySlot.BELOW_NAME,DisplaySlot.LIST}){
            var objective=board.getDisplayObjective(slot);
            if(objective==null || objective.getRenderType()!=ObjectiveCriteria.RenderType.HEARTS)continue;
            var score=board.getPlayerScoreInfo(player,objective);
            if(score!=null)return checked(score.value(),maximum,true);
        }
        return null;
    }
    public static int hearts(float maximum){return Math.clamp((int)Math.ceil(maximum/2),1,10);}
    public static int fill(float current,float maximum,int index){float unit=maximum/hearts(maximum),value=current-index*unit;return value>=unit?2:value>0?1:0;}
    public static void tick(Minecraft mc){
        if(level!=mc.level){level=mc.level;entries=List.of();}
        var c=VortexClient.config;
        if(!c.entityHeartsEnabled || mc.level==null || mc.player==null){entries=List.of();return;}
        if(refresh++%4!=0)return;
        // A bounded nearest set keeps visibility raycasts off the render path.
        double range=Math.clamp(c.entityHeartsRange,8,64);
        var candidates=new ArrayList<LivingEntity>();
        for(var entity:mc.level.entitiesForRendering())if(entity instanceof LivingEntity living && living!=mc.player && living.isAlive() && !living.isInvisible() && !living.isSpectator() && ((living instanceof Player && c.entityHeartsPlayers)||(living instanceof Mob && c.entityHeartsMobs)) && living.distanceToSqr(mc.player)<=range*range)candidates.add(living);
        candidates.sort(Comparator.comparingDouble(e->e.distanceToSqr(mc.player)));
        var result=new ArrayList<Entry>();
        for(int i=0;i<Math.min(64,candidates.size());i++){
            var entity=candidates.get(i);if(!mc.player.hasLineOfSight(entity))continue;
            var health=entity instanceof Player?playerHealth(mc.level.getScoreboard(),entity,entity.getMaxHealth()):checked(entity.getHealth(),entity.getMaxHealth(),false);
            if(health!=null)result.add(new Entry(entity,health));
        }
        entries=List.copyOf(result);
    }
    public static void draw(GuiGraphicsExtractor g,DeltaTracker delta){
        var mc=Minecraft.getInstance();if(!VortexClient.config.entityHeartsEnabled || mc.level==null || level!=mc.level || mc.player==null || mc.gui.hud.isHidden() || mc.gui.screen()!=null)return;
        var camera=mc.gameRenderer.mainCamera();var origin=camera.position();var matrix=camera.getViewRotationProjectionMatrix(new Matrix4f());var font=UiFonts.body();
        for(var entry:entries){
            var entity=entry.entity();if(!entity.isAlive() || entity.isInvisible() || entity.isRemoved())continue;
            var pos=entity.getPosition(delta.getGameTimeDeltaPartialTick(false));
            var pin=WaypointProjection.project(pos.x-origin.x,pos.y+entity.getBbHeight()+.65-origin.y,pos.z-origin.z,matrix,g.guiWidth(),g.guiHeight());
            if(pin==null || pin.edge())continue;
            var hp=entry.health();int count=hearts(hp.maximum()),w=count*8,x=pin.x()-w/2,y=pin.y()-6;
            if(x<2 || x+w>g.guiWidth()-2 || y<2 || y+18>g.guiHeight()-2)continue;
            for(int i=0;i<count;i++){int xx=x+i*8;g.blitSprite(RenderPipelines.GUI_TEXTURED,EMPTY,xx,y,9,9);int fill=fill(hp.current(),hp.maximum(),i);if(fill>0)g.blitSprite(RenderPipelines.GUI_TEXTURED,fill==2?FULL:HALF,xx,y,9,9);}
            String label=number(hp.current())+(hp.player()?" HP":" / "+number(hp.maximum()));g.text(font,label,pin.x()-font.width(label)/2,y+10,0xFFE9EBF0,true);
        }
    }
    public static String number(float value){return value==Math.rint(value)?String.format(Locale.ROOT,"%.0f",value):String.format(Locale.ROOT,"%.1f",value);}
}
