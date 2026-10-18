# White in-game mark

Final master: `Assets/Branding/vortex-ingame-white-v2.png` (1254×1254 RGBA).
Runtime copy: `Client/VortexClient/src/main/resources/assets/vortex_client/textures/ui/icons/brand.png`.

The earlier code-native flat-white adaptation was rejected by the user because the folds merged and the mark appeared thicker. It has been replaced by a built-in image edit of the original purple master: white/silver highlights and gray fold shading, transparent exterior and center hole, no added outlines or separation strokes. This is not a pixel-exact conversion; the generated output was visually compared with the master. The desktop PNG/ICO/ICNS remain unchanged.

Regenerate with the installed JDK: `java Client/VortexClient/tools/GenerateIcons.java Client/VortexClient/src/main/resources/assets/vortex_client/textures/ui/icons brand`.

Built-in edit prompt (final): "Color change only, not a redesign. Convert the violet/lavender surfaces to a white/silver monochrome material, retaining the original smooth grayscale shading so every folded ribbon remains distinct. Bright highlights white, midtones pale silver, deep folds charcoal gray. Preserve the existing silhouette, ribbon widths, negative spaces, pose, proportions and padding. Do not inflate, thicken, flatten, simplify or trace into outlines. Preserve genuine transparency. No extra decorations or text."
