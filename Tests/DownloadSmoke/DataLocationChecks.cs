using VortexLauncher.Core.Launch;

internal static class DataLocationChecks
{
    public static void Run(string testRoot)
    {
        string root=Directory.CreateDirectory(Path.Combine(testRoot,"locations")).FullName;
        string registry=Path.Combine(root,"user-data"), temporary=Directory.CreateDirectory(Path.Combine(root,"temporary")).FullName;
        string zipA=Directory.CreateDirectory(Path.Combine(temporary,"zip-a")).FullName;
        string zipB=Directory.CreateDirectory(Path.Combine(temporary,"zip-b")).FullName;
        string first=LauncherDataLocation.Resolve(zipA,registry,temporary);
        Directory.CreateDirectory(Path.Combine(first,"instances","Existing"));
        File.WriteAllText(Path.Combine(first,"instances","Existing","world-marker"),"keep");
        Check(first==LauncherDataLocation.Resolve(zipB,registry,temporary),"two different ZIP launch locations reuse one persistent library");
        string legacy=Directory.CreateDirectory(Path.Combine(root,"old-launcher")).FullName;
        Directory.CreateDirectory(Path.Combine(legacy,"instances","Old world"));
        string legacyRegistry=Path.Combine(root,"legacy-registry");
        Check(LauncherDataLocation.Resolve(legacy,legacyRegistry,temporary)==legacy && Directory.Exists(Path.Combine(legacy,"instances","Old world")),"old portable library is remembered in place, without copying/moving worlds");
        Check(LauncherDataLocation.Resolve(zipB,legacyRegistry,temporary)==legacy,"new executable discovers the remembered old library");
        LauncherDataLocation.Connect(legacy,registry,temporary);
        Check(LauncherDataLocation.Resolve(zipB,registry,temporary)==legacy && File.ReadAllText(Path.Combine(first,"instances","Existing","world-marker"))=="keep","connecting an old library preserves both libraries without merging");
        Reject(()=>LauncherDataLocation.Connect(zipA,registry,temporary),"temporary data locations are rejected");
        Reject(()=>LauncherDataLocation.Connect(root,registry,temporary),"unrelated folders are rejected");
        string malformed=Directory.CreateDirectory(Path.Combine(root,"broken-registry")).FullName;
        File.WriteAllText(Path.Combine(malformed,"data-location.json"),"{broken}");
        Reject(()=>LauncherDataLocation.Resolve(zipB,malformed,temporary),"corrupt location cannot silently reset the library");
        string missingRegistry=Path.Combine(root,"missing-registry");
        string missing=Directory.CreateDirectory(Path.Combine(root,"disconnected")).FullName;
        Directory.CreateDirectory(Path.Combine(missing,"instances"));
        LauncherDataLocation.Connect(missing,missingRegistry,temporary);
        Directory.Move(missing,missing+"-offline");
        Reject(()=>LauncherDataLocation.Resolve(zipB,missingRegistry,temporary),"disconnected library is not replaced by an empty one");
    }
    private static void Check(bool success,string message) { if(!success)throw new Exception(message);Console.WriteLine("PASS: "+message); }
    private static void Reject(Action action,string message) { try { action(); } catch(IOException) { Check(true,message);return; } throw new Exception(message); }
}
