package dev.vortex.client;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;

/**
 * Turns a GIF or a PNG strip (frames stacked top to bottom, each frame in the cape texture layout, twice as wide as
 * tall) into the bounded animated-cape format. Everything is decoded locally; only the packed pixels are ever uploaded.
 */
public final class CapeAnimation {
    private static final int MAX_FILE = 8 * 1024 * 1024;
    private static final int MAX_GIF_SIZE = 2048;
    private static final long MAX_TOTAL_PIXELS = 16L * 1024 * 1024;
    /** Decoded frames as RGBA, all the same size. Public so the crop screen can show and re-compose them. */
    public record Clip(int width, int height, int frameMs, List<byte[]> frames) { }
    static final int MAX_CLIP_FRAMES = 32;
    private CapeAnimation() { }

    public static boolean isGif(byte[] head) {
        return head != null && head.length >= 6 && head[0] == 'G' && head[1] == 'I' && head[2] == 'F' && head[3] == '8';
    }
    public static boolean isWebp(byte[] head) {
        return head != null && head.length >= 12 && head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F' && head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P';
    }
    public static boolean isWebp(Path path) {
        try (var input = Files.newInputStream(path)) { return isWebp(input.readNBytes(16)); } catch (Exception unreadable) { return false; }
    }
    public static boolean isGif(Path path) {
        try (var input = Files.newInputStream(path)) { return isGif(input.readNBytes(8)); } catch (Exception unreadable) { return false; }
    }

    public static byte[] read(Path path) throws Exception {
        if (Files.size(path) > MAX_FILE) throw new IllegalArgumentException("Choose a GIF or PNG strip up to 8 MB.");
        return pack(decode(Files.readAllBytes(path)));
    }

    /** A GIF or PNG strip for the crop screen, thinned to at most 32 frames while keeping its duration. */
    public static Clip load(Path path) throws Exception {
        if (Files.size(path) > MAX_FILE) throw new IllegalArgumentException("Choose a GIF or PNG strip up to 8 MB.");
        var clip = decode(Files.readAllBytes(path));
        if (clip.frames().size() < 2) throw new IllegalArgumentException("An animated cape needs at least two frames.");
        return thin(clip, MAX_CLIP_FRAMES);
    }

    static Clip thin(Clip clip, int limit) {
        int total = clip.frames().size();
        if (total <= limit) return clip;
        var kept = new ArrayList<byte[]>();
        for (int i = 0; i < limit; i++) kept.add(clip.frames().get((int) ((long) i * total / limit)));
        return new Clip(clip.width(), clip.height(), Math.clamp((int) Math.round(clip.frameMs() * (double) total / limit), 50, 1000), kept);
    }

    /** Frames that already are cape textures (twice as wide as tall, a supported size) need no crop. */
    public static boolean isTexture(Clip clip) {
        return clip.height() * 2 == clip.width() && (clip.width() == 64 || clip.width() == 128 || clip.width() == 256 || clip.width() == 512);
    }

    static Clip decode(byte[] bytes) throws Exception {
        if (bytes.length > MAX_FILE) throw new IllegalArgumentException("Choose a GIF or PNG strip up to 8 MB.");
        if (isWebp(bytes)) throw new IllegalArgumentException("WebP files cannot be read yet. Save or convert the animation as a GIF first.");
        return isGif(bytes) ? gif(bytes) : strip(bytes);
    }

    /** A PNG whose height is a whole number of frames, each width x width/2. */
    static Clip strip(byte[] png) throws Exception {
        var source = CapeArtwork.decode(png);
        int frameHeight = source.width() / 2;
        if (frameHeight < 1 || source.width() % 2 != 0 || source.height() % frameHeight != 0 || source.height() / frameHeight < 2)
            throw new IllegalArgumentException("A strip needs at least two frames stacked top to bottom, each twice as wide as tall (for example 128x64 per frame, so 128x256 for four frames).");
        int count = source.height() / frameHeight, frameSize = source.width() * frameHeight * 4;
        var frames = new ArrayList<byte[]>();
        for (int i = 0; i < count; i++) frames.add(java.util.Arrays.copyOfRange(source.rgba(), i * frameSize, (i + 1) * frameSize));
        return new Clip(source.width(), frameHeight, 100, frames);
    }

