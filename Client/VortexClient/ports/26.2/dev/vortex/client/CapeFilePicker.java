package dev.vortex.client;
import java.nio.file.Path;
import java.util.concurrent.atomic.AtomicReference;
import java.util.function.Consumer;
import org.lwjgl.system.MemoryStack;
import org.lwjgl.system.MemoryUtil;
import org.lwjgl.util.tinyfd.TinyFileDialogs;

/** Minecraft 26.2 ships GLFW/tinyfd, not the SDL dialog API used by 26.3. */
public final class CapeFilePicker {
    public record Result(Path path, boolean failed) { }
    private static final AtomicReference<Consumer<Result>> pending = new AtomicReference<>();
    private CapeFilePicker() { }
    public static Result decode(long files) {
        if(files==0)return new Result(null,true);
        long first=MemoryUtil.memGetAddress(files);
        return first==0?new Result(null,false):new Result(Path.of(MemoryUtil.memUTF8(first)),false);
    }
    public static boolean show(long window, Consumer<Result> completion) {
        if(!begin(completion))return false;
        Thread worker = new Thread(() -> {
            try(MemoryStack stack=MemoryStack.stackPush()) {
                var patterns=stack.mallocPointer(3);
                patterns.put(stack.UTF8("*.png")).put(stack.UTF8("*.jpg")).put(stack.UTF8("*.jpeg")).flip();
                String selected=TinyFileDialogs.tinyfd_openFileDialog("Select skin / cape image",null,patterns,"PNG / JPEG images",false);
                complete(new Result(selected==null?null:Path.of(selected),false));
            } catch(RuntimeException | LinkageError unavailable) { complete(new Result(null,true)); }
        },"Vortex-image-picker");
        worker.setDaemon(true);worker.start();return true;
    }
    public static boolean begin(Consumer<Result> completion) { return pending.compareAndSet(null,java.util.Objects.requireNonNull(completion)); }
    public static void complete(Result result) { var consumer=pending.getAndSet(null);if(consumer!=null)consumer.accept(result); }
}
