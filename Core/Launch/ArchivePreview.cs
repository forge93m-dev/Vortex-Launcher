using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using VortexLauncher.Models;

namespace VortexLauncher.Core.Launch;

public sealed record ArchivePreview(string Fingerprint, string Format, string Name, string Version,
    string Loader, string LoaderVersion, int Mods, int ResourcePacks, int ShaderPacks, int Worlds,
    int Files, long Bytes, int Downloads, string Note = "")
{
    public string Summary => $"{Name}\nMinecraft {Version} · {Loader} {LoaderVersion}\nFormat: {Format}\n\n" +
        $"Mods: {Mods}\nResource packs: {ResourcePacks}\nShader packs: {ShaderPacks}\nWorlds: {Worlds}\n" +
        $"{Files:N0} archived files · {Bytes / 1048576d:N1} MB unpacked\n{Downloads} additional files to download\n\n" +
        (Note.Length > 0 ? Note + "\n\n" : "") +
        "A new, independent instance will be created. Existing instances will not be overwritten.";

    public static ArchivePreview Inspect(string path)
    {
        using var stream = File.OpenRead(path);
        string fingerprint = Convert.ToHexString(SHA256.HashData(stream));
        stream.Position = 0;
        using var archive = new ZipArchive(stream);
        var plan = ArchiveImportPlan.Read(archive, Path.GetFileNameWithoutExtension(path));
        var paths = plan.ContentPaths;
        int curseForge = plan.CurseForge?.Files.Count ?? 0;
        return new(fingerprint, plan.Format, plan.Instance.Name, plan.Instance.Version, plan.Instance.Loader,
            plan.Instance.LoaderVersion, curseForge + paths.Count(p => p.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)),
            paths.Count(p => p.StartsWith("resourcepacks/", StringComparison.OrdinalIgnoreCase)),
            paths.Count(p => p.StartsWith("shaderpacks/", StringComparison.OrdinalIgnoreCase)),
            paths.Where(p => p.StartsWith("saves/", StringComparison.OrdinalIgnoreCase)).Select(p => p.Split('/').ElementAtOrDefault(1)).Distinct().Count(),
            archive.Entries.Count(e => !e.FullName.EndsWith('/')), archive.Entries.Sum(e => e.Length), plan.Downloads.Count + curseForge,
            curseForge == 0 ? "" : $"CurseForge pack: its {curseForge} files are downloaded through CurseForge with your own free API key (Settings → Java → CurseForge). " +
                "Files whose authors block other launchers are listed in a text file for you to download by hand.");
    }
}

internal sealed record ImportDownload(string Path, string Url, string Sha1, long Size);

/// <summary>A CurseForge pack: only (project, file) ids; the files themselves are looked up through the CurseForge API.</summary>
internal sealed record CurseForgePack(IReadOnlyList<(int Project, int File)> Files);

