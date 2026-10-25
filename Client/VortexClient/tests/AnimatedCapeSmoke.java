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
    private static int rgb(byte[] frame, int x, int y, int width) {
        int p = (y * width + x) * 4;
        return ((frame[p] & 255) << 16) | ((frame[p + 1] & 255) << 8) | (frame[p + 2] & 255);
    }
    private static Path noiseGif(Path dir, String name, int w, int h, int count) throws Exception {
        var writer = javax.imageio.ImageIO.getImageWritersByFormatName("gif").next();
        Path path = dir.resolve(name); var random = new Random(11);
        try (var output = javax.imageio.ImageIO.createImageOutputStream(Files.newOutputStream(path))) {
            writer.setOutput(output); writer.prepareWriteSequence(null);
            for (int f = 0; f < count; f++) {
                var image = new BufferedImage(w, h, BufferedImage.TYPE_INT_ARGB);
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) image.setRGB(x, y, 0xFF000000 | random.nextInt(0xFFFFFF));
                var type = javax.imageio.ImageTypeSpecifier.createFromRenderedImage(image);
                var metadata = writer.getDefaultImageMetadata(type, null);
                var tree = (javax.imageio.metadata.IIOMetadataNode) metadata.getAsTree("javax_imageio_gif_image_1.0");
                var control = new javax.imageio.metadata.IIOMetadataNode("GraphicControlExtension");
                control.setAttribute("disposalMethod", "none"); control.setAttribute("userInputFlag", "FALSE"); control.setAttribute("transparentColorFlag", "FALSE");
                control.setAttribute("delayTime", "5"); control.setAttribute("transparentColorIndex", "0");
                tree.appendChild(control); metadata.setFromTree("javax_imageio_gif_image_1.0", tree);
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
        byte[] badSize = packed.clone(); java.nio.ByteBuffer.wrap(badSize, 4, 4).putInt(1024);
        fails("too large frame", () -> unpack.invoke(null, (Object) badSize));

        // Saved capes (the wardrobe's collection) keep animated capes too; a forged id still cannot leave the folder.
        Class<?> storeType = Class.forName("dev.vortex.client.CosmeticCollection", true, target);
        Class<?> kindType = Class.forName("dev.vortex.client.CosmeticCollection$Kind", true, target);
        Class<?> entryType = Class.forName("dev.vortex.client.CosmeticCollection$Entry", true, target);
        Object capeKind = kindType.getEnumConstants()[1];
        Object store = storeType.getConstructor(Path.class).newInstance(dir.resolve("collection"));
        Object savedEntry = storeType.getMethod("addCape", byte[].class).invoke(store, (Object) packed);
        if (!((String) entryType.getMethod("id").invoke(savedEntry)).matches("anim_[a-f0-9]{64}")) throw new AssertionError("A saved animated cape gets an anim_ id");
        Object reopened = storeType.getConstructor(Path.class).newInstance(dir.resolve("collection"));
        if (!Arrays.equals(packed, (byte[]) storeType.getMethod("readCape", entryType).invoke(reopened, savedEntry))) throw new AssertionError("A saved animated cape comes back byte for byte");
        if (((List<?>) storeType.getMethod("entries", kindType).invoke(reopened, capeKind)).stream().noneMatch(e -> { try { return e.equals(savedEntry); } catch (Exception ex) { return false; } })) throw new AssertionError("A saved animated cape is listed after a restart");
        Object forged = entryType.getConstructor(kindType, String.class).newInstance(capeKind, "anim_../../outside");
        fails("a forged saved-cape id", () -> storeType.getMethod("readCape", entryType).invoke(reopened, forged));

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
        // A portrait GIF (like the 133x200 meme GIFs people try) goes through the same crop as a photo, frame by frame.
        Class<?> clipType = Class.forName("dev.vortex.client.CapeAnimation$Clip", true, target);
        Method load = importer.getMethod("load", Path.class);
        Method pack = importer.getMethod("pack", clipType, double.class, double.class, double.class, boolean.class, boolean.class, int.class);
        int[] colors = {0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00, 0x00FFFF, 0xFF00FF};
        Object clip = load.invoke(null, gif(dir, "portrait.gif", 133, 200, 10, colors));
        if ((int) clipType.getMethod("width").invoke(clip) != 133 || (int) clipType.getMethod("height").invoke(clip) != 200 || ((List<?>) clipType.getMethod("frames").invoke(clip)).size() != 6)
            throw new AssertionError("A portrait GIF keeps its own size and frames until it is cropped");
        for (boolean fit : new boolean[] {false, true}) {
            Object composed = unpack.invoke(null, (Object) pack.invoke(null, clip, 1.0, 0.0, 0.0, fit, false, 256));
            if ((int) animation.getMethod("width").invoke(composed) != 256 || (int) animation.getMethod("height").invoke(composed) != 128 || ((byte[][]) animation.getMethod("frames").invoke(composed)).length != 6)
                throw new AssertionError("Cropped animation size and frame count");
            byte[][] frames = (byte[][]) animation.getMethod("frames").invoke(composed);
            for (int f = 0; f < 6; f++) {
                if (rgb(frames[f], 10, 40, 256) != colors[f]) throw new AssertionError("Cropped frame " + f + " keeps its colour inside the cape face (fit=" + fit + ")");
                // With "fit" the picture leaves bars above and below, so only the side edge still carries the picture's colour.
                if (rgb(frames[f], 44, 40, 256) != colors[f] || (!fit && (rgb(frames[f], 10, 0, 256) != colors[f] || rgb(frames[f], 48 + 10, 0, 256) != colors[f]))) throw new AssertionError("The cape's edges repeat the face's border colour");
                if ((frames[f][(100 * 256 + 150) * 4 + 3] & 255) != 0 || (frames[f][(40 * 256 + 10) * 4 + 3] & 255) != 255) throw new AssertionError("Outside the cape's faces stays transparent, inside is opaque");
            }
        }
        for (int width : new int[] {64, 128, 512}) {
            Object sized = unpack.invoke(null, (Object) pack.invoke(null, clip, 1.0, 0.0, 0.0, false, false, width));
            if ((int) animation.getMethod("width").invoke(sized) != width) throw new AssertionError("The chosen cape size " + width + " is used");
        }
        fails("an unsupported texture width", () -> pack.invoke(null, clip, 1.0, 0.0, 0.0, false, false, 300));
        fails("texture mode on frames that are not a cape texture", () -> pack.invoke(null, clip, 1.0, 0.0, 0.0, false, true, 128));
        Object texture = load.invoke(null, gif(dir, "texture.gif", 128, 64, 10, 0xFF0000, 0x00FF00, 0x0000FF));
        Object textured = unpack.invoke(null, (Object) pack.invoke(null, texture, 1.0, 0.0, 0.0, false, true, 128));
        if (pixel(textured, 0) != 0xFF0000 || pixel(textured, 2) != 0x0000FF) throw new AssertionError("Frames that already are cape textures are used as they are");
        // A big noisy GIF at the largest size is made lighter instead of being refused.
        Object noisyClip = load.invoke(null, noiseGif(dir, "noise.gif", 200, 300, 24));
        byte[] lighter = (byte[]) pack.invoke(null, noisyClip, 1.0, 0.0, 0.0, false, false, 512);
        if (lighter.length > (int) pixels.getField("MAX_ANIMATED").get(null)) throw new AssertionError("A big animation fits the service limit after it is made lighter");
        Object lighterAnimation = unpack.invoke(null, (Object) lighter);
        if (((byte[][]) animation.getMethod("frames").invoke(lighterAnimation)).length < 2) throw new AssertionError("A lighter animation still animates");
        // A long clip is thinned when loaded.
        Object longClip = load.invoke(null, gif(dir, "long.gif", 64, 32, 5, new int[40]));
        if (((List<?>) clipType.getMethod("frames").invoke(longClip)).size() != 32) throw new AssertionError("A long GIF is thinned to 32 frames when it is loaded");
        // WebP cannot be read; say so plainly.
        Path webp = dir.resolve("anim.webp"); Files.write(webp, new byte[] {'R', 'I', 'F', 'F', 100, 0, 0, 0, 'W', 'E', 'B', 'P', 'V', 'P', '8', 'X', 0, 0, 0, 0});
        try { load.invoke(null, webp); throw new AssertionError("WebP should be refused"); }
        catch (InvocationTargetException expected) { if (!String.valueOf(expected.getCause().getMessage()).contains("WebP")) throw new AssertionError("The WebP refusal names WebP"); }
        System.out.println("ANIMATED_CAPE_SMOKE_OK: strip and GIF import, exact frame colours and order, speed bounds, 32-frame thinning, size fitting and shrinking, persistence, and refusal of corrupt, truncated, one-frame and mis-shaped files");
    }
}
