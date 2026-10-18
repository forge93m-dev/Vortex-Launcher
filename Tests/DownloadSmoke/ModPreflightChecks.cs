using System.IO.Compression;
using System.Text;
using System.Text.Json;
using VortexLauncher.Core.Diagnostics;

internal static class ModPreflightChecks
{
    internal static async Task RunAsync(string output)
    {
        var service = new ModPreflightService();
        string NewRoot(string name) => Directory.CreateDirectory(Path.Combine(output, "mod-preflight", name)).FullName;
        void Check(bool ok, string text) { if (!ok) throw new Exception("FAIL: " + text); Console.WriteLine("PASS: " + text); }
        Task<ModPreflightReport> Scan(string root, string loader = "Fabric") => service.CheckAsync(root, "26.3", loader, "0.19.5");
        string healthy = NewRoot("healthy");
        Jar(healthy, "base.JAR", Meta("base", "1.2.3"));
        Jar(healthy, "consumer.jar", Meta("consumer", "1.0", depends: new() { ["base"] = ">=1.0 <2", ["minecraft"] = "~26.3", ["fabricloader"] = ">=0.19.5", ["java"] = ">=25" }));
        Jar(healthy, "old.jar.disabled", Meta("base", "0.1"));
        var report = await Scan(healthy);
        Check(!report.NeedsReview && report.Checked == 2 && report.Disabled == 1, "healthy Fabric set, uppercase JAR extension and disabled duplicate do not warn");
        Jar(healthy, "copy.jar", Meta("base", "1.2.3"));
        report = await Scan(healthy);
        Check(report.Issues.Any(i => i.Code == "duplicate-id" && i.IsError && i.File.Contains("base.JAR") && i.File.Contains("copy.jar")), "duplicate top-level IDs name both files and block launch");
        string missing = NewRoot("missing");
        Jar(missing, "consumer.jar", Meta("consumer", "1", depends: new() { ["fabric-api"] = "*" }));
        Check((await Scan(missing)).Issues.Any(i => i.IsError && i.Code == "missing-dependency"), "missing required dependency is an error when all metadata is known");
        Jar(missing, "unknown.jar", null);
        report = await Scan(missing);
        Check(!report.HasErrors && report.Issues.Any(i => i.Code == "missing-dependency"), "unknown archive downgrades an unproven missing dependency to a warning");
        string wrong = NewRoot("wrong");
        Jar(wrong, "wrong.jar", Meta("wrong", "1", depends: new() { ["minecraft"] = "1.21.x", ["fabricloader"] = ">=0.20" }));
        Check((await Scan(wrong)).Issues.Count(i => i.IsError && i.Code == "wrong-version") == 2, "known Minecraft and pinned Fabric loader mismatches block launch");
        Directory.CreateDirectory(Path.Combine(wrong, "config")); File.WriteAllText(Path.Combine(wrong, "config/fabric_loader_dependencies.json"), "{}");
        Check(!(await Scan(wrong)).HasErrors, "dependency override files prevent false hard errors");
        string aliases = NewRoot("aliases");
        Jar(aliases, "provider.jar", Meta("provider", "1", provides: new[] { "other-api" }));
        Jar(aliases, "consumer.jar", Meta("consumer", "1", depends: new() { ["other-api"] = ">=2" }));
        Check(!(await Scan(aliases)).HasErrors, "provided aliases satisfy presence without assuming alias versions");
        string nested = NewRoot("nested");
        var child = Bytes(Meta("bundled", "2.0"));
        Jar(nested, "parent.jar", Meta("parent", "1", depends: new() { ["bundled"] = ">=2" }, jars: new[] { new { file = "libs/child.jar" } }), ("libs/child.jar", child));
        Jar(nested, "alternative.jar", Meta("alternative", "1", jars: new[] { new { file = "libs/child.jar" } }), ("libs/child.jar", child));
        Check(!(await Scan(nested)).NeedsReview, "nested dependencies and repeated optional bundled versions are not mistaken for duplicates");
        string loaderLibrary = NewRoot("loader-library");
        Jar(loaderLibrary, "consumer.jar", Meta("consumer", "1", depends: new() { ["mixinextras"] = ">=0.5.5" }));
        Check(!(await Scan(loaderLibrary)).HasErrors, "loader-provided MixinExtras is not falsely reported as a missing mandatory mod");
        Directory.CreateDirectory(Path.Combine(loaderLibrary, "libraries"));
        File.WriteAllBytes(Path.Combine(loaderLibrary, "libraries/fabric-loader-0.19.5.jar"), Bytes(Meta("fabricloader", "0.19.5", jars: new[] { new { file = "libs/extras.jar" } }), ("libs/extras.jar", Bytes(Meta("mixinextras", "0.5.5")))));
        Check(!(await Scan(loaderLibrary)).NeedsReview, "selected loader's bundled libraries satisfy versioned mod requirements");
        string vortex = NewRoot("bundled-client");
        Directory.CreateDirectory(Path.Combine(vortex, "mods"));
        using (var artifact = typeof(VortexLauncher.Core.Network.VortexClientService).Assembly.GetManifestResourceStream("VortexLauncher.Client.vortex-client-26.3-fabric-" + VortexLauncher.Core.Network.VortexClientService.ClientVersion + ".jar")!)
        using (var destination = File.Create(Path.Combine(vortex, "mods/vortex-client.jar"))) artifact.CopyTo(destination);
        Check(!(await Scan(vortex)).NeedsReview, "bundled Vortex Client passes the real offline metadata check without spurious warnings");
        string server = NewRoot("server");
        Jar(server, "server.jar", Meta("server-only", "1", environment: "server", depends: new() { ["missing-on-server"] = "*" }));
        Check(!(await Scan(server)).HasErrors, "server-only mod dependencies are not enforced on the client");
        string conflict = NewRoot("conflict");
        Jar(conflict, "base.jar", Meta("base", "2"));
        Jar(conflict, "conflict.jar", JsonSerializer.Serialize(new { schemaVersion = 1, id = "conflict", version = "1", breaks = new Dictionary<string, string> { ["base"] = ">=2" } }));
        Check((await Scan(conflict)).Issues.Any(i => i.Code == "declared-conflict" && i.IsError), "declared hard conflicts with installed mandatory mods block launch");
        string malformed = NewRoot("malformed");
        Jar(malformed, "bad-json.jar", "{broken");
        Check(!(await Scan(malformed)).HasErrors, "unreadable metadata is unverified, not a guessed incompatibility");
        File.WriteAllText(Path.Combine(malformed, "mods/corrupt.jar"), "not a zip");
        Check((await Scan(malformed)).Issues.Any(i => i.Code == "broken-jar" && i.IsError), "corrupt enabled archives are distinguished from unknown metadata");
        Check(!(await Scan(malformed, "Vanilla")).HasErrors, "unused mods folder does not block Vanilla startup");
        Check(!(await Scan(wrong, "Quilt")).HasErrors, "Fabric dependency rules are not treated as authoritative on Quilt/bridges");
        string oversized = NewRoot("bounded"); Jar(oversized, "large-metadata.jar", new string(' ', 524289));
        Check(!(await Scan(oversized)).HasErrors, "inspection size limits yield warnings rather than invented broken-JAR errors");
        bool? Match(string version, string predicate) { using var json = JsonDocument.Parse(JsonSerializer.Serialize(predicate)); return ModVersionRange.Matches(version, json.RootElement); }
        Check(Match("1.2.3+mc26", ">=1.2 <2") == true && Match("1.2.3", "~1.2") == true && Match("0.8", "^0.2") == true && Match("26.3", "26.3.x") == true && Match("26.4", "26.3.x") == false, "stable Fabric range semantics include AND, build metadata, tilde, caret and wildcards");
        Check(Match("26.3-snapshot-1", ">=26.3") == null && Match("1.0", "1.0 || 2.0") == null && Match("1", ">=2 unknown") == null, "snapshots and unsupported expressions remain unknown even beside a mismatch");
        using (var ranges = JsonDocument.Parse("[\"1.21.x\", \"~26.3\"]")) Check(ModVersionRange.Matches("26.3", ranges.RootElement) == true, "array dependency predicates use OR semantics");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { await service.CheckAsync(healthy, "26.3", "Fabric", token: cancel.Token); throw new Exception("Scan ignored cancellation"); }
        catch (OperationCanceledException) { Check(true, "offline checks are cancellable"); }
        Check(File.Exists(Path.Combine(healthy, "mods/copy.jar")) && !Directory.Exists(Path.Combine(nested, "libs")), "checking never disables, deletes or extracts mods");
        Console.WriteLine("MOD_PREFLIGHT_OK");
    }
    private static string Meta(string id, string version, Dictionary<string, string>? depends = null, string[]? provides = null, object? jars = null, string environment = "*")
        => JsonSerializer.Serialize(new { schemaVersion = 1, id, version, depends = depends ?? new(), provides = provides ?? Array.Empty<string>(), jars = jars ?? Array.Empty<object>(), environment });
    private static byte[] Bytes(string? json, params (string Name, byte[] Content)[] entries)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            if (json != null) { using var content = zip.CreateEntry("fabric.mod.json").Open(); content.Write(Encoding.UTF8.GetBytes(json)); }
            foreach (var item in entries) { using var content = zip.CreateEntry(item.Name).Open(); content.Write(item.Content); }
        }
        return bytes.ToArray();
    }
    private static void Jar(string root, string name, string? json, params (string Name, byte[] Content)[] entries)
    { Directory.CreateDirectory(Path.Combine(root, "mods")); File.WriteAllBytes(Path.Combine(root, "mods", name), Bytes(json, entries)); }
}
