using System.IO.Compression;
using System.Text.Json;
using VortexLauncher.Core.Launch;
using VortexLauncher.Models;

// Per-instance Java, memory and JVM arguments: defaults, overrides, safe loading and untrusted archives.
internal static class LaunchSettingsChecks
{
    internal static async Task RunAsync(string testRoot)
    {
        string work = Directory.CreateDirectory(Path.Combine(testRoot, "launch-settings")).FullName;
        var plain = new InstanceModel { Name = "Plain", Version = "1.20.1", Loader = "Vanilla" };
        Check(LaunchSettings.RamGb(plain, 6) == 6 && LaunchSettings.JavaPath(plain, "C:/java/bin/java.exe") == "C:/java/bin/java.exe" &&
              LaunchSettings.JvmArgs(plain, "-XX:+UseG1GC") == "-XX:+UseG1GC" && !LaunchSettings.HasOverrides(plain),
              "an instance without overrides follows the launcher's memory, Java and arguments");

        var custom = new InstanceModel { Name = "Custom", Version = "1.20.1", Loader = "Vanilla", RamGbOverride = 10, JavaPathOverride = "  D:/jdk/bin/java.exe ", JvmArgsOverride = "" };
        Check(LaunchSettings.RamGb(custom, 4) == 10 && LaunchSettings.JavaPath(custom, "x") == "D:/jdk/bin/java.exe" && LaunchSettings.JvmArgs(custom, "-XX:+UseG1GC") == "" && LaunchSettings.HasOverrides(custom),
              "overrides win, an empty argument list is a real choice, and the Java path is trimmed");
        LaunchSettings.Reset(custom);
        Check(!LaunchSettings.HasOverrides(custom) && LaunchSettings.RamGb(custom, 4) == 4 && LaunchSettings.JvmArgs(custom, "-a") == "-a", "reset returns an instance to the launcher defaults");

        Check(LaunchSettings.ClampRam(0) == 1 && LaunchSettings.ClampRam(500) == 64 && LaunchSettings.ClampRam(8) == 8, "memory is kept between 1 and 64 GB");
        var damaged = new InstanceModel { RamGbOverride = 9999, JavaPathOverride = "C:\\java\u0007\u0000\\bin\\java.exe", JvmArgsOverride = "-Da=1\n-Db=2\u0001" + new string('x', 6000) };
        LaunchSettings.Sanitize(damaged);
        Check(damaged.RamGbOverride == 64 && !damaged.JavaPathOverride.Any(char.IsControl) && damaged.JvmArgsOverride!.Contains('\n') && !damaged.JvmArgsOverride.Contains('\u0001') &&
              damaged.JvmArgsOverride.Length == LaunchSettings.MaxTextLength, "values read from disk are bounded and stripped of control characters");
        var negative = new InstanceModel { RamGbOverride = -3 }; LaunchSettings.Sanitize(negative);
        Check(negative.RamGbOverride == 0, "a negative memory value means no override");

        string javaDir = Directory.CreateDirectory(Path.Combine(work, "jdk", "bin")).FullName;
        string fakeJava = Path.Combine(javaDir, "java.exe"); File.WriteAllText(fakeJava, "x");
        string notJava = Path.Combine(javaDir, "notepad.exe"); File.WriteAllText(notJava, "x");
        Check(LaunchSettings.CheckJavaPath("") == null && LaunchSettings.CheckJavaPath(null) == null && LaunchSettings.CheckJavaPath(fakeJava) == null, "an empty or existing Java path is accepted");
        Check(LaunchSettings.CheckJavaPath(notJava) != null && LaunchSettings.CheckJavaPath(Path.Combine(javaDir, "missing", "java.exe")) != null && LaunchSettings.CheckJavaPath("C:\\a\u0001\\java.exe") != null,
              "only existing java / javaw files are accepted as a Java path");
        Check(LaunchSettings.RamWarning(4, 16) == "" && LaunchSettings.RamWarning(14, 16).Contains("Leave some") && LaunchSettings.RamWarning(16, 16).Contains("cannot be used") && LaunchSettings.RamWarning(8, 0) == "",
              "memory warnings appear only when the game would take most of the computer");

        // Round trip through instance.json; older files without the new fields keep following the launcher.
        var manager = new InstanceManager(Path.Combine(work, "instances"));
        var saved = manager.CreateInstance("Tuned", "1.20.1", "Vanilla");
        saved.RamGbOverride = 12; saved.JavaPathOverride = fakeJava; saved.JvmArgsOverride = "-XX:+UseZGC";
        manager.SaveInstance(saved);
        var reloaded = manager.LoadInstances().Single(i => i.Name == "Tuned");
        Check(reloaded.RamGbOverride == 12 && reloaded.JavaPathOverride == fakeJava && reloaded.JvmArgsOverride == "-XX:+UseZGC", "per-instance settings survive a restart");
        string oldFolder = Directory.CreateDirectory(Path.Combine(work, "instances", "Legacy")).FullName;
        File.WriteAllText(Path.Combine(oldFolder, "instance.json"), "{\"Name\":\"Legacy\",\"Version\":\"1.20.1\",\"Loader\":\"Vanilla\"}");
        var legacy = manager.LoadInstances().Single(i => i.Name == "Legacy");
        Check(!LaunchSettings.HasOverrides(legacy) && legacy.JvmArgsOverride == null, "an instance saved before this feature loads with no overrides");
        string hostile = Directory.CreateDirectory(Path.Combine(work, "instances", "Hand edited")).FullName;
        File.WriteAllText(Path.Combine(hostile, "instance.json"), "{\"Name\":\"Hand edited\",\"Version\":\"1.20.1\",\"Loader\":\"Vanilla\",\"RamGbOverride\":123456,\"JavaPathOverride\":\"x\\u0000y\"}");
        var edited = manager.LoadInstances().Single(i => i.Name == "Hand edited");
        Check(edited.RamGbOverride == 64 && !edited.JavaPathOverride.Contains('\0'), "a hand-edited instance.json cannot push memory or paths out of bounds");

        // A pack from someone else must not bring its own Java or arguments (it would run on this computer).
        var hostilePack = new InstanceModel { Name = "Shared pack", Version = "1.20.1", Loader = "Vanilla", RamGbOverride = 48, JavaPathOverride = "C:\\Users\\other\\evil\\java.exe", JvmArgsOverride = "-javaagent:evil.jar" };
        string zip = Path.Combine(work, "hostile.zip");
        using (var file = new FileStream(zip, FileMode.Create))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("instance.json").Open())) writer.Write(JsonSerializer.Serialize(hostilePack));
            using (var note = new StreamWriter(archive.CreateEntry("options.txt").Open())) note.Write("x");
        }
        var imported = await new InstanceManager(Path.Combine(work, "imported")).ImportArchiveAsync(zip);
        Check(!LaunchSettings.HasOverrides(imported) && imported.JvmArgsOverride == null, "an imported archive never brings its own Java, memory or arguments");
        var importedFromDisk = new InstanceManager(Path.Combine(work, "imported")).LoadInstances().Single();
        Check(!LaunchSettings.HasOverrides(importedFromDisk), "the imported instance is saved without those overrides too");

        // Exporting your own instance does not leak this computer's Java path or arguments into the archive.
        string export = Path.Combine(work, "export.zip");
        manager.ExportArchive(saved, export);
        using (var archive = ZipFile.OpenRead(export))
        {
            using var reader = new StreamReader(archive.GetEntry("instance.json")!.Open());
            string json = reader.ReadToEnd();
            Check(!json.Contains(fakeJava.Replace("\\", "\\\\")) && !json.Contains("UseZGC") && !json.Contains("\"RamGbOverride\": 12"), "exported archives carry no local Java path, memory or arguments");
        }
        Check(saved.RamGbOverride == 12 && saved.JavaPathOverride == fakeJava, "exporting leaves the live instance's settings untouched");
        Console.WriteLine("LAUNCH_SETTINGS_CHECKS_OK");
    }

    private static void Check(bool valid, string message) { if (!valid) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}
