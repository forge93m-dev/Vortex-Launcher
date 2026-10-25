package dev.vortex.client;

import net.minecraft.network.chat.Component;

/** Read-only view into the chat box, implemented by ChatComponentMixin. */
public interface ChatAccess {
    /** The full message shown on a visible row (0 = bottom row, counting the current scroll), or null. */
    Component vortex$messageAtRow(int row);
    /** How many stored messages pass the current tab and search. */
    int vortex$matchingCount();
}
