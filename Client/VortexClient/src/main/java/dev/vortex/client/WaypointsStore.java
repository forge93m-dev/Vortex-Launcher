package dev.vortex.client;

import com.google.gson.Gson;
import java.nio.file.*;
import java.util.*;

/** Private local data, isolated by world/server and dimension; no networking. */
public final class WaypointsStore {
    public record Point(UUID id,String world,String dimension,String name,int x,int y,int z,int color,boolean hidden,String group){
        public Point {if(group==null)group="General";}
        public Point(UUID id,String world,String dimension,String name,int x,int y,int z,int color,boolean hidden){this(id,world,dimension,name,x,y,z,color,hidden,"General");}
        public Point hidden(boolean value){return new Point(id,world,dimension,name,x,y,z,color,value,group);}
    }
    private record Document(int version,List<Point> points){}
    private static final Gson JSON=new Gson();
    private final Path path;
    public WaypointsStore(Path path){this.path=path.toAbsolutePath().normalize();}
    public static WaypointsStore local(){return new WaypointsStore(Path.of(System.getProperty("user.home"),".vortex","waypoints-v1.json"));}
    private void unlinked()throws Exception{for(Path p=path;p!=null;p=p.getParent())if(Files.isSymbolicLink(p))throw new IllegalArgumentException("Linked waypoint storage is not supported.");}
    public static String cleanName(String value){if(value==null)return "";var result=new StringBuilder();value.strip().codePoints().filter(c->!Character.isISOControl(c) && c!=0xA7).limit(32).forEach(result::appendCodePoint);return result.toString().strip();}
    public static boolean valid(Point p){return p!=null && p.id()!=null && p.world()!=null && p.world().matches("[a-f0-9]{64}") && p.dimension()!=null && p.dimension().matches("[a-z0-9_.-]+:[a-z0-9_./-]+") && p.dimension().length()<=160 && !cleanName(p.name()).isEmpty() && cleanName(p.name()).equals(p.name()) && Math.abs((long)p.x())<=30000000 && Math.abs((long)p.z())<=30000000 && Math.abs((long)p.y())<=2048 && (p.color()&0xFF000000)==0;}
    public synchronized List<Point> load()throws Exception{
        unlinked();if(!Files.exists(path))return List.of();if(Files.size(path)>1024*1024)throw new IllegalArgumentException("Waypoint file is too large; original kept.");
        final Document doc;try{doc=JSON.fromJson(Files.readString(path),Document.class);}catch(Exception invalid){throw new IllegalArgumentException("Waypoint file could not be read; original kept.");}
        if(doc==null || doc.version()!=1 && doc.version()!=2 || doc.points()==null || doc.points().size()>512)throw new IllegalArgumentException("Invalid waypoint file; original kept.");
        var ids=new HashSet<UUID>();for(var point:doc.points())if(!valid(point) || point.group().isEmpty() || !point.group().equals(cleanName(point.group())) || !ids.add(point.id()))throw new IllegalArgumentException("Invalid waypoint entry; original kept.");
        return List.copyOf(doc.points());
    }
    public synchronized List<Point> update(Point point,UUID remove)throws Exception{
        if(point!=null && (!valid(point) || point.group().isEmpty() || !point.group().equals(cleanName(point.group()))))throw new IllegalArgumentException("Enter a name, group and valid coordinates.");
        unlinked();Files.createDirectories(path.getParent());unlinked();Path lock=path.resolveSibling("waypoints-v1.lock");if(Files.isSymbolicLink(lock))throw new IllegalArgumentException("Waypoint lock must not be linked.");
        try(var channel=java.nio.channels.FileChannel.open(lock,StandardOpenOption.CREATE,StandardOpenOption.WRITE)){
            java.nio.channels.FileLock held=null;
            for(int i=0;i<50 && held==null;i++){try{held=channel.tryLock();}catch(java.nio.channels.OverlappingFileLockException busy){}if(held==null)Thread.sleep(100);}
            if(held==null)throw new IllegalStateException("Waypoints are in use. Retry shortly.");
            try{
                var result=new ArrayList<>(load());
                if(remove!=null)result.removeIf(p->p.id().equals(remove));
                if(point!=null){result.removeIf(p->p.id().equals(point.id()));result.add(point);}
                if(result.size()>512)throw new IllegalArgumentException("Waypoint storage is full. Remove unused entries before adding another.");
                Path temp=Files.createTempFile(path.getParent(),"waypoints-",".part");
                try{Files.writeString(temp,JSON.toJson(new Document(2,result)));unlinked();try{Files.move(temp,path,StandardCopyOption.ATOMIC_MOVE,StandardCopyOption.REPLACE_EXISTING);}catch(AtomicMoveNotSupportedException unsupported){Files.move(temp,path,StandardCopyOption.REPLACE_EXISTING);}}finally{Files.deleteIfExists(temp);}
                return List.copyOf(result);
            }finally{held.release();}
        }
    }
    public static List<Point> visible(List<Point> points,String world,String dimension){return points.stream().filter(p->!p.hidden() && p.world().equals(world) && p.dimension().equals(dimension)).toList();}
}
