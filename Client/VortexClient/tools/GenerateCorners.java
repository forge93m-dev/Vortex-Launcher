import java.awt.*;
import java.awt.geom.Ellipse2D;
import java.awt.image.BufferedImage;
import java.nio.file.*;
import javax.imageio.ImageIO;

/** Offline antialiased corner masks for radius 1–12. Runtime is a fixed seven quads. */
public class GenerateCorners {
    public static void main(String[] args) throws Exception {
        var image = new BufferedImage(512, 384, BufferedImage.TYPE_INT_ARGB);
        var g = image.createGraphics();
        g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
        g.setColor(Color.WHITE);
        for (int radius = 1; radius <= 12; radius++) {
            int index = radius - 1, x = index % 4 * 128 + 4, y = index / 4 * 128 + 4;
            g.fill(new Ellipse2D.Double(x, y, radius * 8, radius * 8));
        }
        g.dispose();
        Path path = Path.of(args[0]); Files.createDirectories(path.getParent());
        ImageIO.write(image, "png", path.toFile());
    }
}
