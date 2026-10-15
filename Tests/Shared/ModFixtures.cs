using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using VortexLauncher.Core.Network;

internal sealed class ModFixture : IDisposable
{
    internal string Root { get; }
    internal ModApi Api { get; } = new();
    internal ModHttp Handler { get; } = new();
    internal HttpClient Http { get; }
    internal ModUpdateService Service { get; }
    internal ModFixture(string parent)
    {
        Root = Directory.CreateDirectory(Path.Combine(parent, "mod-fixture-" + Guid.NewGuid().ToString("N"))).FullName;
        Directory.CreateDirectory(Path.Combine(Root, "mods"));
        Http = new HttpClient(Handler);
        Service = new ModUpdateService(Api, Http, Path.Combine(Root, "cache"));
    }
    internal (ModrinthVersionItem Old, ModrinthVersionItem New) Add(string id, bool disabled = false)
    {
        var old = Version(id, "old", 1); var next = Version(id, "new", 2);
        File.WriteAllBytes(Path.Combine(Root, "mods", old.Files[0].Filename + (disabled ? ".disabled" : "")), Handler.Files[old.Files[0].Url]);
        Api.Identified[old.Files[0].Hashes["sha512"]] = old;
        Api.Projects[id] = new() { old, next };
        return (old, next);
    }
    internal ModrinthVersionItem Version(string project, string suffix, int day, string loader = "fabric", string minecraft = "26.3", string channel = "release")
    {
        string id = project + "-" + suffix;
        byte[] bytes = Jar("{\"id\":\"" + project + "\",\"version\":\"" + suffix + "\"}");
        var file = new ModrinthFileItem { Filename = id + ".jar", Url = "https://cdn.modrinth.com/data/" + project + "/versions/" + id + "/" + id + ".jar", Size = bytes.Length, Primary = true,
            Hashes = new() { ["sha512"] = Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant(), ["sha1"] = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant() } };
        Handler.Files[file.Url] = bytes;
        var version = new ModrinthVersionItem { Id = id, ProjectId = project, Name = id, VersionNumber = suffix,
            DatePublished = new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero), Loaders = new() { loader }, GameVersions = new() { minecraft },
            Files = new() { file }, Changelog = "Fixture release notes: " + suffix, VersionType = channel };
        Api.Versions[id] = version; return version;
    }
    internal static byte[] Jar(string metadata)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open(), Encoding.UTF8)) writer.Write(metadata);
        return stream.ToArray();
    }
    public void Dispose() => Http.Dispose();
}
internal sealed class ModApi : ModrinthService
{
    internal Dictionary<string, ModrinthVersionItem> Identified { get; } = new();
    internal Dictionary<string, List<ModrinthVersionItem>> Projects { get; } = new();
    internal Dictionary<string, ModrinthVersionItem> Versions { get; } = new();
    internal bool Fail { get; set; }
    public override Task<ModrinthVersionItem?> IdentifyForUpdateAsync(string hash, CancellationToken token)
    { token.ThrowIfCancellationRequested(); if (Fail) throw new HttpRequestException("Fixture network failure"); return Task.FromResult(Identified.GetValueOrDefault(hash)); }
    public override Task<List<ModrinthVersionItem>> GetUpdateVersionsAsync(string id, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(Projects[id]); }
    public override Task<ModrinthVersionItem> GetVersionAsync(string id) => Task.FromResult(Versions[id]);
}
internal sealed class ModHttp : HttpMessageHandler
{
    internal Dictionary<string, byte[]> Files { get; } = new();
    internal int Requests;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Requests++;
        return Task.FromResult(Files.TryGetValue(request.RequestUri!.AbsoluteUri, out var bytes)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
