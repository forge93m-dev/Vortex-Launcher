package dev.vortex.client;

import java.util.List;
import java.util.Optional;
import net.minecraft.core.NonNullList;
import net.minecraft.core.component.DataComponents;
import net.minecraft.world.item.BlockItem;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.component.ItemContainerContents;
import net.minecraft.world.level.block.ShulkerBoxBlock;
import net.minecraft.world.inventory.tooltip.TooltipComponent;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.inventory.tooltip.ClientTooltipComponent;

/** Read-only snapshot; never opens a menu or asks the server for inventory data. */
public final class ShulkerPreview {
    public record Contents(List<ItemStack> items,boolean unknown) implements TooltipComponent {
        public Contents { items=items.stream().map(ItemStack::copy).toList(); }
    }
    private static ItemContainerContents cachedSource;
    private static Contents cached;
    public static Optional<TooltipComponent> image(ItemStack stack){
        if(!VortexClient.config.shulkerPreviewEnabled || stack.isEmpty() || !(stack.getItem() instanceof BlockItem block) || !(block.getBlock() instanceof ShulkerBoxBlock))return Optional.empty();
        if(stack.has(DataComponents.CONTAINER_LOOT))return Optional.of(new Contents(List.of(),true));
        var source=stack.getOrDefault(DataComponents.CONTAINER,ItemContainerContents.EMPTY);
        if(source!=cachedSource || cached==null){var slots=NonNullList.withSize(27,ItemStack.EMPTY);source.copyInto(slots);cached=new Contents(slots,false);cachedSource=source;}
        return Optional.of(cached);
    }
    public static final class Grid implements ClientTooltipComponent {
        private final Contents contents;
        public Grid(Contents contents){this.contents=contents;}
        public int getWidth(Font font){return 166;}
        public int getHeight(Font font){return contents.unknown()?16:66;}
        @Override public void extractImage(Font font,int x,int y,int width,int height,GuiGraphicsExtractor g){
            if(contents.unknown()){g.text(font,"Contents unavailable",x+3,y+2,0xFFABB2C1,false);return;}
            for(int i=0;i<27;i++){
                int sx=x+2+(i%9)*18,sy=y+2+(i/9)*18;
                g.fill(sx-1,sy-1,sx+17,sy+17,0xFF333540);g.fill(sx,sy,sx+16,sy+16,0xFF1A1C24);
                if(i<contents.items().size()){var item=contents.items().get(i);g.item(item,sx,sy);g.itemDecorations(font,item,sx,sy);}
            }
            if(contents.items().stream().allMatch(ItemStack::isEmpty))g.text(font,"Empty",x+3,y+55,0xFFABB2C1,false);
        }
    }
}
