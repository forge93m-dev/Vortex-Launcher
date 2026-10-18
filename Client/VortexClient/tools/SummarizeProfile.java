import java.nio.file.Path;
import java.util.*;
import jdk.jfr.consumer.*;

/** Reads only explicitly selected performance fields. Never dumps event payloads or JVM/environment values. */
public final class SummarizeProfile {
    static final Map<String,Long> counts=new TreeMap<>(),threads=new HashMap<>(),allocations=new HashMap<>(),allocationStacks=new HashMap<>(),parks=new HashMap<>();
    static final Map<String,Map<String,Long>> leaves=new HashMap<>(),inclusive=new HashMap<>();
    static final Map<String,double[]> cpu=new HashMap<>();
    static long allocWeight,gcCount,gcNanos,maxGc;static double cpuUser,cpuSystem,cpuMachine;static int cpuEvents;
    static final List<Integer> fps=new ArrayList<>();static final List<Double> ticks=new ArrayList<>();
    static String method(RecordedFrame f){var m=f.getMethod();return m.getType().getName()+"."+m.getName();}
    static String thread(RecordedThread t){return t==null?"unknown":t.getJavaName();}
    static void add(Map<String,Long> m,String key,long value){m.merge(key,value,Long::sum);}
    static void top(String label,Map<String,Long> values,int limit){System.out.println(label);values.entrySet().stream().sorted(Map.Entry.<String,Long>comparingByValue().reversed()).limit(limit).forEach(e->System.out.println("  "+e.getValue()+"  "+e.getKey()));}
    public static void main(String[] args)throws Exception{
        try(var recording=new RecordingFile(Path.of(args[0]))){while(recording.hasMoreEvents()){
            var e=recording.readEvent();String type=e.getEventType().getName();add(counts,type,1);
            switch(type){
                case "minecraft.ClientFps"->fps.add(e.getInt("fps"));
                case "minecraft.ServerTickTime"->ticks.add(e.getDuration("averageTickDuration").toNanos()/1e6);
                case "jdk.ExecutionSample","jdk.NativeMethodSample"->{
                    String name=thread(e.getThread("sampledThread"));add(threads,name,1);var trace=e.getStackTrace();if(trace==null || trace.getFrames().isEmpty())break;
                    add(leaves.computeIfAbsent(name,k->new HashMap<>()),method(trace.getFrames().getFirst()),1);
                    var unique=new HashSet<String>();for(var frame:trace.getFrames())unique.add(method(frame));for(String frame:unique)add(inclusive.computeIfAbsent(name,k->new HashMap<>()),frame,1);
                }
                case "jdk.CPULoad"->{cpuUser+=e.getFloat("jvmUser");cpuSystem+=e.getFloat("jvmSystem");cpuMachine+=e.getFloat("machineTotal");cpuEvents++;}
                case "jdk.ThreadCPULoad"->{var a=cpu.computeIfAbsent(thread(e.getThread()),k->new double[3]);a[0]+=e.getFloat("user");a[1]+=e.getFloat("system");a[2]++;}
                case "jdk.GarbageCollection"->{gcCount++;long nanos=e.getDuration("sumOfPauses").toNanos();gcNanos+=nanos;maxGc=Math.max(maxGc,e.getDuration("longestPause").toNanos());}
                case "jdk.ObjectAllocationSample"->{long weight=e.getLong("weight");allocWeight+=weight;add(allocations,e.getClass("objectClass").getName(),weight);var trace=e.getStackTrace();if(trace!=null)for(var frame:trace.getFrames()){String name=method(frame);if(name.startsWith("net.minecraft.") || name.startsWith("dev.vortex.")){add(allocationStacks,name,weight);break;}}}
                case "jdk.ThreadPark","jdk.JavaMonitorEnter"->{var trace=e.getStackTrace();String at=trace==null || trace.getFrames().isEmpty()?"unknown":method(trace.getFrames().getFirst());add(parks,thread(e.getThread())+" "+type+" "+at,e.getDuration().toNanos());}
            }
        }}
        top("EVENT_COUNTS",counts,40);top("SAMPLES_BY_THREAD",threads,20);
        if(!fps.isEmpty()){var sorted=fps.stream().sorted().toList();System.out.printf(Locale.ROOT,"FPS samples=%d min=%d median=%d p90=%d max=%d mean=%.2f%n",fps.size(),sorted.getFirst(),sorted.get(sorted.size()/2),sorted.get(Math.min(sorted.size()-1,(int)(sorted.size()*.9))),sorted.getLast(),fps.stream().mapToInt(Integer::intValue).average().orElse(0));System.out.println("FPS_TIMELINE "+fps);var active=fps.stream().filter(value->value>10).sorted().toList();if(!active.isEmpty())System.out.printf(Locale.ROOT,"FPS_EXCLUDING_10_CAP samples=%d min=%d median=%d max=%d mean=%.2f (one-second FPS counters, not frame-time percentiles)%n",active.size(),active.getFirst(),active.get(active.size()/2),active.getLast(),active.stream().mapToInt(Integer::intValue).average().orElse(0));}
        if(!ticks.isEmpty())System.out.printf(Locale.ROOT,"SERVER_TICK_MS samples=%d mean=%.3f max=%.3f%n",ticks.size(),ticks.stream().mapToDouble(Double::doubleValue).average().orElse(0),ticks.stream().mapToDouble(Double::doubleValue).max().orElse(0));
        if(cpuEvents>0)System.out.printf(Locale.ROOT,"CPU_MEAN jvmUser=%.3f jvmSystem=%.3f machine=%.3f events=%d%n",cpuUser/cpuEvents,cpuSystem/cpuEvents,cpuMachine/cpuEvents,cpuEvents);
        System.out.println("THREAD_CPU_MEANS (fraction of all logical CPUs)");cpu.entrySet().stream().sorted(Comparator.comparingDouble((Map.Entry<String,double[]> e)->(e.getValue()[0]+e.getValue()[1])/e.getValue()[2]).reversed()).limit(15).forEach(e->{var a=e.getValue();System.out.printf(Locale.ROOT,"  %.4f user %.4f system %s samples=%.0f%n",a[0]/a[2],a[1]/a[2],e.getKey(),a[2]);});
        for(String name:threads.entrySet().stream().sorted(Map.Entry.<String,Long>comparingByValue().reversed()).limit(7).map(Map.Entry::getKey).toList()){top("LEAF "+name,leaves.getOrDefault(name,Map.of()),18);top("INCLUSIVE "+name,inclusive.getOrDefault(name,Map.of()),25);}
        System.out.printf(Locale.ROOT,"GC collections=%d pauseTotalMs=%.3f maxPauseMs=%.3f sampledAllocationMiB=%.2f%n",gcCount,gcNanos/1e6,maxGc/1e6,allocWeight/1048576d);
        top("ALLOCATION_CLASSES (sampled weights in bytes)",allocations,15);top("ALLOCATION_SITES",allocationStacks,20);top("STALL_TOTAL_NANOSECONDS",parks,20);
    }
}
