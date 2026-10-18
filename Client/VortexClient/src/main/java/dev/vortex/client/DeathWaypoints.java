package dev.vortex.client;
import java.util.*;
import net.minecraft.client.Minecraft;
public final class DeathWaypoints {
    public static final class Gate {
        private Object player;private boolean dead;
        public boolean died(Object identity,boolean dying){if(identity!=player){player=identity;dead=false;}boolean result=identity!=null && dying && !dead;dead=dying;return result;}
    }
    private static final Gate GATE=new Gate();
    private static final ArrayDeque<WaypointsStore.Point> pending=new ArrayDeque<>();
    public static void tick(Minecraft mc){
        boolean died=GATE.died(mc.player,mc.player!=null && mc.player.isDeadOrDying());if(died)capture(mc);
        if(!pending.isEmpty() && WaypointsFeature.ready() && !WaypointsFeature.busy()){
            var point=pending.removeFirst();WaypointsFeature.save(point,null,ok->{if(!ok)ClientNotifications.info("death-save","Death waypoint not saved","Coordinates: "+point.x()+", "+point.y()+", "+point.z()+". "+WaypointsFeature.message);});
        }
    }
    public static void healthDepleted(Minecraft mc){if(mc.player!=null && GATE.died(mc.player,true))capture(mc);}
    private static void capture(Minecraft mc){var scope=WaypointsFeature.scope();
        if(scope!=null && VortexClient.config.deathWaypointsEnabled){
            var pos=mc.player.blockPosition();String time=java.time.LocalTime.now().format(java.time.format.DateTimeFormatter.ofPattern("HH:mm:ss",Locale.ROOT));
            var point=new WaypointsStore.Point(UUID.randomUUID(),scope.world(),scope.dimension(),"Death "+time,pos.getX(),pos.getY(),pos.getZ(),0xFF969F,false,"Deaths");
            if(pending.size()==8)pending.removeFirst();pending.addLast(point);
            ClientNotifications.info("death-coordinates","Death coordinates",pos.getX()+", "+pos.getY()+", "+pos.getZ()+" · "+scope.dimension());
        }
    }
}
