using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VortexLauncher.Core.Launch;
using VortexLauncher.Core.Management;

// CurseForge packs: manifest parsing, the API key requirement, a fake CurseForge API and CDN, and files that must stay manual.
internal static class CurseForgeImportChecks
{
    private sealed class Fixture : HttpMessageHandler
    {
        public readonly Dictionary<string, byte[]> Cdn = new();
        public string? KeySeen; public int ApiCalls, CdnCalls; public bool Refuse;
        public readonly HashSet<string> Hosts = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            if (url.StartsWith("https://api.curseforge.com/v1/", StringComparison.Ordinal))
            {
                ApiCalls++;
                KeySeen = request.Headers.TryGetValues("x-api-key", out var values) ? values.Single() : null;
                if (Refuse) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                string body = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                string json = url.EndsWith("/mods/files", StringComparison.Ordinal)
                    ? Files(JsonDocument.Parse(body).RootElement.GetProperty("fileIds").EnumerateArray().Select(i => i.GetInt32()))
                    : Mods();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            }
            CdnCalls++; if (request.RequestUri!.Host.Length > 0) Hosts.Add(request.RequestUri.Host);
            return Task.FromResult(Cdn.TryGetValue(url, out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
        private static string Hash(byte[] data) => Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant();
        private string Files(IEnumerable<int> ids)
        {
            var entries = new List<object>();
            foreach (int id in ids)
            {
                switch (id)
                {
                    case 11: entries.Add(new { id, fileName = "good-mod.jar", downloadUrl = "https://edge.forgecdn.net/files/1/11/good-mod.jar", fileLength = Cdn["https://edge.forgecdn.net/files/1/11/good-mod.jar"].Length, hashes = new[] { new { value = Hash(Cdn["https://edge.forgecdn.net/files/1/11/good-mod.jar"]), algo = 1 } } }); break;
                    case 12: entries.Add(new { id, fileName = "blocked.jar", downloadUrl = (string?)null, fileLength = 10, hashes = new[] { new { value = new string('a', 40), algo = 1 } } }); break;
                    case 13: entries.Add(new { id, fileName = "evil-host.jar", downloadUrl = "https://evil.example/evil-host.jar", fileLength = 10, hashes = new[] { new { value = new string('b', 40), algo = 1 } } }); break;
                    case 14: entries.Add(new { id, fileName = "nice-pack.zip", downloadUrl = "https://mediafilez.forgecdn.net/files/1/14/nice-pack.zip", fileLength = Cdn["https://mediafilez.forgecdn.net/files/1/14/nice-pack.zip"].Length, hashes = new[] { new { value = Hash(Cdn["https://mediafilez.forgecdn.net/files/1/14/nice-pack.zip"]), algo = 1 } } }); break;
                    case 15: entries.Add(new { id, fileName = "..\\..\\escape.jar", downloadUrl = "https://edge.forgecdn.net/files/1/15/escape.jar", fileLength = 10, hashes = new[] { new { value = new string('c', 40), algo = 1 } } }); break;
                    case 16: entries.Add(new { id, fileName = "tampered.jar", downloadUrl = "https://edge.forgecdn.net/files/1/16/tampered.jar", fileLength = Cdn["https://edge.forgecdn.net/files/1/16/tampered.jar"].Length, hashes = new[] { new { value = new string('d', 40), algo = 1 } } }); break;
                }
            }
            return JsonSerializer.Serialize(new { data = entries });
        }
        private static string Mods() => JsonSerializer.Serialize(new { data = new object[]
        {
            new { id = 1, name = "Good Mod", classId = 6, links = new { websiteUrl = "https://www.curseforge.com/minecraft/mc-mods/good-mod" } },
            new { id = 2, name = "Blocked Mod", classId = 6, links = new { websiteUrl = "https://www.curseforge.com/minecraft/mc-mods/blocked" } },
            new { id = 3, name = "Evil Host", classId = 6, links = new { websiteUrl = (string?)null } },
            new { id = 4, name = "Nice Pack", classId = 12, links = new { websiteUrl = (string?)null } },
            new { id = 5, name = "Escape", classId = 6, links = new { websiteUrl = (string?)null } },
            new { id = 6, name = "Tampered", classId = 6, links = new { websiteUrl = (string?)null } },
        } });
    }

    private static string Manifest(string loader, string type = "minecraftModpack", string files = "[]", string overrides = "overrides") =>
        $"{{\"minecraft\":{{\"version\":\"1.20.1\",\"modLoaders\":[{{\"id\":\"{loader}\",\"primary\":true}}]}},\"manifestType\":\"{type}\",\"manifestVersion\":1,\"name\":\"CF fixture\",\"version\":\"1.0\",\"author\":\"x\",\"files\":{files},\"overrides\":\"{overrides}\"}}";

    private static void Zip(string path, params (string Path, string Content)[] entries)
    {
        using var file = new FileStream(path, FileMode.Create);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var (name, content) in entries) { using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write(content); }
    }

