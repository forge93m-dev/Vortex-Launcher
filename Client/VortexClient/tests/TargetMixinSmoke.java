import java.nio.file.*;
import java.util.*;
import java.util.jar.JarFile;
import org.objectweb.asm.*;
import net.fabricmc.api.EnvType;
import net.fabricmc.loader.impl.launch.knot.Knot;

/** Transform every declared target without starting a game, renderer or account session. */
public final class TargetMixinSmoke {
    public static void main(String[] args) throws Exception {
        Set<String> targets = new TreeSet<>();
        try (JarFile jar = new JarFile(args[2])) {
            for (var entries = jar.entries(); entries.hasMoreElements();) {
                var entry = entries.nextElement();
                if (!entry.getName().startsWith("dev/vortex/client/mixin/") || !entry.getName().endsWith(".class")) continue;
                try (var input = jar.getInputStream(entry)) {
                    new ClassReader(input).accept(new ClassVisitor(Opcodes.ASM9) {
                        public AnnotationVisitor visitAnnotation(String descriptor, boolean visible) {
                            if (!descriptor.equals("Lorg/spongepowered/asm/mixin/Mixin;")) return null;
                            return new AnnotationVisitor(Opcodes.ASM9) {
                                public AnnotationVisitor visitArray(String name) {
                                    return new AnnotationVisitor(Opcodes.ASM9) {
                                        public void visit(String unused, Object value) {
                                            if (value instanceof Type type) targets.add(type.getClassName());
                                        }
                                    };
                                }
                            };
                        }
                    }, ClassReader.SKIP_CODE | ClassReader.SKIP_DEBUG | ClassReader.SKIP_FRAMES);
                }
            }
        }
        if (targets.size() < 15) throw new AssertionError("Unexpectedly small mixin target set: " + targets);
        var knot = new Knot(EnvType.CLIENT);
        ClassLoader loader = knot.init(new String[]{"--gameDir",args[0],"--version",args[1]});
        for (String name : targets) {
            Class<?> transformed = Class.forName(name, false, loader);
            if (Arrays.stream(transformed.getDeclaredMethods()).noneMatch(m -> m.getName().contains("vortex$")))
                throw new AssertionError("No Vortex hook in " + name);
            System.out.println("PASS " + args[1] + " mixins: " + name);
        }
        Class<?> client = Class.forName("dev.vortex.client.VortexClient", true, loader);
        client.getMethod("onInitializeClient").invoke(client.getConstructor().newInstance());
        Class<?> screen = Class.forName("dev.vortex.client.VortexScreen", true, loader);
        for (int[] size : new int[][]{{320,240},{640,360},{854,480},{1920,1080}}) {
            Object layout = screen.getMethod("menuLayout",int.class,int.class).invoke(null,size[0],size[1]);
            Class<?> geometry = layout.getClass();
            int x=(int)geometry.getMethod("x").invoke(layout), y=(int)geometry.getMethod("y").invoke(layout);
            int w=(int)geometry.getMethod("width").invoke(layout), h=(int)geometry.getMethod("height").invoke(layout);
            if(x<0 || y<0 || x+w>size[0] || y+h>size[1]) throw new AssertionError("Menu layout outside target viewport");
        }
        float zoom=(float)client.getMethod("adjustZoomStrength",float.class,double.class).invoke(null,4f,1d);
        Class<?> chrome = Class.forName("dev.vortex.client.VortexMenuChrome", true, loader);
        for (int[] size : new int[][]{{320,240},{430,300},{640,360},{854,480}})
            for (boolean paused : new boolean[]{false,true}) for (int count : new int[]{6,9,12,16}) {
                Object layout=chrome.getMethod("layout",int.class,int.class,int.class,boolean.class).invoke(null,size[0],size[1],count,paused);
                Class<?> geometry=layout.getClass();
                int primary=(int)geometry.getMethod("primaryCount").invoke(layout);
                for(int i=0;i<count;i++) {
                    int x=(int)geometry.getMethod("buttonX",int.class).invoke(layout,i),y=(int)geometry.getMethod("buttonY",int.class).invoke(layout,i);
                    int w=(int)geometry.getMethod("widthFor",int.class).invoke(layout,i),h=(int)geometry.getMethod("heightFor",int.class).invoke(layout,i);
                    if(x<0 || y<0 || x+w>size[0] || y+h>size[1]-14 || (i>=primary && w<64))
                        throw new AssertionError("Labeled title/pause menu clips target viewport");
                }
            }
        if(!Float.isFinite(zoom) || zoom<=4f) throw new AssertionError("Target zoom math failed");
        System.out.println("PASS " + args[1] + " module-menu layouts and zoom logic");
        CollectionSmoke.run(loader,Path.of(args[0]));
        ResourceToolsSmoke.run(loader,Path.of(args[0]));
        System.out.println("TARGET_MIXINS_OK " + args[1] + " (not a full GUI/gameplay test)");
    }
}
