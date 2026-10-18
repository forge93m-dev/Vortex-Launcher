package dev.vortex.client;

import java.nio.file.Path;
import java.util.concurrent.atomic.AtomicReference;
import java.util.function.Consumer;
import org.lwjgl.sdl.SDLDialog;
import org.lwjgl.sdl.SDL_DialogFileCallback;
import org.lwjgl.sdl.SDL_DialogFileFilter;
import org.lwjgl.system.MemoryUtil;

/** SDL native picker, independent of AWT and Minecraft's java.awt.headless=true. */
public final class CapeFilePicker {
    public record Result(Path path, boolean failed) { }
    private static final AtomicReference<Consumer<Result>> pending=new AtomicReference<>();
    private CapeFilePicker() { }
    // One reusable native callback/filter for the process, not one leaked allocation per dialog.
    // SDL may invoke the callback on any thread; all game changes are marshalled by the caller.
    private static final class Native {
        static final java.nio.ByteBuffer name=MemoryUtil.memUTF8("Skin / cape image (PNG, JPEG)");
        static final java.nio.ByteBuffer pattern=MemoryUtil.memUTF8("png;jpg;jpeg");
        static final SDL_DialogFileFilter.Buffer filters=SDL_DialogFileFilter.calloc(1);
        static {filters.get(0).name(name).pattern(pattern);}
        static final SDL_DialogFileCallback callback=SDL_DialogFileCallback.create((userdata,files,filter)-> {
            Result result;
            try {result=decode(files);} catch(Exception invalid) {result=new Result(null,true);}
            complete(result);
        });
    }
    public static Result decode(long files) {
        if(files==0) return new Result(null,true); // SDL error, not user cancellation.
        long first=MemoryUtil.memGetAddress(files);
        if(first==0) return new Result(null,false);
        return new Result(Path.of(MemoryUtil.memUTF8(first)),false);
    }
    public static boolean show(long window,Consumer<Result> completion) {
        if(!begin(completion)) return false;
        try {SDLDialog.SDL_ShowOpenFileDialog(Native.callback,0,window,Native.filters,(CharSequence)null,false);}
        catch(RuntimeException | LinkageError unavailable) {complete(new Result(null,true));}
        return true;
    }
    public static boolean begin(Consumer<Result> completion) {
        return pending.compareAndSet(null,java.util.Objects.requireNonNull(completion));
    }
    public static void complete(Result result) {
        var consumer=pending.getAndSet(null);
        if(consumer!=null) consumer.accept(result);
    }
}
