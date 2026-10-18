package dev.vortex.client;

import java.util.*;
import java.lang.reflect.Field;

/** Screen-local bounded undo. Copies only placement/appearance fields, never other settings. */
public final class HudLayoutHistory {
    private static final List<Field> FIELDS=fields();
    private static List<Field> fields(){
        var fields=new ArrayList<Field>();
        try{for(String prefix:new String[]{"fps","keys","armor","sprint","coordinates","heldItem","effects","ping","speed","clock","tps","blockInfo"})
            for(String suffix:new String[]{"X","Y","Scale","Opacity"})fields.add(ClientConfig.class.getField(prefix+suffix));}
        catch(ReflectiveOperationException error){throw new ExceptionInInitializerError(error);}
        return List.copyOf(fields);
    }
    private final ArrayDeque<List<Object>> undo=new ArrayDeque<>();
    private List<Object> pending;
    private static List<Object> snapshot(){
        var result=new ArrayList<Object>();try{for(var field:FIELDS)result.add(field.get(VortexClient.config));}
        catch(IllegalAccessException impossible){throw new IllegalStateException(impossible);}return List.copyOf(result);
    }
    public void begin(){if(pending==null)pending=snapshot();}
    public void commit(){
        if(pending!=null && !pending.equals(snapshot())){undo.addLast(pending);if(undo.size()>32)undo.removeFirst();}
        pending=null;
    }
    public boolean canUndo(){return !undo.isEmpty();}
    public int size(){return undo.size();}
    public void change(Runnable change){begin();change.run();commit();}
    public boolean undo(){
        commit();var state=undo.pollLast();if(state==null)return false;
        try{for(int i=0;i<FIELDS.size();i++)FIELDS.get(i).set(VortexClient.config,state.get(i));}
        catch(IllegalAccessException impossible){throw new IllegalStateException(impossible);}return true;
    }
}