internal sealed class ArchiveImportPlan
{
    public string Format { get; private set; } = "";
    public InstanceModel Instance { get; private set; } = new();
    public PolyMcArchive? PolyMc { get; private set; }
    public CurseForgePack? CurseForge { get; private set; }
    public List<(ZipArchiveEntry Entry, string Path)> Entries { get; } = new();
    public List<ImportDownload> Downloads { get; } = new();
    public IReadOnlyList<string> ContentPaths => PolyMc?.ContentPaths ?? Entries.Select(e => e.Path).Concat(Downloads.Select(d => d.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static ArchiveImportPlan Read(ZipArchive archive, string fallback)
    {
        if (archive.Entries.Count > 200000 || archive.Entries.Sum(e => e.Length) > 50L * 1024 * 1024 * 1024)
            throw new InvalidDataException("This archive is too large to import safely (limit: 200,000 entries / 50 GB).");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            string path = entry.FullName.Replace('\\', '/').TrimEnd('/');
            ValidatePath(path);
            if (!names.Add(path)) throw new InvalidDataException($"Duplicate archive path: {path}");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & 0x400) != 0)
                throw new InvalidDataException("Linked files are not supported in archives.");
        }
        var plan = new ArchiveImportPlan();
        if (archive.GetEntry("instance.json") is { } vortex)
        {
            using var document = ReadJson(vortex);
            if (!document.RootElement.EnumerateObject().Any(p => p.Name.Equals("Loader", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The exported instance has no loader metadata. Export it again with the latest launcher.");
            plan.Instance = document.RootElement.Deserialize<InstanceModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Invalid Vortex instance metadata.");
            plan.Format = "Vortex";
            foreach (var entry in archive.Entries.Where(e => !e.FullName.EndsWith('/')))
            {
                string path = entry.FullName.Replace('\\', '/');
                if (path is "instance.json" or ".vortex-importing" or ".vortex-cloning" or ".vortex-installation-pending" or ".vortex-installation-complete") continue;
                plan.Entries.Add((entry, path));
            }
        }
        else if (PolyMcArchive.Read(archive, fallback) is { } poly)
        {
            plan.Format = "PolyMC / MultiMC / Prism";
            plan.Instance = poly.Instance;
            plan.PolyMc = poly;
        }
        else if (archive.GetEntry("modrinth.index.json") is { } index)
        {
            plan.Format = "Modrinth";
            using var document = ReadJson(index);
            var root = document.RootElement;
            if (!root.TryGetProperty("game", out var game) || game.GetString() != "minecraft")
                throw new InvalidDataException("Only Minecraft Modrinth packs are supported.");
            var deps = root.GetProperty("dependencies");
            plan.Instance = new() { Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? fallback : fallback,
                Version = deps.GetProperty("minecraft").GetString() ?? "" };
            int loaders = 0;
            foreach (var dependency in deps.EnumerateObject().Where(p => p.Name != "minecraft"))
            {
                plan.Instance.Loader = dependency.Name switch { "fabric-loader" => "Fabric", "quilt-loader" => "Quilt", "forge" => "Forge", "neoforge" => "NeoForge",
                    _ => throw new InvalidDataException($"Unsupported pack loader: {dependency.Name}") };
                plan.Instance.LoaderVersion = dependency.Value.GetString() ?? "";
                loaders++;
            }
            if (loaders > 1) throw new InvalidDataException("The pack specifies conflicting loaders.");
            foreach (var file in root.GetProperty("files").EnumerateArray())
            {
                if (file.TryGetProperty("env", out var env) && env.TryGetProperty("client", out var client) && client.GetString() == "unsupported") continue;
                string path = file.GetProperty("path").GetString() ?? "";
                ValidateContentPath(path);
                string url = file.GetProperty("downloads").EnumerateArray().Select(u => u.GetString()).FirstOrDefault(IsAllowedDownload)
                    ?? throw new InvalidDataException($"No trusted HTTPS download for {path}.");
                string hash = file.GetProperty("hashes").GetProperty("sha1").GetString() ?? "";
                if (!Regex.IsMatch(hash, "^[a-fA-F0-9]{40}$")) throw new InvalidDataException($"Missing SHA-1 for {path}.");
                long size = file.GetProperty("fileSize").GetInt64();
                if (size <= 0 || size > 2L * 1024 * 1024 * 1024) throw new InvalidDataException($"Invalid size for {path}.");
                if (plan.Downloads.Any(d => d.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException($"Duplicate pack file: {path}");
                plan.Downloads.Add(new(path, url, hash, size));
            }
            foreach (string prefix in new[] { "overrides/", "client-overrides/" })
            foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal) && !e.FullName.EndsWith('/')))
            {
                string path = entry.FullName[prefix.Length..];
                ValidateContentPath(path);
                plan.Entries.RemoveAll(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
                plan.Entries.Add((entry, path));
            }
        }
        else if (archive.GetEntry("manifest.json") is { } curseManifest && ReadCurseForge(plan, archive, curseManifest, fallback)) { }
        else throw new InvalidDataException("Unsupported archive. Choose a Vortex, PolyMC/MultiMC/Prism ZIP, Modrinth .mrpack or CurseForge pack.");
        ValidatePath(plan.Instance.Version);
        if (plan.Instance.Version.Contains('/') || plan.Instance.Version.Contains('\\')) throw new InvalidDataException("Invalid Minecraft version.");
        if (!string.IsNullOrEmpty(plan.Instance.LoaderVersion))
        {
            ValidatePath(plan.Instance.LoaderVersion);
            if (plan.Instance.LoaderVersion.Contains('/') || plan.Instance.LoaderVersion.Contains('\\')) throw new InvalidDataException("Invalid loader version.");
        }
        if (string.IsNullOrWhiteSpace(plan.Instance.Name)) plan.Instance.Name = fallback;
        if (plan.Instance.Loader is not ("Vanilla" or "Fabric" or "Forge" or "NeoForge" or "Quilt")) throw new InvalidDataException("Unsupported loader in the archive.");
        // A shared pack or archive never brings its own Java, memory or arguments: they would run on this computer.
        LaunchSettings.RejectImported(plan.Instance);
        // Discard machine-specific absolute artwork paths from another computer.
        plan.Instance.CoverImagePath = "";
        plan.Instance.IconPath = "";
        return plan;
    }

    internal const int MaxCurseForgeFiles = 2000;

    /// <summary>CurseForge export (manifest.json): game version, loader, file ids and an overrides folder. False when it is some other manifest.json.</summary>
    private static bool ReadCurseForge(ArchiveImportPlan plan, ZipArchive archive, ZipArchiveEntry manifest, string fallback)
    {
        using var document = ReadJson(manifest);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("manifestType", out var type) || type.GetString() != "minecraftModpack") return false;
        plan.Format = "CurseForge";
        var game = root.TryGetProperty("minecraft", out var minecraft) && minecraft.ValueKind == JsonValueKind.Object ? minecraft : throw new InvalidDataException("The CurseForge pack has no Minecraft section.");
        plan.Instance = new() { Name = root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? fallback : fallback,
            Version = game.TryGetProperty("version", out var version) ? version.GetString() ?? "" : "" };
        string loader = "";
        if (game.TryGetProperty("modLoaders", out var loaders) && loaders.ValueKind == JsonValueKind.Array && loaders.GetArrayLength() > 0)
        {
            var chosen = loaders.EnumerateArray().FirstOrDefault(l => l.TryGetProperty("primary", out var p) && p.ValueKind == JsonValueKind.True);
            if (chosen.ValueKind != JsonValueKind.Object) chosen = loaders[0];
            loader = chosen.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
        }
        int dash = loader.IndexOf('-');
        string loaderName = dash > 0 ? loader[..dash] : loader;
        plan.Instance.Loader = loaderName.ToLowerInvariant() switch
        {
            "" => "Vanilla", "forge" => "Forge", "fabric" => "Fabric", "quilt" => "Quilt",
            "neoforge" => "NeoForge",
            _ => throw new InvalidDataException($"Unsupported pack loader: {loaderName}")
        };
        plan.Instance.LoaderVersion = dash > 0 ? loader[(dash + 1)..] : "";
        var files = new List<(int, int)>();
        if (root.TryGetProperty("files", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            if (list.GetArrayLength() > MaxCurseForgeFiles) throw new InvalidDataException($"The pack lists more than {MaxCurseForgeFiles} files.");
            foreach (var file in list.EnumerateArray())
            {
                if (!file.TryGetProperty("projectID", out var project) || !project.TryGetInt32(out int projectId) || projectId <= 0 ||
                    !file.TryGetProperty("fileID", out var fileId) || !fileId.TryGetInt32(out int fileValue) || fileValue <= 0)
                    throw new InvalidDataException("The CurseForge pack has an invalid file entry.");
                if (!files.Contains((projectId, fileValue))) files.Add((projectId, fileValue));
            }
        }
        plan.CurseForge = new CurseForgePack(files);
        string overrides = root.TryGetProperty("overrides", out var folder) && folder.ValueKind == JsonValueKind.String ? folder.GetString() ?? "overrides" : "overrides";
        ValidatePath(overrides);
        string prefix = overrides + "/";
        foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal) && !e.FullName.EndsWith('/')))
        {
            string path = entry.FullName[prefix.Length..];
            ValidateContentPath(path);
            plan.Entries.Add((entry, path));
        }
        return true;
    }

    private static JsonDocument ReadJson(ZipArchiveEntry entry)
    {
        if (entry.Length > 8 * 1024 * 1024) throw new InvalidDataException("Archive metadata is too large.");
        using var stream = entry.Open();
        return JsonDocument.Parse(stream);
    }

    private static bool IsAllowedDownload(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        uri.Host is "cdn.modrinth.com" or "github.com" or "raw.githubusercontent.com";

    internal static void ValidateContentPath(string path)
    {
        ValidatePath(path);
        if (path.Split('/')[0].StartsWith(".vortex", StringComparison.OrdinalIgnoreCase) || path.Equals("instance.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The pack contains reserved launcher metadata.");
    }

    internal static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains('\\') ||
            path.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
                part.IndexOfAny(new[] { ':', '*', '?', '"', '<', '>', '|' }) >= 0 || part.Any(char.IsControl) ||
                Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase)))
            throw new InvalidDataException($"Unsafe archive path: {path}");
    }
}
