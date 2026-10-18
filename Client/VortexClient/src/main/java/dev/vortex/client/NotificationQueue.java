package dev.vortex.client;

import java.util.*;

/** Bounded, deterministic clock. Hidden notifications do not expire unseen. No callbacks or IO. */
public final class NotificationQueue {
    public enum Kind { SUCCESS, INFO, ERROR }
    public static final class Notice {
        public final long id;
        public final String key,title,body;
        public final Kind kind;
        public final double duration;
        private double age;
        Notice(long id,String key,String title,String body,Kind kind){this.id=id;this.key=key;this.title=title;this.body=body;this.kind=kind;duration=kind==Kind.ERROR?7500:kind==Kind.SUCCESS?3500:4500;}
        public double age(){return age;}
        public float visibility(boolean animate){if(!animate)return 1;return (float)Math.min(ease(age/200),ease((duration-age)/220));}
        public float remaining(){return (float)Math.clamp(1-age/duration,0,1);}
        private static double ease(double t){t=Math.clamp(t,0,1);return 1-Math.pow(1-t,3);}
    }
    private final ArrayList<Notice> active=new ArrayList<>();
    private final ArrayDeque<Notice> pending=new ArrayDeque<>();
    private final LinkedHashMap<String,Long> recent=new LinkedHashMap<>();
    private long serial,last=-1;
    public static String clean(String text,int max){if(text==null)return "";var b=new StringBuilder();text.codePoints().filter(c->!Character.isISOControl(c)&&c!=0xA7).limit(max).forEach(b::appendCodePoint);return b.toString();}
    public synchronized boolean offer(String key,String title,String body,Kind kind,long now){
        key=clean(key,64);if(kind==null || key.isEmpty())return false;
        String id=key;
        if(active.stream().anyMatch(n->n.key.equals(id)) || pending.stream().anyMatch(n->n.key.equals(id)))return false;
        Long previous=recent.get(key);if(previous!=null && now>=previous && now-previous<3000)return false;
        if(pending.size()>=8){
            if(kind!=Kind.ERROR)return false;
            var replace=pending.stream().filter(n->n.kind!=Kind.ERROR).findFirst().orElse(pending.getFirst());pending.remove(replace);
        }
        pending.addLast(new Notice(++serial,key,clean(title,48),clean(body,160),kind));recent.put(key,now);
        while(recent.size()>64)recent.remove(recent.keySet().iterator().next());return true;
    }
    public synchronized List<Notice> advance(long now,int slots,boolean shown){
        double elapsed=last<0?0:Math.clamp(now-last,0,250);last=now;
        if(shown && slots>0){
            for(int i=0;i<Math.min(slots,active.size());i++)active.get(i).age+=elapsed;
            active.removeIf(n->n.age>=n.duration);
            while(active.size()<Math.min(3,slots) && !pending.isEmpty())active.add(pending.removeFirst());
        }
        return List.copyOf(active.subList(0,Math.min(Math.max(slots,0),active.size())));
    }
    public synchronized int size(){return active.size()+pending.size();}
    public synchronized void dismiss(String key){active.removeIf(n->n.key.equals(key));pending.removeIf(n->n.key.equals(key));}
    public synchronized void clear(){active.clear();pending.clear();recent.clear();last=-1;}
}
