import java.util.*;

/** Uses real mod classes, but never opens a world or contacts a server. Works after production remapping. */
public final class ResourceToolsSmoke {
    public static void run(ClassLoader loader,java.nio.file.Path root)throws Exception{
        Class<?> counter=Class.forName("dev.vortex.client.ResourceCounter",true,loader),goal=Class.forName("dev.vortex.client.ResourceCounter$Goal",true,loader);
        var goalConstructor=goal.getConstructor(String.class,int.class);var valid=counter.getMethod("valid",goal);
        for(Object invalid:new Object[]{null,goalConstructor.newInstance("stone",64),goalConstructor.newInstance("minecraft:stone",0),goalConstructor.newInstance("minecraft:stone",1000001)})
            if((boolean)valid.invoke(null,invalid))throw new AssertionError("Invalid goal accepted");
        Object stone=goalConstructor.newInstance("minecraft:stone",640),iron=goalConstructor.newInstance("minecraft:iron_ingot",128);
        List<?> cleaned=(List<?>)counter.getMethod("clean",List.class).invoke(null,Arrays.asList(null,stone,stone,iron));
        if(cleaned.size()!=2 || !(boolean)valid.invoke(null,stone))throw new AssertionError("Goal validation/dedup failed");
        if(!((List<?>)counter.getMethod("clean",List.class).invoke(null,new Object[]{null})).isEmpty())throw new AssertionError("Null legacy goal list not migrated");
        var tooMany=new ArrayList<>();for(int i=0;i<10;i++)tooMany.add(goalConstructor.newInstance("example:item_"+i,64));
        if(((List<?>)counter.getMethod("clean",List.class).invoke(null,tooMany)).size()!=4)throw new AssertionError("Goal count unbounded");
        Class<?> config=Class.forName("dev.vortex.client.ClientConfig",true,loader);
        java.nio.file.Path saved=root.resolve("resource-goal-config.json");
        java.nio.file.Files.writeString(saved,"{\"hudScale\":1.25,\"resourceCounterEnabled\":true,\"storageMemoryRadius\":999,\"resourceGoals\":[{\"item\":\"minecraft:stone\",\"target\":640},{\"item\":\"minecraft:stone\",\"target\":640},null,{\"item\":\"minecraft:iron_ingot\",\"target\":128}]}");
        Object loaded=config.getMethod("load",java.nio.file.Path.class).invoke(null,saved);
        if(((List<?>)config.getField("resourceGoals").get(loaded)).size()!=2 || !config.getField("resourceCounterEnabled").getBoolean(loaded) || config.getField("hudScale").getFloat(loaded)!=1.25f || config.getField("storageMemoryRadius").getInt(loaded)!=256)
            throw new AssertionError("Saved goal migration reset unrelated settings or lost goals");
        Class<?> gson=Class.forName("com.google.gson.Gson",true,loader);
        String serialized=(String)gson.getMethod("toJson",Object.class).invoke(gson.getConstructor().newInstance(),loaded);
        java.nio.file.Files.writeString(saved,serialized);
        Object reloaded=config.getMethod("load",java.nio.file.Path.class).invoke(null,saved);
        if(((List<?>)config.getField("resourceGoals").get(reloaded)).size()!=2)throw new AssertionError("Goal roundtrip lost saved targets");
        if(config.getField("resourceCounterEnabled").getBoolean(config.getConstructor().newInstance()) || config.getField("storageMemoryEnabled").getBoolean(config.getConstructor().newInstance()))throw new AssertionError("New modules must be explicit opt-in");
        Class<?> index=Class.forName("dev.vortex.client.StorageMemory$Index",true,loader),seen=Class.forName("dev.vortex.client.StorageMemory$Seen",true,loader),item=Class.forName("dev.vortex.client.StorageMemory$Item",true,loader);
        Class<?> pos=seen.getRecordComponents()[1].getType();var position=pos.getConstructor(int.class,int.class,int.class);
        Object origin=position.newInstance(0,64,0),near=position.newInstance(3,64,4),far=position.newInstance(60,64,0);
        Object diamond=item.getConstructor(String.class,String.class,int.class).newInstance("minecraft:diamond","Diamond",12);
        var snapshot=seen.getConstructor(String.class,pos,String.class,List.class,long.class);Object memory=index.getConstructor().newInstance();
        var context=index.getMethod("context",String.class);var observe=index.getMethod("observe",seen);var find=index.getMethod("find",String.class,pos,int.class);
        context.invoke(memory,"world-a/overworld/player-a");
        Class<?> storage=Class.forName("dev.vortex.client.StorageMemory",true,loader),scope=Class.forName("dev.vortex.client.WaypointsFeature$Scope",true,loader);
        Object world=scope.getConstructor(String.class,String.class).newInstance("world-a","overworld");
        var markerFor=storage.getMethod("markerFor",seen,scope);
        Object marker=markerFor.invoke(null,snapshot.newInstance("world-a/overworld/player-a",near,"Chest",List.of(diamond),1000L),world);
        if(marker==null || (int)marker.getClass().getMethod("x").invoke(marker)!=3 || (int)marker.getClass().getMethod("z").invoke(marker)!=4 || !((String)marker.getClass().getMethod("name").invoke(marker)).startsWith("Last seen:"))
            throw new AssertionError("Temporary storage marker has wrong coordinates or claims live contents");
        Object foreign=scope.getConstructor(String.class,String.class).newInstance("world-b","overworld");
        if(markerFor.invoke(null,snapshot.newInstance("world-a/overworld/player-a",near,"Chest",List.of(diamond),1000L),foreign)!=null)throw new AssertionError("Temporary marker crossed world scope");
        observe.invoke(memory,snapshot.newInstance("world-a/overworld/player-a",far,"Far chest",List.of(diamond),1000L));
        observe.invoke(memory,snapshot.newInstance("world-a/overworld/player-a",near,"Near chest",List.of(diamond),2000L));
        var matches=(List<?>)find.invoke(memory,"DIAMOND",origin,64);
        if(matches.size()!=2 || (double)matches.get(0).getClass().getMethod("distance").invoke(matches.get(0))!=5d)throw new AssertionError("Nearest-first/localized literal lookup failed");
        if(((List<?>)find.invoke(memory,"diamond",origin,16)).size()!=1 || !((List<?>)find.invoke(memory,"",origin,64)).isEmpty() || !((List<?>)find.invoke(memory,"diamond stone",origin,64)).isEmpty())throw new AssertionError("Radius/empty/all-word lookup failed");
        observe.invoke(memory,snapshot.newInstance("world-a/overworld/player-a",near,"Near chest",List.of(),3000L));
        if(((List<?>)find.invoke(memory,"diamond",origin,64)).size()!=1 || (int)index.getMethod("size").invoke(memory)!=2)throw new AssertionError("Opening an emptied chest did not replace the old snapshot");
        observe.invoke(memory,snapshot.newInstance("other-world",near,"Wrong world",List.of(diamond),4000L));
        if((int)index.getMethod("size").invoke(memory)!=2)throw new AssertionError("Foreign-world snapshot accepted");
        context.invoke(memory,"world-a/nether/player-a");if((int)index.getMethod("size").invoke(memory)!=0)throw new AssertionError("Dimension change leaked storage history");
        for(int i=0;i<300;i++)observe.invoke(memory,snapshot.newInstance("world-a/nether/player-a",position.newInstance(i,64,0),"Chest",List.of(),1000L+i));
        if((int)index.getMethod("size").invoke(memory)!=256)throw new AssertionError("Session history unbounded");
        context.invoke(memory,"");if((int)index.getMethod("size").invoke(memory)!=0)throw new AssertionError("Disconnect retained storage history");
        for(String screen:new String[]{"ResourceGoalsScreen","StorageFinderScreen"}){
            Class<?> type=Class.forName("dev.vortex.client."+screen,true,loader);type.getDeclaredMethods();
            for(int[] size:new int[][]{{320,240},{640,360},{854,480}}){
                Object layout=type.getMethod("layout",int.class,int.class).invoke(null,size[0],size[1]);Class<?> geometry=layout.getClass();
                int y=(int)geometry.getMethod("y").invoke(layout),height=(int)geometry.getMethod("height").invoke(layout);
                if(y<0 || y+height>size[1])throw new AssertionError("Tool outside viewport");
                if(screen.equals("StorageFinderScreen")){
                    int rows=(int)geometry.getMethod("rows").invoke(layout);
                    if(76+(rows-1)*28+22>height-58)throw new AssertionError("Storage results overlap footer");
                }
            }
        }
        System.out.println("PASS Resource goals: validation/dedup/limits/opt-in; storage snapshots: nearest/radius/all-word search, updates, isolation, bounds and responsive tools");
    }
}
