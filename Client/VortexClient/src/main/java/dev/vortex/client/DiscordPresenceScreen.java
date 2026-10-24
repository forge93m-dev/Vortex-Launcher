package dev.vortex.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

public final class DiscordPresenceScreen extends Screen {
    private final Screen parent;
    private final long opened = System.nanoTime();
    private String message = "";
    public record Layout(int x, int y, int width, int height) { }
    public static Layout layout(int width, int height) {
        int w = Math.min(490, Math.max(40, width - 24)), h = Math.min(258, Math.max(40, height - 16));
        return new Layout((width-w)/2, (height-h)/2, w, h);
    }
    public DiscordPresenceScreen(Screen parent) {
        super(Minecraft.getInstance(), UiFonts.body(), Component.literal("Discord Rich Presence")); this.parent = parent;
    }
    @Override protected void init() {
        var l = layout(width,height);
        addRenderableWidget(new ClientUi.ActionButton(l.x()+12,l.y()+40,l.width()-24,26,
            Component.literal(VortexClient.config.discordPresenceEnabled ? "Share activity: ON" : "Share activity: OFF"), button -> {
                boolean previous = VortexClient.config.discordPresenceEnabled;
                VortexClient.config.discordPresenceEnabled = !previous;
                if (!VortexClient.config.save()) { VortexClient.config.discordPresenceEnabled=previous; message="Could not save. Check the game log."; return; }
                if (!VortexClient.config.discordPresenceEnabled) DiscordServerInvites.stop();
                message="";
                button.setMessage(Component.literal(VortexClient.config.discordPresenceEnabled ? "Share activity: ON" : "Share activity: OFF"));
            }));
        addRenderableWidget(new ClientUi.ActionButton(l.x()+12,l.y()+l.height()-34,l.width()-24,24,Component.literal("Back"),b->onClose()));
        addRenderableWidget(new ClientUi.ActionButton(l.x()+12,l.y()+108,l.width()-24,24,Component.literal("Enable server invitations (30 min)"),b->{DiscordPresence.startInvites();}));
        addRenderableWidget(new ClientUi.ActionButton(l.x()+12,l.y()+138,l.width()-24,24,Component.literal("Stop server invitations"),b->{DiscordServerInvites.stop();}));
    }
    @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta) {
        var l=layout(width,height); int x=l.x()+12,w=l.width()-24;
        ClientUi.beginPage(g,width,height,l.x(),l.y(),l.width(),l.height(),"Discord Rich Presence","social",opened);
        ClientUi.text(g,message.isEmpty()?DiscordPresence.status():message,x,l.y()+78,w,ClientUi.TEXT);
        ClientUi.text(g,"Invitations reveal the server address to Discord and recipients.",x,l.y()+94,w,ClientUi.MUTED);
        if (l.height() >= 220) ClientUi.text(g,DiscordServerInvites.message,x,l.y()+174,w,ClientUi.TEXT);
        if (l.height() >= 250) {
            ClientUi.text(g,"No singleplayer hosting. No modpack transfer. Changes: up to 15 sec.",x,l.y()+190,w,ClientUi.MUTED);
            ClientUi.text(g,"Friend: Vortex Launcher open, Receive invitations ON, Minecraft closed.",x,l.y()+204,w,ClientUi.MUTED);
        }
        super.extractRenderState(g,mx,my,delta);g.pose().popMatrix();
    }
    @Override public boolean isPauseScreen(){return false;}
    @Override public boolean isInGameUi(){return minecraft.level!=null;}
    @Override public void extractBackground(GuiGraphicsExtractor g,int mx,int my,float delta){if(isInGameUi())minecraft.gui.hud.extractDeferredSubtitles();else extractPanorama(g,delta);}
    @Override public void onClose(){minecraft.gui.setScreen(parent);}
}
