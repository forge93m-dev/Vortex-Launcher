package dev.vortex.client;

import java.util.ArrayList;
import java.util.List;
import com.mojang.blaze3d.platform.InputConstants;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;

/**
 * Picking real blocks for a waypoint and outlining them. Picking only reads the block under the crosshair:
 * it never breaks, places, interacts, scans chunks or sends packets. Only Save in the editor writes anything.
 */
public final class WaypointBlocks {
    private WaypointBlocks() { }
    public static final int MAXIMUM = 256;
    /** Outlines further than this are skipped; the pin itself has no distance limit. */
    static final double OUTLINE_RANGE = 96;
    private static WaypointEditScreen picking;
    private static List<WaypointsStore.Block> before = List.of();
    private static boolean toggleDown, finishDown;

    /** Adds the block, or removes it when already selected; a full selection ignores new blocks. */
    public static List<WaypointsStore.Block> toggle(List<WaypointsStore.Block> current, WaypointsStore.Block block) {
        var result = new ArrayList<>(current);
        if (!result.remove(block) && result.size() < MAXIMUM && WaypointsStore.validBlock(block)) result.add(block);
        return List.copyOf(result);
    }
    /** Top centre of a selection: the average of the block centres, standing on its highest block. */
    public static double[] center(List<WaypointsStore.Block> blocks) {
        double x = 0, z = 0; int top = Integer.MIN_VALUE;
        for (var b : blocks) { x += b.x() + .5; z += b.z() + .5; top = Math.max(top, b.y()); }
        return new double[] {x / blocks.size(), top + 1, z / blocks.size()};
    }
    /** Whole-block XYZ saved with the marker (shown in lists and used for distances): the block under the centre. */
    public static int[] anchor(List<WaypointsStore.Block> blocks) {
        double[] c = center(blocks);
        return new int[] {(int)Math.floor(c[0]), (int)c[1] - 1, (int)Math.floor(c[2])};
    }
    public static boolean picking() { return picking != null; }
    static void start(WaypointEditScreen editor) {
        picking = editor; before = editor.blocks(); editor.rememberPosition(); toggleDown = finishDown = true;
        Minecraft.getInstance().gui.setScreen(null);
    }
    static void cancel() { picking = null; }
    private static boolean down(int key) { return InputConstants.isKeyDown(key); }
    /** H toggles the aimed block; Enter or the waypoints key returns to the editor. */
    static void tick(Minecraft mc) {
        if (picking == null) return;
        if (mc.level == null || mc.player == null) { picking = null; return; }
        if (mc.gui.screen() != null) {
            // Esc (or any other menu) cancels this picking session: back to the editor with the earlier selection.
            var editor = picking; picking = null; editor.restoreBlocks(before); mc.gui.setScreen(editor); return;
        }
        boolean toggle = mc.isWindowActive() && down(InputConstants.KEY_H);
        boolean finish = mc.isWindowActive() && (down(InputConstants.KEY_RETURN) || ClientBindings.down(ClientBindings.Action.WAYPOINTS));
        if (toggle && !toggleDown && mc.hitResult instanceof net.minecraft.world.phys.BlockHitResult hit
            && hit.getType() == net.minecraft.world.phys.HitResult.Type.BLOCK) {
            var pos = hit.getBlockPos();
            picking.toggleBlock(new WaypointsStore.Block(pos.getX(), pos.getY(), pos.getZ()));
        }
        if (finish && !finishDown) { var editor = picking; picking = null; mc.gui.setScreen(editor); }
        toggleDown = toggle; finishDown = finish;
    }
    /** Hint pill at the top while picking. */
    static void drawHint(GuiGraphicsExtractor g) {
        if (picking == null) return;
        var font = UiFonts.body();
        String text = picking.blocks().size() + " blocks selected  ·  H select / unselect  ·  Enter done";
        int w = font.width(text) + 24, x = (g.guiWidth() - w) / 2;
        ClientUi.pill(g, x, 8, w, 18, 0xE010151C);
        ClientUi.rounded(g, x + 7, 14, 6, 6, 3, ClientUi.ACCENT);
        g.text(font, text, x + 17, 13, ClientUi.TEXT, false);
    }
    /** Blocks being picked right now, drawn in the accent colour. */
    static List<WaypointsStore.Block> draft() { return picking == null ? List.of() : picking.blocks(); }

