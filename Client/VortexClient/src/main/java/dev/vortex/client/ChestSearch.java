package dev.vortex.client;
import java.util.BitSet;
import java.util.Locale;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.world.inventory.*;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.BlockItem;
import net.minecraft.core.component.DataComponents;
import net.minecraft.tags.ItemTags;

/** Read-only search of the open storage; never moves items or sends packets. */
public final class ChestSearch {
    public record Layout(int x,int y,int width,int height,boolean compact){}
    public static Layout layout(int left,int top,int width,int height,int screenHeight){
        if(top>=24)return new Layout(left,top-22,Math.max(40,width-48),18,false);
        if(top+height+22<=screenHeight)return new Layout(left,top+height+3,Math.max(40,width-48),18,false);
        return new Layout(left+width-84,Math.max(0,top+4),76,12,true);
    }
    public static int storageSlots(AbstractContainerMenu menu){if(menu instanceof ChestMenu chest)return chest.getRowCount()*9;if(menu instanceof ShulkerBoxMenu)return 27;return 0;}
    public static final class Query {
        private static final java.util.regex.Pattern DURABILITY=java.util.regex.Pattern.compile("(<=|>=|<|>|=)?(\\d{1,3})%?");
        private final String[] words;
        public Query(String value){String text=value==null?"":value.codePoints().limit(128).collect(StringBuilder::new,StringBuilder::appendCodePoint,StringBuilder::append).toString().strip().toLowerCase(Locale.ROOT);words=text.isEmpty()?new String[0]:text.split("\\s+");}
        public boolean active(){return words.length>0;}
        public boolean matches(String name,String id){String text=((name==null?"":name)+" "+(id==null?"":id)).toLowerCase(Locale.ROOT);for(String word:words)if(!text.contains(word))return false;return true;}
        public boolean matches(ItemStack item){
            if(item.isEmpty())return false;
            String text=(item.getHoverName().getString()+" "+BuiltInRegistries.ITEM.getKey(item.getItem())).toLowerCase(Locale.ROOT);
            for(String token:words){boolean not=token.startsWith("-") && token.length()>1;String word=not?token.substring(1):token;boolean match;
                if(word.startsWith("ench:")){String needle=word.substring(5);var enchantments=item.getOrDefault(DataComponents.ENCHANTMENTS,net.minecraft.world.item.enchantment.ItemEnchantments.EMPTY);var stored=item.getOrDefault(DataComponents.STORED_ENCHANTMENTS,net.minecraft.world.item.enchantment.ItemEnchantments.EMPTY);match=!needle.isEmpty() && java.util.stream.Stream.concat(enchantments.keySet().stream(),stored.keySet().stream()).anyMatch(h->(h.value().description().getString()+" "+h.unwrapKey().map(k->k.identifier().toString()).orElse("")).toLowerCase(Locale.ROOT).contains(needle));}
                else if(word.startsWith("dur:"))match=durability(word.substring(4),item.getMaxDamage(),item.getDamageValue());
                else if(word.startsWith("#") || word.startsWith("type:")){String type=word.startsWith("#")?word.substring(1):word.substring(5);match=switch(type){case "armor"->item.is(ItemTags.ARMOR_ENCHANTABLE);case "weapon"->item.is(ItemTags.WEAPON_ENCHANTABLE);case "tool"->item.has(DataComponents.TOOL);case "food"->item.has(DataComponents.FOOD);case "block"->item.getItem() instanceof BlockItem;case "enchanted"->item.isEnchanted() || item.has(DataComponents.STORED_ENCHANTMENTS);case "damaged"->item.isDamaged();default->false;};}
                else match=text.contains(word);
                if(match==not)return false;
            }return true;
        }
        public static boolean durability(String filter,int max,int damage){
            if(max<=0 || filter==null)return false;
            var m=DURABILITY.matcher(filter);if(!m.matches())return false;
            int threshold=Integer.parseInt(m.group(2));if(threshold>100)return false;double percent=100d*(max-Math.clamp(damage,0,max))/max;
            return switch(m.group(1)==null?"=":m.group(1)){case "<"->percent<threshold;case "<="->percent<=threshold;case ">"->percent>threshold;case ">="->percent>=threshold;default->Math.round(percent)==threshold;};
        }
    }
    public static final class State {
        private Query query=new Query("");private final BitSet matches=new BitSet();private boolean dirty=true;private int found,total;
        public void query(String value){query=new Query(value);dirty=true;}
        public boolean active(){return query.active();}public void changed(){dirty=true;}
        public int found(){return found;}public int total(){return total;}public boolean matches(int index){return matches.get(index);}
        public void refresh(AbstractContainerMenu menu){
            if(!dirty)return;dirty=false;matches.clear();found=total=0;
            for(int i=0;i<Math.min(storageSlots(menu),menu.slots.size());i++){
                var item=menu.slots.get(i).getItem();if(item.isEmpty())continue;total++;
                if(query.matches(item)){matches.set(i);found++;}
            }
        }
    }
}
