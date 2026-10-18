package dev.vortex.client;

import java.util.*;

/** Session evidence only. Never treats a local save, cached key or failed request as publication. */
public final class CapePublication {
    public enum State { PREVIEW, HIDDEN, UNKNOWN, LOCAL, PUBLISHED, UPLOADING, UNCONFIRMED }
    public record Status(State state,String label,String detail,boolean retry){ }
    public record Request(UUID profile,String cape,long sequence){ }
    private static final class Evidence {
        String remote,failed;long checked;Request pending;
    }
    private final LinkedHashMap<UUID,Evidence> profiles=new LinkedHashMap<>();
    private long sequence;
    private Evidence evidence(UUID id){
        var result=profiles.computeIfAbsent(id,ignored->new Evidence());
        while(profiles.size()>8)profiles.remove(profiles.keySet().iterator().next());return result;
    }
    public Request begin(UUID id,String cape){var e=evidence(id);e.pending=new Request(id,cape,++sequence);e.failed=null;return e.pending;}
    public boolean complete(Request request,boolean success,long now){
        var e=profiles.get(request.profile());if(e==null || !request.equals(e.pending))return false;
        e.pending=null;
        if(success){e.remote=request.cape();e.checked=now;e.failed=null;}else e.failed=request.cape();return true;
    }
    public void observe(UUID id,String remote,long now){
        if(id==null)return;var e=evidence(id);if(e.pending!=null)return;
        e.remote=remote==null?"none":remote;e.checked=now;
        if(Objects.equals(e.failed,e.remote))e.failed=null;
    }
    public void cancelPending(){for(var e:profiles.values())e.pending=null;}
    public Status status(UUID id,String selected,boolean enabled,boolean draft,long now){
        if(draft)return new Status(State.PREVIEW,"Preview","This is a preview only. Click Apply to use it, then Online to publish separately.",false);
        if(!enabled)return new Status(State.HIDDEN,"Hidden here","Vortex capes are disabled here. This does not remove a previously published cape online.",false);
        var e=profiles.get(id);
        if(e!=null && e.pending!=null && Objects.equals(e.pending.cape(),selected))return new Status(State.UPLOADING,"Publishing…","Waiting for server confirmation. Your local selection is already saved.",false);
        if(e!=null && Objects.equals(e.failed,selected))return new Status(State.UNCONFIRMED,"Unconfirmed","The server did not confirm publication. The outcome is unknown; click Retry to send again.",true);
        if(e==null || e.remote==null || now-e.checked>300_000 || now<e.checked)return new Status(State.UNKNOWN,"Not checked","Online status is unknown or out of date. Saving locally does not publish your cape.",false);
        if(Objects.equals(e.remote,selected))return new Status(State.PUBLISHED,"none".equals(selected)?"Vanilla":"Published",
            "none".equals(selected)?"The server confirmed the standard cape instead of a Vortex cape.":"The server confirmed this cape. Other updated Vortex clients may take about a minute to refresh.",false);
        return new Status(State.LOCAL,"Local only","This selection is not published. Others may see your previous cape; click Online to replace it.",false);
    }
}
