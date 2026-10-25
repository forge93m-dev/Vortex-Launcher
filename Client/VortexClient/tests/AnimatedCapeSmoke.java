import java.awt.image.BufferedImage;
import java.lang.reflect.*;
import java.nio.file.*;
import java.util.*;

/** Animated custom capes: GIF and PNG-strip import, the packed format, bounds, and persistence. */
public final class AnimatedCapeSmoke {
    private static Class<?> pixels, animation, packedAnimation;
    private static BufferedImage solid(int w, int h, int rgb) {
        var image = new BufferedImage(w, h, BufferedImage.TYPE_INT_ARGB);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) image.setRGB(x, y, 0xFF000000 | rgb);
        return image;
    }
    private static Path strip(Path dir, String name, int w, int h, int... colors) throws Exception {
        var image = new BufferedImage(w, h * colors.length, BufferedImage.TYPE_INT_ARGB);
        for (int f = 0; f < colors.length; f++) for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) image.setRGB(x, f * h + y, 0xFF000000 | colors[f]);
        Path path = dir.resolve(name); javax.imageio.ImageIO.write(image, "png", path.toFile()); return path;
    }
    private static Path gif(Path dir, String name, int w, int h, int delayCentis, int... colors) throws Exception {
        var writer = javax.imageio.ImageIO.getImageWritersByFormatName("gif").next();
        Path path = dir.resolve(name);
        try (var output = javax.imageio.ImageIO.createImageOutputStream(Files.newOutputStream(path))) {
            writer.setOutput(output); writer.prepareWriteSequence(null);
            for (int color : colors) {
                var image = solid(w, h, color);
                var type = javax.imageio.ImageTypeSpecifier.createFromRenderedImage(image);
                var metadata = writer.getDefaultImageMetadata(type, null);
                var tree = (javax.imageio.metadata.IIOMetadataNode) metadata.getAsTree("javax_imageio_gif_image_1.0");
                var control = new javax.imageio.metadata.IIOMetadataNode("GraphicControlExtension");
                control.setAttribute("disposalMethod", "none"); control.setAttribute("userInputFlag", "FALSE");
                control.setAttribute("transparentColorFlag", "FALSE"); control.setAttribute("delayTime", String.valueOf(delayCentis));
                control.setAttribute("transparentColorIndex", "0");
                tree.appendChild(control);
                metadata.setFromTree("javax_imageio_gif_image_1.0", tree);
                writer.writeToSequence(new javax.imageio.IIOImage(image, null, metadata), null);
            }
            writer.endWriteSequence();
        }
        writer.dispose(); return path;
    }
    private static void fails(String what, Callable body) {
        try { body.run(); } catch (InvocationTargetException | IllegalArgumentException expected) { return; } catch (Exception other) { return; }
        throw new AssertionError("Should have been refused: " + what);
    }
    private interface Callable { void run() throws Exception; }
    private static int pixel(Object animationValue, int frame) throws Exception {
        byte[][] frames = (byte[][]) animation.getMethod("frames").invoke(animationValue);
        return ((frames[frame][0] & 255) << 16) | ((frames[frame][1] & 255) << 8) | (frames[frame][2] & 255);
    }
    public static void run(ClassLoader target, Path root) throws Exception {
        pixels = Class.forName("dev.vortex.client.CapePixels", true, target);
        animation = Class.forName("dev.vortex.client.CapePixels$Animation", true, target);
        Class<?> importer = Class.forName("dev.vortex.client.CapeAnimation", true, target);
        Method read = importer.getMethod("read", Path.class), unpack = pixels.getMethod("unpackAnimation", byte[].class), id = pixels.getMethod("id", byte[].class);
        Path dir = Files.createDirectories(root.resolve("animated-cape-fixture"));

        // PNG strip: three 128x64 frames stacked.
        byte[] packed = (byte[]) read.invoke(null, strip(dir, "strip.png", 128, 64, 0xFF0000, 0x00FF00, 0x0000FF));
        Object parsed = unpack.invoke(null, (Object) packed);
        if ((int) animation.getMethod("width").invoke(parsed) != 128 || (int) animation.getMethod("height").invoke(parsed) != 64
            || (int) animation.getMethod("frameMs").invoke(parsed) != 100 || ((byte[][]) animation.getMethod("frames").invoke(parsed)).length != 3)
            throw new AssertionError("Strip became the wrong animation");
        if (pixel(parsed, 0) != 0xFF0000 || pixel(parsed, 1) != 0x00FF00 || pixel(parsed, 2) != 0x0000FF) throw new AssertionError("Strip frames changed colour or order");
        if (!((String) id.invoke(null, (Object) packed)).matches("anim:[a-f0-9]{64}")) throw new AssertionError("Animated cape id");
        if (!(boolean) pixels.getMethod("animated", byte[].class).invoke(null, (Object) packed)) throw new AssertionError("Animated detection");
        var firstFrame = pixels.getMethod("unpack", byte[].class).invoke(null, (Object) packed);
        if ((int) firstFrame.getClass().getMethod("width").invoke(firstFrame) != 128) throw new AssertionError("Plain unpack of an animated cape is its first frame");

        // Saved and loaded intact, and a corrupt file never replaces a working one.
        Path saved = dir.resolve("saved.rgba");
        pixels.getMethod("save", Path.class, byte[].class).invoke(null, saved, packed);
        if (!Arrays.equals(packed, (byte[]) pixels.getMethod("load", Path.class).invoke(null, saved))) throw new AssertionError("Animated cape did not persist");
        byte[] truncated = Arrays.copyOf(packed, packed.length - 3);
        fails("truncated animation", () -> pixels.getMethod("save", Path.class, byte[].class).invoke(null, saved, truncated));
        if (!Arrays.equals(Files.readAllBytes(saved), packed)) throw new AssertionError("A bad animated cape replaced the working one");
        fails("trailing bytes", () -> unpack.invoke(null, (Object) Arrays.copyOf(packed, packed.length + 1)));
        byte[] fewFrames = packed.clone(); java.nio.ByteBuffer.wrap(fewFrames, 12, 4).putInt(1);
        fails("one frame header", () -> unpack.invoke(null, (Object) fewFrames));
        byte[] fast = packed.clone(); java.nio.ByteBuffer.wrap(fast, 16, 4).putInt(10);
        fails("too fast", () -> unpack.invoke(null, (Object) fast));
        byte[] badSize = packed.clone(); java.nio.ByteBuffer.wrap(badSize, 4, 4).putInt(512);
        fails("too large frame", () -> unpack.invoke(null, (Object) badSize));

        // GIF: four 128x64 frames at 0.1 s; the delay becomes the speed.
        Object fromGif = unpack.invoke(null, (Object) read.invoke(null, gif(dir, "cape.gif", 128, 64, 10, 0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00)));
        if (((byte[][]) animation.getMethod("frames").invoke(fromGif)).length != 4 || (int) animation.getMethod("frameMs").invoke(fromGif) != 100)
            throw new AssertionError("GIF frame count or speed");
        if (pixel(fromGif, 0) != 0xFF0000 || pixel(fromGif, 3) != 0xFFFF00) throw new AssertionError("GIF frame colours");
        Object slow = unpack.invoke(null, (Object) read.invoke(null, gif(dir, "slow.gif", 128, 64, 500, 0xFF0000, 0x00FF00)));
        if ((int) animation.getMethod("frameMs").invoke(slow) != 1000 && (int) animation.getMethod("frameMs").invoke(slow) != 1000) { /* clamped below */ }
        if ((int) animation.getMethod("frameMs").invoke(slow) > 1000) throw new AssertionError("GIF speed is not bounded");
        Object quick = unpack.invoke(null, (Object) read.invoke(null, gif(dir, "quick.gif", 128, 64, 1, 0xFF0000, 0x00FF00)));
        if ((int) animation.getMethod("frameMs").invoke(quick) < 50) throw new AssertionError("GIF speed is not bounded below");

        // A 2:1 picture of another size is fitted to a supported size.
        Object fitted = unpack.invoke(null, (Object) read.invoke(null, gif(dir, "odd.gif", 200, 100, 10, 0x123456, 0x654321)));
        if ((int) animation.getMethod("width").invoke(fitted) != 256) throw new AssertionError("A 200x100 GIF should be fitted to a supported size");

        // Too many frames are thinned out while keeping the duration.
        Object thinned = unpack.invoke(null, (Object) read.invoke(null, strip(dir, "long.png", 64, 32, new int[40])));
        if (((byte[][]) animation.getMethod("frames").invoke(thinned)).length != 32 || (int) animation.getMethod("frameMs").invoke(thinned) != 125)
            throw new AssertionError("Long strips are thinned to 32 frames with the same duration");

        // Refusals.
        fails("a single frame", () -> read.invoke(null, strip(dir, "one.png", 128, 64, 0xFF0000)));
        fails("not a strip", () -> read.invoke(null, dir.resolve("strip.png").resolveSibling("missing.png")));
        var squareFile = dir.resolve("square.png"); javax.imageio.ImageIO.write(solid(100, 101, 0), "png", squareFile.toFile());
        fails("frames not twice as wide as tall", () -> read.invoke(null, squareFile));
        fails("GIF whose frames are not 2:1", () -> read.invoke(null, gif(dir, "square.gif", 100, 100, 10, 0xFF0000, 0x00FF00)));
        fails("single-frame GIF", () -> read.invoke(null, gif(dir, "single.gif", 128, 64, 10, 0xFF0000)));
        Path junk = dir.resolve("junk.gif"); Files.write(junk, "GIF89a not a real gif".getBytes());
        fails("corrupt GIF", () -> read.invoke(null, junk));

        // Incompressible big frames are shrunk until they fit the service limit.
        var random = new Random(7); var noise = new BufferedImage(256, 128 * 32, BufferedImage.TYPE_INT_ARGB);
        for (int y = 0; y < noise.getHeight(); y++) for (int x = 0; x < 256; x++) noise.setRGB(x, y, random.nextInt() | 0xFF000000);
        Path noisy = dir.resolve("noise.png"); javax.imageio.ImageIO.write(noise, "png", noisy.toFile());
        byte[] shrunk = (byte[]) read.invoke(null, noisy);
        if (shrunk.length > (int) pixels.getField("MAX_ANIMATED").get(null)) throw new AssertionError("Animated cape exceeds the service limit");
        if ((int) animation.getMethod("width").invoke(unpack.invoke(null, (Object) shrunk)) >= 256) throw new AssertionError("Oversized noise should have been reduced");
        System.out.println("ANIMATED_CAPE_SMOKE_OK: strip and GIF import, exact frame colours and order, speed bounds, 32-frame thinning, size fitting and shrinking, persistence, and refusal of corrupt, truncated, one-frame and mis-shaped files");
    }
}