    /**
     * Edges of the selection's outer shape: an edge is drawn only where the cells around it are not all
     * on the same side, so neighbouring blocks merge into one outline without lines inside it.
     * Each edge is {axis, x, y, z} of its start corner; it runs one block along the axis.
     */
    private static final java.util.Map<List<WaypointsStore.Block>, List<int[]>> EDGE_CACHE = new java.util.IdentityHashMap<>();
    /** Saved selections never change, so their edges are computed once per list. */
    static List<int[]> cachedEdges(List<WaypointsStore.Block> blocks) {
        var edges = EDGE_CACHE.get(blocks);
        if (edges == null) {
            if (EDGE_CACHE.size() > 256) EDGE_CACHE.clear();
            EDGE_CACHE.put(blocks, edges = silhouetteEdges(blocks));
        }
        return edges;
    }
    public static List<int[]> silhouetteEdges(java.util.Collection<WaypointsStore.Block> blocks) {
        var set = new java.util.HashSet<>(blocks);
        var seen = new java.util.HashSet<List<Integer>>();
        var edges = new ArrayList<int[]>();
        for (var b : set) {
            int[] base = {b.x(), b.y(), b.z()};
            for (int axis = 0; axis < 3; axis++) {
                int p = (axis + 1) % 3, q = (axis + 2) % 3;
                for (int u = 0; u < 2; u++) for (int v = 0; v < 2; v++) {
                    int[] start = base.clone(); start[p] += u; start[q] += v;
                    if (!seen.add(List.of(axis, start[0], start[1], start[2]))) continue;
                    boolean[] filled = new boolean[4];
                    int count = 0;
                    for (int i = 0; i < 4; i++) {
                        int[] cell = start.clone(); cell[p] -= 1 - (i & 1); cell[q] -= 1 - (i >> 1);
                        filled[i] = set.contains(new WaypointsStore.Block(cell[0], cell[1], cell[2]));
                        if (filled[i]) count++;
                    }
                    boolean diagonal = count == 2 && filled[0] == filled[3];
                    if (count % 2 == 1 || diagonal) edges.add(new int[] {axis, start[0], start[1], start[2]});
                }
            }
        }
        return edges;
    }
    /** Outer faces only ({direction 0..5 = +x,-x,+y,-y,+z,-z, x, y, z}); faces between two selected blocks are hidden. */
    public static List<int[]> silhouetteFaces(java.util.Collection<WaypointsStore.Block> blocks) {
        var set = new java.util.HashSet<>(blocks);
        var faces = new ArrayList<int[]>();
        int[][] steps = {{1,0,0},{-1,0,0},{0,1,0},{0,-1,0},{0,0,1},{0,0,-1}};
        for (var b : set) for (int d = 0; d < 6; d++)
            if (!set.contains(new WaypointsStore.Block(b.x() + steps[d][0], b.y() + steps[d][1], b.z() + steps[d][2])))
                faces.add(new int[] {d, b.x(), b.y(), b.z()});
        return faces;
    }
    private static final java.util.Map<List<WaypointsStore.Block>, List<int[]>> FACE_CACHE = new java.util.IdentityHashMap<>();
    static List<int[]> cachedFaces(List<WaypointsStore.Block> blocks) {
        var faces = FACE_CACHE.get(blocks);
        if (faces == null) {
            if (FACE_CACHE.size() > 256) FACE_CACHE.clear();
            FACE_CACHE.put(blocks, faces = silhouetteFaces(blocks));
        }
        return faces;
    }
    /** Fill alpha of highlighted faces; the outline stays nearly opaque on top. */
    static final int FACE_ALPHA = 0x40;
    private static final net.minecraft.core.Direction[] FACES = {
        net.minecraft.core.Direction.EAST, net.minecraft.core.Direction.WEST, net.minecraft.core.Direction.UP,
        net.minecraft.core.Direction.DOWN, net.minecraft.core.Direction.SOUTH, net.minecraft.core.Direction.NORTH};
    /**
     * Hands the highlight to Minecraft's own world gizmos once per client tick (it runs inside the game's per-tick
     * gizmo collection). The game draws them in 3D on the GPU, see-through, so a selection filling the whole screen
     * costs a few quads instead of thousands of GUI rows.
     */
    static void emit(Minecraft mc) {
        if (mc.level == null || mc.player == null) return;
        var eye = mc.player.getEyePosition();
        if (VortexClient.config.waypointsEnabled)
            for (var point : WaypointsFeature.visiblePoints())
                if (!point.blocks().isEmpty()) emit(point.blocks(), point.color(), eye, OUTLINE_RANGE * OUTLINE_RANGE);
        // Places shared by the party light up their blocks too, in the sharer's chosen colour.
        for (var point : PartyFeature.worldPoints())
            if (!point.blocks().isEmpty()) emit(point.blocks(), point.color(), eye, OUTLINE_RANGE * OUTLINE_RANGE);
        var draft = draft();
        if (!draft.isEmpty()) emit(draft, ClientUi.ACCENT, eye, Double.MAX_VALUE);
    }
    private static void emit(List<WaypointsStore.Block> blocks, int rgb, net.minecraft.world.phys.Vec3 eye, double range) {
        int fill = FACE_ALPHA << 24 | rgb & 0xFFFFFF, line = 0xE0000000 | rgb & 0xFFFFFF;
        var style = net.minecraft.gizmos.GizmoStyle.fill(fill);
        for (int[] face : cachedFaces(blocks)) {
            if (eye.distanceToSqr(face[1] + .5, face[2] + .5, face[3] + .5) > range) continue;
            var min = new net.minecraft.world.phys.Vec3(face[1], face[2], face[3]);
            net.minecraft.gizmos.Gizmos.rect(min, min.add(1, 1, 1), FACES[face[0]], style).setAlwaysOnTop();
        }
        for (int[] edge : cachedEdges(blocks)) {
            if (eye.distanceToSqr(edge[1], edge[2], edge[3]) > range) continue;
            var start = new net.minecraft.world.phys.Vec3(edge[1], edge[2], edge[3]);
            var end = start.add(edge[0] == 0 ? 1 : 0, edge[0] == 1 ? 1 : 0, edge[0] == 2 ? 1 : 0);
            net.minecraft.gizmos.Gizmos.line(start, end, line, 3f).setAlwaysOnTop();
        }
    }
}
