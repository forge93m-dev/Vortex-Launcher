public final class MotionSmoke {
    public static void run(ClassLoader target) throws Exception {
        Class<?> motion=Class.forName("dev.vortex.client.UiMotion",true,target);
        var pulse=motion.getMethod("pulse",double.class,double.class);
        for(double elapsed:new double[]{-1,0,170,1000,Double.NaN,Double.POSITIVE_INFINITY})
            if((float)pulse.invoke(null,elapsed,170d)!=0)throw new AssertionError("Motion pulse escaped time bounds");
        if(Math.abs((float)pulse.invoke(null,85d,170d)-1)>0.0001 || (float)pulse.invoke(null,20d,0d)!=0)
            throw new AssertionError("Motion midpoint / invalid duration");
        Class<?> entry=Class.forName("dev.vortex.client.VortexClient",true,target);
        Object config=entry.getField("config").get(null);var animations=config.getClass().getField("uiAnimations");
        boolean old=animations.getBoolean(config);
        Class<?> fade=Class.forName("dev.vortex.client.CapeFade",true,target);
        Object transition=fade.getConstructor().newInstance();var observe=fade.getMethod("observe",Object.class);
        var step=fade.getMethod("step",double.class,boolean.class);var opacity=fade.getMethod("opacity");
        var displayed=fade.getMethod("displayed");
        observe.invoke(transition,"a");
        if((float)opacity.invoke(transition)!=1 || !"a".equals(displayed.invoke(transition)))throw new AssertionError("Initial cape must be opaque");
        observe.invoke(transition,"b");step.invoke(transition,.08,true);
        if(Math.abs((float)opacity.invoke(transition)-.5f)>.001 || !"a".equals(displayed.invoke(transition)))throw new AssertionError("Old cape must fade before swapping");
        observe.invoke(transition,"c");
        if(Math.abs((float)opacity.invoke(transition)-.5f)>.001)throw new AssertionError("Rapid selection jumps in opacity");
        step.invoke(transition,.08,true);
        if((float)opacity.invoke(transition)>.001 || !"c".equals(displayed.invoke(transition)))throw new AssertionError("Latest cape must swap only at zero opacity");
        step.invoke(transition,.1,true);float partial=(float)opacity.invoke(transition);
        if(partial<=0 || partial>=1)throw new AssertionError("New cape must fade in");
        observe.invoke(transition,new String("c"));step.invoke(transition,.1,true);step.invoke(transition,.1,true);
        if((float)opacity.invoke(transition)!=1)throw new AssertionError("Stable cape retriggers animation");
        observe.invoke(transition,(Object)null);step.invoke(transition,.08,true);
        if(!"c".equals(displayed.invoke(transition)) || Math.abs((float)opacity.invoke(transition)-.5f)>.001)throw new AssertionError("Removal must fade old cape out");
        step.invoke(transition,.1,true);
        if(displayed.invoke(transition)!=null)throw new AssertionError("Removal did not settle");
        observe.invoke(transition,"d");
        if((float)opacity.invoke(transition)!=0)throw new AssertionError("Addition must start transparent");
        step.invoke(transition,Double.NaN,true);step.invoke(transition,-1d,true);
        if((float)opacity.invoke(transition)!=0)throw new AssertionError("Invalid time advances animation");
        step.invoke(transition,.06,true);float before=(float)opacity.invoke(transition);
        observe.invoke(transition,"e");
        if((float)opacity.invoke(transition)!=before)throw new AssertionError("Interrupted fade-in jumps");
        step.invoke(transition,.1,false);
        if((float)opacity.invoke(transition)!=1 || !"e".equals(displayed.invoke(transition)))throw new AssertionError("Reduced motion does not snap");
        observe.invoke(transition,"f");step.invoke(transition,.04,true);observe.invoke(transition,"e");step.invoke(transition,.1,true);
        if((float)opacity.invoke(transition)!=1 || !"e".equals(displayed.invoke(transition)))throw new AssertionError("Reselecting displayed cape should reverse fade smoothly");
        if((boolean)fade.getMethod("retains",Object.class).invoke(transition,"f"))throw new AssertionError("Obsolete texture retained");
        verifyCapeSubmission(target);
        animations.setBoolean(config,false);
        Class<?> feedback=Class.forName("dev.vortex.client.UiMotion$Feedback",true,target);
        Object widget=feedback.getConstructor().newInstance();feedback.getMethod("press").invoke(widget);
        feedback.getMethod("frame",boolean.class,boolean.class).invoke(widget,true,true);
        if(feedback.getField("hover").getFloat(widget)!=1 || feedback.getField("selected").getFloat(widget)!=1 ||
            (float)feedback.getMethod("scale").invoke(widget)!=1)throw new AssertionError("Disabled animations still interpolate widget state");
        animations.setBoolean(config,old);
        System.out.println("PASS Cape-only fade-out/swap/fade-in, rapid selection/reversal, removal/addition, texture retention, translucent submission, world isolation and reduced motion");
    }

    private static void verifyCapeSubmission(ClassLoader target) throws Exception {
        Class<?> layer=Class.forName("net.minecraft.client.renderer.entity.layers.CapeLayer",true,target);
        if(java.util.Arrays.stream(layer.getDeclaredMethods()).noneMatch(m->m.getName().contains("vortex$fade")))
            throw new AssertionError("Cape fade hook not applied to actual CapeLayer");
        Class<?> skin=Class.forName("net.minecraft.world.entity.player.PlayerSkin",true,target);
        Object base=Class.forName("net.minecraft.client.resources.DefaultPlayerSkin",true,target).getMethod("getDefaultSkin").invoke(null);
        Object texture=skin.getMethod("body").invoke(base);
        Class<?> tex=Class.forName("net.minecraft.core.ClientAsset$Texture",true,target);
        Object withCape=skin.getConstructors()[0].newInstance(texture,texture,null,skin.getMethod("model").invoke(base),false);
        Class<?> preview=Class.forName("dev.vortex.client.WardrobePreview",true,target);
        Object avatar=preview.getMethod("state",skin).invoke(null,withCape);
        var alpha=avatar.getClass().getField("capeOpacity");alpha.setFloat(avatar,.5f);
        Class<?> collector=Class.forName("net.minecraft.client.renderer.SubmitNodeCollector",true,target);
        var calls=new java.util.ArrayList<Object[]>();
        Object spy=java.lang.reflect.Proxy.newProxyInstance(target,new Class<?>[]{collector},(p,m,a)->{if(m.getName().equals("submitModel"))calls.add(a);return null;});
        var submit=Class.forName("dev.vortex.client.WardrobeCapeRendering",true,target).getMethod("submit",collector,
            Class.forName("net.minecraft.client.model.Model",true,target),Object.class,
            Class.forName("com.mojang.blaze3d.vertex.PoseStack",true,target),Class.forName("net.minecraft.client.renderer.rendertype.RenderType",true,target),int.class,int.class,int.class);
        submit.invoke(null,spy,null,avatar,null,null,123,456,789);
        if(calls.size()!=1 || calls.getFirst().length!=9 || calls.getFirst()[3]==null ||
            (int)calls.getFirst()[6]!=0x80FFFFFF || (int)calls.getFirst()[4]!=123 || (int)calls.getFirst()[5]!=456 || (int)calls.getFirst()[8]!=789)
            throw new AssertionError("Only cape color alpha should change; lighting/overlay/outline must survive");
        calls.clear();alpha.setFloat(avatar,0);submit.invoke(null,spy,null,avatar,null,null,1,2,3);
        if(!calls.isEmpty())throw new AssertionError("Fully transparent cape still submitted");
        alpha.setFloat(avatar,1);submit.invoke(null,spy,null,avatar,null,null,1,2,3);
        if(calls.size()!=1 || calls.getFirst().length!=7)throw new AssertionError("Opaque cape must use vanilla path");
        calls.clear();Object world=Class.forName("net.minecraft.client.renderer.entity.state.AvatarRenderState",true,target).getConstructor().newInstance();
        submit.invoke(null,spy,null,world,null,null,1,2,3);
        if(calls.size()!=1 || calls.getFirst().length!=7 || calls.getFirst()[1]!=world || calls.getFirst()[3]!=null)
            throw new AssertionError("World players must pass through unchanged");
        if((float)avatar.getClass().getField("capeFlap").get(avatar)!=6 || (float)avatar.getClass().getField("capeLean2").get(avatar)!=0 ||
            (float)avatar.getClass().getField("scale").get(avatar)!=1)throw new AssertionError("Cape animation must not move/scale avatar or flap cloth");
    }
}