    static Clip gif(byte[] bytes) throws Exception {
        try (var input = new javax.imageio.stream.MemoryCacheImageInputStream(new java.io.ByteArrayInputStream(bytes))) {
            var readers = javax.imageio.ImageIO.getImageReadersByFormatName("gif");
            if (!readers.hasNext()) throw new IllegalArgumentException("This computer cannot read GIF files.");
            var reader = readers.next();
            try {
                reader.setInput(input, false, false);
                int count = reader.getNumImages(true);
                int canvasWidth = 0, canvasHeight = 0;
                try {
                    var stream = reader.getStreamMetadata().getAsTree("javax_imageio_gif_stream_1.0");
                    for (var node = stream.getFirstChild(); node != null; node = node.getNextSibling())
                        if ("LogicalScreenDescriptor".equals(node.getNodeName())) {
                            canvasWidth = Integer.parseInt(node.getAttributes().getNamedItem("logicalScreenWidth").getNodeValue());
                            canvasHeight = Integer.parseInt(node.getAttributes().getNamedItem("logicalScreenHeight").getNodeValue());
                        }
                } catch (RuntimeException noScreen) { /* fall back to the first frame's size */ }
                if (canvasWidth < 1 || canvasHeight < 1) { canvasWidth = reader.getWidth(0); canvasHeight = reader.getHeight(0); }
                if (count < 2) throw new IllegalArgumentException("The GIF has only one frame.");
                if (canvasWidth > MAX_GIF_SIZE || canvasHeight > MAX_GIF_SIZE || (long) canvasWidth * canvasHeight * count > MAX_TOTAL_PIXELS)
                    throw new IllegalArgumentException("The GIF is too large. Use a smaller size or fewer frames.");
                int[] canvas = new int[canvasWidth * canvasHeight];
                var frames = new ArrayList<byte[]>();
                long delay = 0;
                for (int i = 0; i < count; i++) {
                    var image = reader.read(i);
                    int left = 0, top = 0, disposal = 0, centis = 10;
                    var tree = reader.getImageMetadata(i).getAsTree("javax_imageio_gif_image_1.0");
                    for (var node = tree.getFirstChild(); node != null; node = node.getNextSibling()) {
                        var attributes = node.getAttributes();
                        if ("ImageDescriptor".equals(node.getNodeName())) {
                            left = Integer.parseInt(attributes.getNamedItem("imageLeftPosition").getNodeValue());
                            top = Integer.parseInt(attributes.getNamedItem("imageTopPosition").getNodeValue());
                        } else if ("GraphicControlExtension".equals(node.getNodeName())) {
                            String method = attributes.getNamedItem("disposalMethod").getNodeValue();
                            disposal = "restoreToBackgroundColor".equals(method) ? 2 : "restoreToPrevious".equals(method) ? 3 : 0;
                            centis = Integer.parseInt(attributes.getNamedItem("delayTime").getNodeValue());
                        }
                    }
                    delay += centis <= 0 ? 10 : centis;
                    int[] before = disposal == 3 ? canvas.clone() : null;
                    int width = image.getWidth(), height = image.getHeight();
                    for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) {
                        int cx = left + x, cy = top + y;
                        if (cx < 0 || cy < 0 || cx >= canvasWidth || cy >= canvasHeight) continue;
                        int argb = image.getRGB(x, y);
                        if ((argb >>> 24) != 0) canvas[cy * canvasWidth + cx] = argb;
                    }
                    byte[] rgba = new byte[canvasWidth * canvasHeight * 4];
                    for (int p = 0; p < canvas.length; p++) {
                        int c = canvas[p];
                        rgba[p * 4] = (byte) (c >> 16); rgba[p * 4 + 1] = (byte) (c >> 8); rgba[p * 4 + 2] = (byte) c; rgba[p * 4 + 3] = (byte) (c >>> 24);
                    }
                    frames.add(rgba);
                    if (disposal == 2) {
                        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) {
                            int cx = left + x, cy = top + y;
                            if (cx >= 0 && cy >= 0 && cx < canvasWidth && cy < canvasHeight) canvas[cy * canvasWidth + cx] = 0;
                        }
                    } else if (disposal == 3 && before != null) canvas = before;
                    image.flush();
                }
                return new Clip(canvasWidth, canvasHeight, (int) Math.round(delay * 10.0 / count), frames);
            } finally { reader.dispose(); }
        }
    }

    /** Fits frames to a supported size, thins out very long animations and packs them under the service limit. */
    static byte[] pack(Clip source) throws Exception {
        List<byte[]> frames = new ArrayList<>(source.frames());
        int frameMs = source.frameMs();
        if (frames.size() < 2) throw new IllegalArgumentException("An animated cape needs at least two frames.");
        if (frames.size() > CapePixels.MAX_FRAMES) {
            // Keep the overall duration: fewer frames, each shown longer.
            int total = frames.size();
            var kept = new ArrayList<byte[]>();
            for (int i = 0; i < CapePixels.MAX_FRAMES; i++) kept.add(frames.get((int) ((long) i * total / CapePixels.MAX_FRAMES)));
            frameMs = (int) Math.round(frameMs * (double) total / CapePixels.MAX_FRAMES);
            frames = kept;
        }
        frameMs = Math.clamp(frameMs, 50, 1000);
        int width = source.width(), height = source.height();
        if (source.height() * 2 != source.width())
            throw new IllegalArgumentException("Each frame must be twice as wide as tall, like a cape texture (for example 128x64).");
        if (!(width == 64 || width == 128 || width == 256)) {
            int target = width > 256 ? 256 : width > 128 ? 256 : 128;
            if (width < 64) target = 64;
            frames = resize(frames, width, height, target, target / 2);
            width = target; height = target / 2;
        }
        for (;;) {
            byte[] packed = CapePixels.packAnimation(width, height, frameMs, frames);
            if (packed.length <= CapePixels.MAX_ANIMATED) return packed;
            if (width <= 64) throw new IllegalArgumentException("The animation is too large even at the smallest size. Use fewer frames.");
            frames = resize(frames, width, height, width / 2, height / 2);
            width /= 2; height /= 2;
        }
    }

    /**
     * Builds the animated cape from a clip the way the photo import builds a static one: each frame is cropped with the same
     * zoom, pan and fit, laid out as a cape texture of the chosen width (64, 128, 256 or 512), and packed. When the result is
     * over the service limit it is made lighter: every other frame is dropped (the speed is halved to keep the duration), and
     * once only a few frames are left the texture gets smaller.
     */
    public static byte[] pack(Clip clip, double zoom, double panX, double panY, boolean fit, boolean textureMode, int width) throws Exception {
        if (!(width == 64 || width == 128 || width == 256 || width == 512)) throw new IllegalArgumentException("Choose a texture width of 64, 128, 256 or 512.");
        if (textureMode && !isTexture(clip)) throw new IllegalArgumentException("These frames are not in the cape texture layout.");
        var frames = new ArrayList<>(clip.frames());
        if (frames.size() < 2) throw new IllegalArgumentException("An animated cape needs at least two frames.");
        int ms = clip.frameMs();
        for (;;)
        {
            var textures = new ArrayList<byte[]>();
            for (byte[] frame : frames)
                textures.add(textureMode ? resize(List.of(frame), clip.width(), clip.height(), width, width / 2).get(0)
                    : texture(new CapeArtwork.Source(clip.width(), clip.height(), frame), zoom, panX, panY, fit, width));
            byte[] packed = CapePixels.packAnimation(width, width / 2, Math.clamp(ms, 50, 1000), textures);
            if (packed.length <= CapePixels.MAX_ANIMATED) return packed;
            if (frames.size() > 8)
            {
                var half = new ArrayList<byte[]>();
                for (int i = 0; i < frames.size(); i += 2) half.add(frames.get(i));
                frames = half; ms *= 2;
            }
            else if (width > 64) width /= 2;
            else throw new IllegalArgumentException("The animation is too large even at the smallest size. Use fewer frames.");
        }
    }

    /** One frame as a cape texture: the photo cropped to the cape's face (10x16 units) and laid out with its edges, like the static import. */
    static byte[] texture(CapeArtwork.Source source, double zoom, double panX, double panY, boolean fit, int width) {
        int s = width / 64, faceWidth = 10 * s, faceHeight = 16 * s, block = 16 / s;
        byte[] big = CapeArtwork.face(source, 160, 256, zoom, panX, panY, fit), face = new byte[faceWidth * faceHeight * 4];
        for (int y = 0; y < faceHeight; y++) for (int x = 0; x < faceWidth; x++)
        {
            int[] sum = new int[4];
            for (int dy = 0; dy < block; dy++) for (int dx = 0; dx < block; dx++)
            {
                int p = ((y * block + dy) * 160 + x * block + dx) * 4;
                for (int c = 0; c < 4; c++) sum[c] += big[p + c] & 255;
            }
            for (int c = 0; c < 4; c++) face[(y * faceWidth + x) * 4 + c] = (byte) Math.round(sum[c] / (double) (block * block));
        }
        int atlasWidth = 64 * s;
        byte[] atlas = new byte[atlasWidth * 32 * s * 4];
        for (int y = 0; y < faceHeight; y++)
        {
            System.arraycopy(face, y * faceWidth * 4, atlas, ((y + s) * atlasWidth + s) * 4, faceWidth * 4);
            System.arraycopy(face, y * faceWidth * 4, atlas, ((y + s) * atlasWidth + 12 * s) * 4, faceWidth * 4);
            for (int x = 0; x < s; x++)
            {
                System.arraycopy(face, y * faceWidth * 4, atlas, ((y + s) * atlasWidth + x) * 4, 4);
                System.arraycopy(face, (y * faceWidth + faceWidth - 1) * 4, atlas, ((y + s) * atlasWidth + 11 * s + x) * 4, 4);
            }
        }
        for (int y = 0; y < s; y++) for (int x = 0; x < faceWidth; x++)
        {
            System.arraycopy(face, x * 4, atlas, (y * atlasWidth + s + x) * 4, 4);
            System.arraycopy(face, ((faceHeight - 1) * faceWidth + x) * 4, atlas, (y * atlasWidth + 11 * s + x) * 4, 4);
        }
        return atlas;
    }

    static List<byte[]> resize(List<byte[]> frames, int sourceWidth, int sourceHeight, int width, int height) {
        var result = new ArrayList<byte[]>();
        for (byte[] frame : frames) {
            byte[] scaled = new byte[width * height * 4];
            for (int y = 0; y < height; y++) {
                int sy = Math.min(sourceHeight - 1, (int) ((y + 0.5) * sourceHeight / height));
                for (int x = 0; x < width; x++) {
                    int sx = Math.min(sourceWidth - 1, (int) ((x + 0.5) * sourceWidth / width));
                    System.arraycopy(frame, (sy * sourceWidth + sx) * 4, scaled, (y * width + x) * 4, 4);
                }
            }
            result.add(scaled);
        }
        return result;
    }
}
