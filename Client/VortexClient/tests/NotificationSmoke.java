import java.util.*;

public final class NotificationSmoke {
    public static void run(ClassLoader loader) throws Exception {
        Class<?> queueType=Class.forName("dev.vortex.client.NotificationQueue",true,loader);
        Class<?> kind=Class.forName("dev.vortex.client.NotificationQueue$Kind",true,loader);
        Object success=kind.getEnumConstants()[0],info=kind.getEnumConstants()[1],error=kind.getEnumConstants()[2];
        var offer=queueType.getMethod("offer",String.class,String.class,String.class,kind,long.class);
        var advance=queueType.getMethod("advance",long.class,int.class,boolean.class);var size=queueType.getMethod("size");
        Object queue=queueType.getConstructor().newInstance();
        offer.invoke(queue,"save","Сохранено","Текст уведомления",success,0L);
        if((boolean)offer.invoke(queue,"save","Повтор","Не должен добавляться",success,1L))throw new AssertionError("Duplicate notice accepted");
        List<?> active=(List<?>)advance.invoke(queue,0L,3,true);Object notice=active.getFirst();Class<?> noticeType=notice.getClass();
        if((float)noticeType.getMethod("visibility",boolean.class).invoke(notice,false)!=1 || (float)noticeType.getMethod("visibility",boolean.class).invoke(notice,true)!=0)
            throw new AssertionError("Animation toggle / initial visibility");
        advance.invoke(queue,100L,3,true);double age=(double)noticeType.getMethod("age").invoke(notice);
        if((float)noticeType.getMethod("visibility",boolean.class).invoke(notice,true)<=0)throw new AssertionError("Notification does not animate in");
        for(long now=200;now<10000;now+=200)advance.invoke(queue,now,0,false);
        if((double)noticeType.getMethod("age").invoke(notice)!=age)throw new AssertionError("Hidden notification expires unseen");
        for(long now=10000;now<14000;now+=200)advance.invoke(queue,now,3,true);
        if((int)size.invoke(queue)!=0)throw new AssertionError("Success notice never expires");
        offer.invoke(queue,"error","Ошибка","Попробуй ещё раз",error,14000L);advance.invoke(queue,14000L,3,true);
        for(long now=14200;now<18200;now+=200)advance.invoke(queue,now,3,true);
        if((int)size.invoke(queue)!=1)throw new AssertionError("Error disappears too soon");
        queueType.getMethod("dismiss",String.class).invoke(queue,"error");
        if((int)size.invoke(queue)!=0)throw new AssertionError("Resolved error cannot be dismissed");
        queueType.getMethod("clear").invoke(queue);
        for(int i=0;i<100;i++)offer.invoke(queue,"info"+i,"Title","Body",info,(long)i);
        if((int)size.invoke(queue)!=8)throw new AssertionError("Pending queue not bounded");
        if(!(boolean)offer.invoke(queue,"urgent","Failure","Must be retained",error,1000L))throw new AssertionError("Full queue drops explicit errors");
        active=(List<?>)advance.invoke(queue,1000L,3,true);
        if(active.size()!=3 || (int)size.invoke(queue)>11)throw new AssertionError("Too many active notifications");
        queueType.getMethod("clear").invoke(queue);
        offer.invoke(queue,"short","A","B",success,0L);advance.invoke(queue,0L,1,true);
        for(long now=250;now<=3500;now+=250)advance.invoke(queue,now,1,true);
        if((boolean)offer.invoke(queue,"short","A","B",success,100L))throw new AssertionError("Dedup cooldown ignored");
        String clean=(String)queueType.getMethod("clean",String.class,int.class).invoke(null,"A\n§B"+"😀".repeat(100),12);
        if(clean.contains("\n") || clean.contains("§") || clean.codePointCount(0,clean.length())!=12)throw new AssertionError("Unbounded / formatted notification text");
        verifyLayout(loader);verifyPublication(loader);
        Class<?> gui=Class.forName("net.minecraft.client.gui.Gui",true,loader);
        if(Arrays.stream(gui.getDeclaredMethods()).noneMatch(m->m.getName().contains("vortex$notifications")))throw new AssertionError("Global notification draw hook was not applied");
        System.out.println("PASS Notification queue caps/priority/dedup, error duration, hidden timer suspension, dismissal, reduced motion, HUD-safe layouts and real global GUI hook");
    }
    private static void verifyLayout(ClassLoader loader) throws Exception {
        Class<?> overlay=Class.forName("dev.vortex.client.ClientNotifications",true,loader);
        Class<?> bounds=Class.forName("dev.vortex.client.HudModules$Bounds",true,loader);
        var layout=overlay.getMethod("layout",int.class,int.class,boolean.class,List.class);
        for(int[] dims:new int[][]{{320,240},{430,300},{854,480},{1920,1080}}){
            int width=dims[0],height=dims[1];Object armor=bounds.getConstructor(int.class,int.class,int.class,int.class).newInstance(width-84,height-110,76,102);
            var cards=(List<?>)layout.invoke(null,width,height,true,List.of(armor));
            if(cards.isEmpty() || cards.size()>3)throw new AssertionError("Notification layout has no slots / too many cards");
            int previousY=height;
            for(Object card:cards){Class<?> type=card.getClass();int x=(int)type.getMethod("x").invoke(card),y=(int)type.getMethod("y").invoke(card),w=(int)type.getMethod("width").invoke(card),h=(int)type.getMethod("height").invoke(card);
                if(x<8 || y<8 || x+w>width-8 || y+h>height-64 || y+h>previousY || x<width && x+w>width-84 && y<height-8 && y+h>height-110)
                    throw new AssertionError("Notification covers armor/hotbar or another card");previousY=y-6;
            }
        }
        Object blocking=bounds.getConstructor(int.class,int.class,int.class,int.class).newInstance(0,0,320,240);
        if(!((List<?>)layout.invoke(null,320,240,true,List.of(blocking))).isEmpty())throw new AssertionError("No-space HUD must defer notifications instead of covering overlays");
    }
    private static void verifyPublication(ClassLoader loader) throws Exception {
        Class<?> type=Class.forName("dev.vortex.client.CapePublication",true,loader);
        Class<?> request=Class.forName("dev.vortex.client.CapePublication$Request",true,loader);
        Object model=type.getConstructor().newInstance();UUID first=UUID.randomUUID(),other=UUID.randomUUID();
        var status=type.getMethod("status",UUID.class,String.class,boolean.class,boolean.class,long.class);
        var begin=type.getMethod("begin",UUID.class,String.class);var complete=type.getMethod("complete",request,boolean.class,long.class);
        var observe=type.getMethod("observe",UUID.class,String.class,long.class);
        state(status.invoke(model,first,"vortex",true,false,0L),"UNKNOWN",false);
        observe.invoke(model,first,"ember",100L);state(status.invoke(model,first,"vortex",true,false,101L),"LOCAL",false);
        Object old=begin.invoke(model,first,"vortex"),current=begin.invoke(model,first,"aurora");
        if((boolean)complete.invoke(model,old,true,200L))throw new AssertionError("Stale publication callback accepted");
        observe.invoke(model,first,"ember",201L);state(status.invoke(model,first,"aurora",true,false,202L),"UPLOADING",false);
        complete.invoke(model,current,false,203L);state(status.invoke(model,first,"aurora",true,false,204L),"UNCONFIRMED",true);
        Object retry=begin.invoke(model,first,"aurora");complete.invoke(model,retry,true,205L);
        state(status.invoke(model,first,"aurora",true,false,206L),"PUBLISHED",false);
        state(status.invoke(model,first,"vortex",true,false,207L),"LOCAL",false);
        state(status.invoke(model,other,"aurora",true,false,208L),"UNKNOWN",false);
        state(status.invoke(model,first,"aurora",false,false,209L),"HIDDEN",false);
        state(status.invoke(model,first,"vortex",true,true,210L),"PREVIEW",false);
        state(status.invoke(model,first,"aurora",true,false,301000L),"UNKNOWN",false);
        Object pending=begin.invoke(model,first,"vortex");type.getMethod("cancelPending").invoke(model);
        if((boolean)complete.invoke(model,pending,true,302000L))throw new AssertionError("World/profile cancellation accepts late publication acknowledgement");
        System.out.println("PASS Cape publication evidence: no false local-save success, server ACK/lookup, timeout uncertainty, retry, stale callback/lookup, profile isolation, disabled/preview and expiry states");
    }
    private static void state(Object value,String expected,boolean retry) throws Exception {
        if(!expected.equals(value.getClass().getMethod("state").invoke(value).toString()) || (boolean)value.getClass().getMethod("retry").invoke(value)!=retry)
            throw new AssertionError("Incorrect cape status: "+expected);
    }
}