    internal static async Task RunAsync(string testRoot)
    {
        string work = Directory.CreateDirectory(Path.Combine(testRoot, "curseforge")).FullName;
        string files = "[{\"projectID\":1,\"fileID\":11,\"required\":true},{\"projectID\":2,\"fileID\":12,\"required\":true},{\"projectID\":3,\"fileID\":13,\"required\":true},{\"projectID\":4,\"fileID\":14,\"required\":true},{\"projectID\":5,\"fileID\":15,\"required\":true},{\"projectID\":6,\"fileID\":16,\"required\":true},{\"projectID\":9,\"fileID\":99,\"required\":true}]";
        string pack = Path.Combine(work, "pack.zip");
        Zip(pack, ("manifest.json", Manifest("forge-47.2.0", files: files)), ("overrides/config/pack.toml", "cfg"), ("overrides/options.txt", "opt"));

        var preview = ArchivePreview.Inspect(pack);
        Check(preview.Format == "CurseForge" && preview.Version == "1.20.1" && preview.Loader == "Forge" && preview.LoaderVersion == "47.2.0", "a CurseForge pack is recognised with its Minecraft version and exact loader");
        Check(preview.Downloads == 7 && preview.Mods >= 7 && preview.Note.Contains("API key") && preview.Summary.Contains("CurseForge pack"), "the preview counts the pack's files and says an API key is needed");
        Check(ArchivePreview.Inspect(WriteZip(work, "fabric.zip", Manifest("fabric-0.15.11"))).Loader == "Fabric" && ArchivePreview.Inspect(WriteZip(work, "quilt.zip", Manifest("quilt-0.26.0"))).LoaderVersion == "0.26.0" &&
              ArchivePreview.Inspect(WriteZip(work, "vanilla.zip", Manifest(""))).Loader == "Vanilla", "Fabric, Quilt and loader-free packs are recognised");
        var neoPreview = ArchivePreview.Inspect(WriteZip(work, "neo.zip", Manifest("neoforge-21.1.172")));
        Check(neoPreview.Loader == "NeoForge" && neoPreview.LoaderVersion == "21.1.172", "a CurseForge NeoForge pack is recognised with its exact NeoForge version");
        Reject(() => ArchivePreview.Inspect(WriteZip(work, "odd.zip", Manifest("forge-1", type: "somethingElse"))), "an unrelated manifest.json is not mistaken for a CurseForge pack");
        Reject(() => ArchivePreview.Inspect(WriteZip(work, "slip.zip", Manifest("forge-1", overrides: "../outside"))), "an overrides folder outside the archive is refused");
        string tooMany = "[" + string.Join(",", Enumerable.Range(1, 2001).Select(i => $"{{\"projectID\":{i},\"fileID\":{i}}}")) + "]";
        Reject(() => ArchivePreview.Inspect(WriteZip(work, "many.zip", Manifest("forge-1", files: tooMany))), "a pack listing thousands of files is refused");
        Reject(() => ArchivePreview.Inspect(WriteZip(work, "badfile.zip", Manifest("forge-1", files: "[{\"projectID\":-1,\"fileID\":3}]"))), "an invalid file entry is refused");
        Reject(() => ArchivePreview.Inspect(WriteZip(work, "evil-override.zip", Manifest("forge-1"), ("overrides/../escape.txt", "x"))), "an unsafe path inside the overrides is refused");

        var fixture = new Fixture();
        byte[] goodJar = RandomNumberGenerator.GetBytes(2048), packZip = RandomNumberGenerator.GetBytes(1500), tampered = RandomNumberGenerator.GetBytes(900);
        fixture.Cdn["https://edge.forgecdn.net/files/1/11/good-mod.jar"] = goodJar;
        fixture.Cdn["https://mediafilez.forgecdn.net/files/1/14/nice-pack.zip"] = packZip;
        fixture.Cdn["https://edge.forgecdn.net/files/1/16/tampered.jar"] = tampered;
        using var http = new HttpClient(fixture);
        string key = "test-key-" + Guid.NewGuid().ToString("N");

        var withoutKey = new InstanceManager(Path.Combine(work, "no-key")) { CurseForgeClient = http, DownloadClient = http };
        await RejectAsync(() => withoutKey.ImportArchiveAsync(pack), "importing a CurseForge pack without an API key explains what to do");
        Check(fixture.ApiCalls == 0 && !Directory.Exists(Path.Combine(work, "no-key")), "a missing key stops the import before any request or instance folder exists");

        fixture.Refuse = true;
        var refused = new InstanceManager(Path.Combine(work, "refused")) { CurseForgeApiKey = () => key, CurseForgeClient = http, DownloadClient = http };
        await RejectAsync(() => refused.ImportArchiveAsync(pack), "a key CurseForge refuses is reported");
        Check(!Directory.Exists(Path.Combine(work, "refused")) || !Directory.EnumerateDirectories(Path.Combine(work, "refused")).Any(), "a refused key leaves no half-made instance");
        fixture.Refuse = false;

        // The tampered file's checksum does not match what CurseForge announced: the whole import must fail, not install it.
        var strict = new InstanceManager(Path.Combine(work, "strict")) { CurseForgeApiKey = () => key, CurseForgeClient = http, DownloadClient = http };
        await RejectAsync(() => strict.ImportArchiveAsync(pack), "a downloaded file that fails its SHA-1 check stops the import");

        // Without the tampered entry the rest of the pack imports.
        string cleanFiles = "[{\"projectID\":1,\"fileID\":11},{\"projectID\":2,\"fileID\":12},{\"projectID\":3,\"fileID\":13},{\"projectID\":4,\"fileID\":14},{\"projectID\":5,\"fileID\":15},{\"projectID\":9,\"fileID\":99}]";
        string cleanPack = Path.Combine(work, "clean.zip");
        Zip(cleanPack, ("manifest.json", Manifest("forge-47.2.0", files: cleanFiles)), ("overrides/config/pack.toml", "cfg"), ("overrides/options.txt", "opt"), ("overrides/mods/good-mod.jar", "from the pack itself"));
        var manager = new InstanceManager(Path.Combine(work, "ok")) { CurseForgeApiKey = () => key, CurseForgeClient = http, DownloadClient = http };
        var imported = await manager.ImportArchiveAsync(cleanPack);
        string root = imported.InstancePath;
        Check(fixture.KeySeen == key, "the API key is sent in the x-api-key header");
        Check(imported.Loader == "Forge" && imported.LoaderVersion == "47.2.0" && imported.Version == "1.20.1" && !LaunchSettings.HasOverrides(imported), "the imported instance has the pack's game version and loader and no launch overrides");
        Check(File.ReadAllText(Path.Combine(root, "config", "pack.toml")) == "cfg" && File.ReadAllText(Path.Combine(root, "options.txt")) == "opt", "overrides are copied into the instance");
        Check(File.ReadAllText(Path.Combine(root, "mods", "good-mod.jar")) == "from the pack itself", "a file shipped in the pack's overrides wins over the downloaded copy");
        Check(File.ReadAllBytes(Path.Combine(root, "resourcepacks", "nice-pack.zip")).SequenceEqual(packZip), "a resource pack from CurseForge goes to resourcepacks, verified byte for byte");
        Check(!Directory.EnumerateFiles(root, "*.jar", SearchOption.AllDirectories).Any(f => Path.GetFileName(f) is "blocked.jar" or "evil-host.jar" or "escape.jar") && !File.Exists(Path.Combine(root, "..", "escape.jar")), "blocked, untrusted-host and unsafe-name files are never downloaded");
        string manual = File.ReadAllText(Path.Combine(root, "CurseForge manual downloads.txt"));
        Check(manual.Contains("Blocked Mod") && manual.Contains("does not allow downloads") && manual.Contains("https://www.curseforge.com/minecraft/mc-mods/blocked") && manual.Contains("untrusted download address") &&
              manual.Contains("unsafe file name") && manual.Contains("was not found on CurseForge") && !manual.Contains(key), "files that must be fetched by hand are listed with their pages, and the key is never written down");
        Check(manager.ImportNotes.Count == 1 && manager.ImportNotes[0].Contains("by hand"), "the import reports how many files need manual downloads");
        Check(fixture.CdnCalls > 0 && fixture.Hosts.All(h => h is "edge.forgecdn.net" or "mediafilez.forgecdn.net" or "media.forgecdn.net"), "only CurseForge's own CDN hosts are ever requested for files (calls " + fixture.CdnCalls + ", hosts " + string.Join(",", fixture.Hosts) + ")");

        // Settings: the key persists in the launcher settings file only.
        var settings = new SettingsManager(Path.Combine(work, "settings"));
        var loaded = settings.LoadSettings(); loaded.CurseForgeApiKey = key; settings.SaveSettings(loaded);
        Check(new SettingsManager(Path.Combine(work, "settings")).LoadSettings().CurseForgeApiKey == key, "the CurseForge key is remembered between launches");
        Console.WriteLine("CURSEFORGE_IMPORT_CHECKS_OK");
    }

    private static string WriteZip(string work, string name, string manifest, params (string Path, string Content)[] extra)
    {
        string path = Path.Combine(work, name);
        Zip(path, new[] { ("manifest.json", manifest) }.Concat(extra).ToArray());
        return path;
    }
    private static void Check(bool valid, string message) { if (!valid) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    private static void Reject(Action action, string message)
    {
        bool rejected = false; try { action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException) { rejected = true; }
        Check(rejected, message);
    }
    private static async Task RejectAsync(Func<Task> action, string message)
    {
        bool rejected = false;
        try { await action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or HttpRequestException or OperationCanceledException) { rejected = true; }
        Check(rejected, message);
    }
}
