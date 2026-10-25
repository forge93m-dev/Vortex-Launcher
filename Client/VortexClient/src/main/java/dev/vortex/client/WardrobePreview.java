package dev.vortex.client;

import java.util.function.Supplier;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.narration.NarrationElementOutput;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.renderer.entity.state.AvatarRenderState;
import net.minecraft.network.chat.Component;
import net.minecraft.world.entity.EntityTypes;
import net.minecraft.world.entity.Pose;
import net.minecraft.world.entity.player.PlayerSkin;
import net.minecraft.core.ClientAsset;
import org.joml.Quaternionf;
import org.joml.Vector3f;

/** Independent render state: never turns, equips or otherwise modifies the actual player. */
public final class WardrobePreview extends AbstractWidget {
    private final Supplier<PlayerSkin> appearance;
    private final Camera camera;
    private long previousFrame=System.nanoTime();
    public record View(float yaw,float pitch,float zoom,float focusY) { }
    /** Marker confined to this widget; world players always use vanilla render state. */
    public static final class PreviewState extends AvatarRenderState {
        public float capeOpacity=1;
    }
    /** Screen-owned camera survives widget rebuilds, gallery loads and window resizes. */
    public static final class Camera {
        private float yaw=25,pitch=-5,zoom=1,focusY=.9f;
        private float targetYaw=25,targetPitch=-5,targetZoom=1,targetFocusY=.9f;
        public final CapeFade<ClientAsset.Texture> capeFade=new CapeFade<>();
        public void focusCape(boolean cape){targetYaw=cape?180:25;targetPitch=-5;targetZoom=cape?1.65f:1;targetFocusY=cape?1.12f:.9f;}
        public View view(){return new View(yaw,pitch,zoom,focusY);}
        public void step(double seconds,boolean animate){
            if(!Double.isFinite(seconds))seconds=0;
            float amount=animate?(float)(1-Math.exp(-Math.clamp(seconds,0,.1)*14)):1;
            yaw+=(float)Math.IEEEremainder(targetYaw-yaw,360)*amount;
            pitch+=(targetPitch-pitch)*amount;zoom+=(targetZoom-zoom)*amount;focusY+=(targetFocusY-focusY)*amount;
        }
        public void drag(double dx,double dy){
            yaw=targetYaw=turn(yaw,dx);
            if(Double.isFinite(dy))pitch=targetPitch=(float)Math.clamp(pitch-dy*.5,-25,25);
        }
    }
    public WardrobePreview(int x,int y,int width,int height,Supplier<PlayerSkin> appearance) {
        this(x,y,width,height,appearance,new Camera());
    }
    public WardrobePreview(int x,int y,int width,int height,Supplier<PlayerSkin> appearance,Camera camera) {
        super(x,y,width,height,Component.literal("Player preview. Drag to rotate."));this.appearance=appearance;
        this.camera=camera;
    }
    public static float turn(float angle,double delta) {
        return Double.isFinite(delta)?(float)Math.IEEEremainder(angle+Math.clamp(delta,-10000,10000)*1.4,360):angle;
    }
    public void back() {camera.focusCape(true);}
    public void front() {camera.focusCape(false);}
    public static AvatarRenderState state(PlayerSkin skin) {
        var state=new PreviewState();state.entityType=EntityTypes.PLAYER;state.skin=skin;
        state.boundingBoxWidth=.6f;state.boundingBoxHeight=1.8f;state.eyeHeight=1.62f;
        state.scale=state.ageScale=1;state.pose=Pose.STANDING;state.bodyRot=180;
        state.showHat=state.showJacket=state.showLeftPants=state.showRightPants=state.showLeftSleeve=state.showRightSleeve=true;
        state.showCape=skin.cape()!=null;state.capeFlap=6;state.lightCoords=0xF000F0;
        return state;
    }
    @Override protected void extractWidgetRenderState(GuiGraphicsExtractor g,int mx,int my,float delta) {
        var skin=appearance.get();if(skin==null)return;
        long now=System.nanoTime();double seconds=(now-previousFrame)/1e9;previousFrame=now;
        camera.capeFade.step(seconds,VortexClient.config.uiAnimations);
        // All frames of an animated cape count as one cape, so it animates instead of fading every frame.
        camera.capeFade.observe(CapeCatalog.canonical(skin.cape()));
        if(!VortexClient.config.uiAnimations)camera.capeFade.step(0,false);
        camera.step(seconds,VortexClient.config.uiAnimations);
        var view=camera.view();float scale=Math.min(getHeight()/2.15f,getWidth()/1.25f)*view.zoom();
        var cape=CapeCatalog.current(camera.capeFade.displayed());
        if(!java.util.Objects.equals(cape,skin.cape()))skin=new PlayerSkin(skin.body(),cape,skin.elytra(),skin.model(),skin.secure());
        var avatar=(PreviewState)state(skin);avatar.capeOpacity=camera.capeFade.opacity();
        g.entity(avatar,scale,new Vector3f(0,view.focusY(),0),new Quaternionf().rotateZ((float)Math.PI).rotateY((float)Math.toRadians(view.yaw())),
            new Quaternionf().rotateX((float)Math.toRadians(view.pitch())),getX(),getY(),getRight(),getBottom());
    }
    @Override protected void onDrag(MouseButtonEvent event,double dx,double dy) {
        camera.drag(dx,dy);
    }
    @Override protected void updateWidgetNarration(NarrationElementOutput narration) {defaultButtonNarrationText(narration);}
}
