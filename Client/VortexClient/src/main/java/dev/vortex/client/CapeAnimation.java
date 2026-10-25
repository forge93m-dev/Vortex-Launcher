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
    /** Decoded frames as RGBA, all the same size. */
    record Frames(int width, int height, int frameMs, List<byte[]> rgba) { }
    private CapeAnimation() { }

    public static boolean isGif(byte[] head) {
        return head != null && head.length >= 6 && head[0] == 'G' && head[1] == 'I' && head[2] == 'F' && head[3] == '8';
    }
    public static boolean isGif(Path path) {
        try (var input = Files.newInputStream(path)) { return isGif(input.readNBytes(8)); } catch (Exception unreadable) { return false; }
    }

    public static byte[] read(Path path) throws Exception {
        if (Files.size(path) > MAX_FILE) throw new IllegalArgumentException("Choose a GIF or PNG strip up to 8 MB.");
        return pack(decode(Files.readAllBytes(path)));
    }

    static Frames decode(byte[] bytes) throws Exception {
        if (bytes.length > MAX_FILE) throw new IllegalArgumentException("Choose a GIF or PNG strip up to 8 MB.");
        return isGif(bytes) ? gif(bytes) : strip(bytes);
    }

    /** A PNG whose height is a whole number of frames, each width x width/2. */
    static Frames strip(byte[] png) throws Exception {
        var source = CapeArtwork.decode(png);
        int frameHeight = source.width() / 2;
        if (frameHeight < 1 || source.width() % 2 != 0 || source.height() % frameHeight != 0 || source.height() / frameHeight < 2)
            throw new IllegalArgumentException("A strip needs at least two frames stacked top to bottom, each twice as wide as tall (for example 128x64 per frame, so 128x256 for four frames).");
        int count = source.height() / frameHeight, frameSize = source.width() * frameHeight * 4;
        var frames = new ArrayList<byte[]>();
        for (int i = 0; i < count; i++) frames.add(java.util.Arrays.copyOfRange(source.rgba(), i * frameSize, (i + 1) * frameSize));
        return new Frames(source.width(), frameHeight, 100, frames);
    }

    static Frames gif(byte[] bytes) throws Exception {
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
                return new Frames(canvasWidth, canvasHeight, (int) Math.round(delay * 10.0 / count), frames);
            } finally { reader.dispose(); }
        }
    }

    /** Fits frames to a supported size, thins out very long animations and packs them under the service limit. */
    static byte[] pack(Frames source) throws Exception {
        List<byte[]> frames = new ArrayList<>(source.rgba());
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
