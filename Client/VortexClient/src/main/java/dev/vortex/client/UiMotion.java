package dev.vortex.client;

/** Small time-based transitions; never affect game ticks or vanilla HUD. */
public final class UiMotion {
    private UiMotion() { }
    public static float pulse(double elapsed,double duration) {
        if(!Double.isFinite(elapsed) || !Double.isFinite(duration) || duration<=0 || elapsed<=0 || elapsed>=duration)return 0;
        return (float)Math.sin(Math.PI*elapsed/duration);
    }
    /** Tiny reusable widget state; kept by the owning screen across widget rebuilds. */
    public static final class Feedback {
        public final long born=System.nanoTime();
        public float hover,selected;
        private long frame=System.nanoTime(),pressed;
        public void press(){pressed=System.nanoTime();}
        public void frame(boolean hovered,boolean chosen){
            long now=System.nanoTime();double seconds=(now-frame)/1e9;frame=now;
            hover=ClientUi.approach(hover,hovered?1:0,seconds);selected=ClientUi.approach(selected,chosen?1:0,seconds);
        }
        public float scale(){return VortexClient.config.uiAnimations?1-.03f*pulse((System.nanoTime()-pressed)/1e6,170):1;}
    }
    public static float reveal(double milliseconds) {
        if (!Double.isFinite(milliseconds)) return milliseconds > 0 ? 1 : 0;
        float t = (float)Math.clamp(milliseconds / 220.0, 0, 1);
        return 1 - (1 - t) * (1 - t) * (1 - t);
    }
    public static float progress(long started) {
        return VortexClient.config.uiAnimations ? reveal((System.nanoTime() - started) / 1e6) : 1;
    }
    public static void begin(net.minecraft.client.gui.GuiGraphicsExtractor g, int x, int y, int width, int height, float scale, float offset) {
        g.pose().pushMatrix();
        g.pose().translate(x + width / 2f, y + height / 2f + offset);
        g.pose().scale(scale, scale);
        g.pose().translate(-x - width / 2f, -y - height / 2f);
    }
}
