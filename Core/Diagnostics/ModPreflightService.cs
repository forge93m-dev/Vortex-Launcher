using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VortexLauncher.Core.Management;

namespace VortexLauncher.Core.Diagnostics;

public sealed record ModCheckIssue(bool IsError, string Code, string File, string Message, string Advice)
{
    public bool IsInformational => !IsError && Code is "version-unverified" or "conflict-unverified";
    public string Heading => (IsError ? "ERROR · " : IsInformational ? "INFO · " : "WARNING · ") + Message;
}
public sealed record ModPreflightReport(IReadOnlyList<ModCheckIssue> Issues, int Checked, int Disabled, int Unknown)
{
    public bool HasErrors => Issues.Any(i => i.IsError);
    public bool NeedsReview => Issues.Any(i => !i.IsInformational);
    public string Headline => HasErrors ? "Fix mod errors before launching" : NeedsReview ? "Review mod warnings" : "No known mod problems found";
    public string Counts => $"{Issues.Count(i => i.IsError)} error(s) · {Issues.Count(i => !i.IsError && !i.IsInformational)} warning(s) · {Issues.Count(i => i.IsInformational)} informational · {Checked} JAR(s) checked · {Disabled} disabled · {Unknown} unverified";
    public string Notes => "Offline metadata check, not a guarantee against crashes. Fabric resolves optional bundled alternatives and unsupported version rules. Informational findings do not interrupt launch; known top-level errors still block it.";
    public string Summary => Headline + "\n" + Counts + "\n\n" + string.Join("\n\n", Issues.Select(i => $"{i.Heading}\n{i.File}\n{i.Advice}")) + "\n\n" + Notes;
}

/// <summary>Read-only, bounded, offline metadata preflight. Not a replacement for a loader/SAT solver.</summary>
public sealed class ModPreflightService
{
    private sealed class InspectionLimitException(string message) : IOException(message);
    private sealed record Node(string Id, string Version, string File, bool Nested, string[] Provides, JsonElement Metadata);
    public Task<ModPreflightReport> CheckAsync(string root, string minecraft, string loader, string? loaderVersion = null,
        CancellationToken token = default, bool externalOverrides = false) => Task.Run(() => Check(root, minecraft, loader, loaderVersion, token, externalOverrides), token);

    private static ModPreflightReport Check(string root, string minecraft, string loader, string? loaderVersion, CancellationToken token, bool externalOverrides)
    {
        var issues = new List<ModCheckIssue>(); var nodes = new List<Node>();
        int checkedFiles = 0, disabled = 0, unknown = 0, inspected = 0;
        long nestedBytes = 0, metadataBytes = 0;
        int totalEntries = 0, ruleWork = 0;
        bool incomplete = false;
        bool fabric = loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase);
        bool overrides = externalOverrides || File.Exists(Path.Combine(root, "config", "fabric_loader_dependencies.json"));
        void Add(bool error, string code, string file, string message, string advice)
        {
            if (issues.Count < 1000) issues.Add(new(error, code, Clean(file), Clean(message), Clean(advice)));
        }
        byte[] Read(ZipArchiveEntry entry, int limit)
        {
            if (entry.Length > limit) throw new InspectionLimitException("Archive entry exceeds the inspection limit.");
            using var stream = entry.Open(); using var output = new MemoryStream();
            byte[] buffer = new byte[16384]; int count;
            while ((count = stream.Read(buffer, 0, Math.Min(buffer.Length, limit + 1 - (int)output.Length))) != 0)
            {
                token.ThrowIfCancellationRequested(); output.Write(buffer, 0, count);
                if (output.Length > limit) throw new InspectionLimitException("Archive entry exceeds the inspection limit.");
            }
            return output.ToArray();
        }
        void Inspect(Stream stream, string file, bool nested, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (++inspected > 4096 || depth > 4) { incomplete = true; throw new InspectionLimitException("Nested mod inspection limit reached."); }
            // Read the small EOCD first: do not instantiate millions of central-directory
            // objects just to discover that a ZIP/ZIP64 file is beyond our scan budget.
            if (stream.CanSeek)
            {
                long originalPosition = stream.Position;
                int size = (int)Math.Min(stream.Length, 65557);
                byte[] tail = new byte[size]; stream.Position = stream.Length - size; stream.ReadExactly(tail); stream.Position = originalPosition;
                for (int p = size - 22; p >= 0; p--)
                {
                    if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(p, 4)) != 0x06054b50 || p + 22 + BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(p + 20, 2)) != size) continue;
                    int entries = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(p + 10, 2));
                    if (entries > 20000 || (totalEntries += entries) > 200000) throw new InspectionLimitException("Archive directory inspection limit reached (including ZIP64).");
                    break;
                }
            }
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > 20000) throw new InspectionLimitException("Too many entries in the mod archive.");
            var metadata = archive.GetEntry("fabric.mod.json");
            if (metadata == null)
            {
                if (nested) { incomplete = true; return; }
                unknown++; incomplete = true;
                var declared = new List<string>();
                if (archive.GetEntry("quilt.mod.json") != null) declared.Add("Quilt");
                if (archive.GetEntry("META-INF/mods.toml") != null || archive.GetEntry("mcmod.info") != null) declared.Add("Forge");
                if (archive.GetEntry("META-INF/neoforge.mods.toml") != null) declared.Add("NeoForge");
                Add(false, "unverified", file, declared.Count > 0 ? $"Declares {string.Join(" / ", declared)} metadata" : "No supported mod metadata found",
                    declared.Count > 0 ? $"This instance uses {loader}. Full dependency validation is currently available for Fabric only; verify this file before launch." : "May be a library or a different kind of archive. No compatibility was guessed.");
                return;
            }
            if (metadataBytes + metadata.Length > 16777216) throw new InspectionLimitException("Total metadata inspection limit reached.");
            byte[] document = Read(metadata, 524288); metadataBytes += document.Length;
            using var doc = JsonDocument.Parse(document, new JsonDocumentOptions { MaxDepth = 48 });
            var json = doc.RootElement;
            if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("schemaVersion", out var schema) || !schema.TryGetInt32(out int format) || format != 1)
                throw new JsonException("Unsupported Fabric metadata schema.");
            string id = Text(json, "id"), version = Text(json, "version"), environment = Text(json, "environment");
            if (!Regex.IsMatch(id, @"\A[a-z][a-z0-9_-]{1,63}\z") || version.Length is 0 or > 128) throw new JsonException("Invalid mod ID or version.");
            if (environment == "server")
            {
                if (!nested) Add(false, "server-only", file, "Server-only mod is ignored by the client", "Move it to the dedicated server if needed. It does not satisfy client dependencies.");
                if (json.TryGetProperty("jars", out _)) incomplete = true;
                return;
            }
            if (environment is not ("" or "*" or "client")) throw new JsonException("Unknown mod environment.");
            string[] provides = json.TryGetProperty("provides", out var aliases) && aliases.ValueKind == JsonValueKind.Array
                ? aliases.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString()!).Where(a => Regex.IsMatch(a, @"\A[a-z][a-z0-9_-]{1,63}\z")).Take(128).ToArray() : Array.Empty<string>();
            nodes.Add(new(id, version, file, nested, provides, json.Clone()));
            if (!json.TryGetProperty("jars", out var jars)) return;
            if (jars.ValueKind != JsonValueKind.Array) throw new JsonException("Invalid nested JAR list.");
            foreach (var item in jars.EnumerateArray())
            {
                token.ThrowIfCancellationRequested();
                string name = Text(item, "file");
                if (name.Length is 0 or > 240 || name.StartsWith('/') || name.Contains('\\') || name.Split('/').Any(p => p is ".." or "."))
                { incomplete = true; Add(false, "nested-unverified", file, "Nested archive path could not be inspected", "Verify this mod's original download. Nothing was extracted."); continue; }
                var entry = archive.GetEntry(name);
                if (entry == null) { incomplete = true; Add(false, "nested-missing", file, "Declared nested archive is missing", "Re-download the original mod; its bundled library could not be checked."); continue; }
                try
                {
                    if (entry.Length > 16777216 || nestedBytes + entry.Length > 67108864) throw new InvalidDataException("Nested JAR size limit reached.");
                    byte[] bytes = Read(entry, 16777216); nestedBytes += bytes.Length;
                    using var child = new MemoryStream(bytes, writable: false);
                    Inspect(child, file + " → " + name, true, depth + 1);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
                { incomplete = true; Add(false, "nested-unverified", file, "A bundled library could not be fully checked", ex.Message); }
            }
        }
        string mods = Path.Combine(root, "mods");
        try
        {
            ModFiles.RejectLinks(mods);
            if (!Directory.Exists(mods)) return new(issues, 0, 0, 0);
            var files = Directory.EnumerateFiles(mods).Take(4097).OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
            if (files.Length > 4096) { incomplete = true; Add(false, "limit", "mods", "Only part of the mods folder was inspected", "There are too many files. Fabric remains the final authority on compatibility."); }
            foreach (var path in files.Take(4096))
            {
                token.ThrowIfCancellationRequested();
                if (path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)) { disabled++; continue; }
                if (!path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) continue;
                checkedFiles++;
                try
                {
                    ModFiles.RejectLinks(path);
                    using var stream = File.OpenRead(path);
                    if (stream.Length > 536870912) throw new IOException("JAR exceeds the inspection size limit.");
                    Inspect(stream, Path.GetFileName(path), false, 0);
                }
                catch (InvalidDataException)
                { incomplete = true; Add(!loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase), "broken-jar", Path.GetFileName(path), "Cannot open this JAR archive", "Re-download the mod or disable it. Its archive structure is unreadable."); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
                { incomplete = true; unknown++; Add(false, "unverified", Path.GetFileName(path), "Could not fully inspect this mod", ex.Message + " No incompatibility was assumed."); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { incomplete = true; unknown++; Add(false, "folder-unverified", "mods", "Mods folder could not be inspected", ex.Message); }
        if (overrides) Add(false, "overrides", "config", "Custom dependency rules are present", "Missing/version/conflict findings are warnings because custom overrides or extra mod paths can change resolution.");
        if (!fabric)
        {
            if (nodes.Count > 0) Add(false, "loader-unverified", "mods", $"Fabric metadata found in a {loader} instance", "Multi-loader archives and compatibility bridges may work. Fabric dependencies were not treated as hard errors for this loader.");
            return new(issues, checkedFiles, disabled, unknown);
        }
        // Fabric Loader itself can supply mods (e.g. MixinExtras). Read only the
        // selected local loader, not arbitrary libraries which may never be loaded.
        if (loaderVersion != null && Regex.IsMatch(loaderVersion, @"\A[A-Za-z0-9_.+-]{1,96}\z"))
        {
            string loaderJar = Path.Combine(root, "libraries", "fabric-loader-" + loaderVersion + ".jar");
            if (File.Exists(loaderJar))
            {
                try
                {
                    ModFiles.RejectLinks(loaderJar);
                    using var input = File.OpenRead(loaderJar);
                    if (input.Length > 33554432) throw new InspectionLimitException("Loader metadata inspection limit reached.");
                    Inspect(input, "Fabric Loader " + loaderVersion, true, 0);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or InvalidOperationException)
                { incomplete = true; Add(false, "loader-unverified", "libraries", "Loader-provided mods could not be checked", ex.Message); }
            }
        }
        foreach (var duplicate in nodes.Where(n => !n.Nested).GroupBy(n => n.Id).Where(g => g.Count() > 1))
            Add(true, "duplicate-id", string.Join("; ", duplicate.Select(n => n.File)), $"Multiple enabled mods use ID '{duplicate.Key}'", "Keep only one version enabled. Disabled files are not counted; bundled libraries are not treated as duplicate top-level mods.");
        var index = nodes.SelectMany(n => n.Provides.Append(n.Id).Distinct(StringComparer.Ordinal).Select(id => (Id: id, Node: n)))
            .GroupBy(p => p.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(p => p.Node).ToArray(), StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            token.ThrowIfCancellationRequested();
            // Bundled JARs are optional candidates, not all active mods. Only
            // Fabric can choose their versions and resolve their dependencies.
            // Keep them indexed as providers, but do not warn about inactive alternatives.
            if (node.Nested) continue;
            foreach (string kind in new[] { "depends", "breaks", "conflicts" })
            {
                if (!node.Metadata.TryGetProperty(kind, out var rules)) continue;
                if (rules.ValueKind != JsonValueKind.Object) { Add(false, "rule-unverified", node.File, "Invalid dependency metadata", "Fabric must validate this mod's dependency rules."); continue; }
                if (rules.EnumerateObject().Skip(256).Any()) Add(false, "rule-limit", node.File, "Only the first 256 rules in this section were inspected", "Fabric remains responsible for the remaining dependency rules.");
                foreach (var rule in rules.EnumerateObject().Take(256))
                {
                    string id = rule.Name; string wanted = rule.Value.ToString();
                    var candidates = index.GetValueOrDefault(id) ?? Array.Empty<Node>();
                    if ((ruleWork += 1 + candidates.Length) > 200000)
                    {
                        Add(false, "dependency-limit", "mods", "Only part of the dependency graph was inspected", "The scan budget was reached. Fabric must resolve the remaining rules.");
                        return new(issues.OrderByDescending(i => i.IsError).ThenBy(i => i.File, StringComparer.Ordinal).ToArray(), checkedFiles, disabled, unknown);
                    }
                    var mandatory = candidates.Where(n => !n.Nested && n.Id == id).ToArray();
                    if (mandatory.Length > 0) candidates = mandatory;
                    bool loaderProvidedUnverified = id == "mixinextras" && candidates.Length == 0;
                    bool builtin = id is "minecraft" or "fabricloader" or "java" || loaderProvidedUnverified;
                    bool? match = null;
                    if (loaderProvidedUnverified) match = null;
                    else if (builtin) match = ModVersionRange.Matches(id == "minecraft" ? minecraft : id == "fabricloader" ? loaderVersion : null, rule.Value);
                    else if (candidates.Length > 0)
                    {
                        var matches = candidates.Select(n => ModVersionRange.Matches(n.Id == id ? n.Version : null, rule.Value)).ToArray();
                        match = matches.Any(m => m == true) ? true : matches.Any(m => m == null) ? null : false;
                    }
                    bool missing = !builtin && candidates.Length == 0;
                    bool certain = !node.Nested && !overrides;
                    if (kind == "depends" && (missing || match == false))
                        Add(certain && (!incomplete || builtin || mandatory.Length > 0), missing ? "missing-dependency" : "wrong-version", node.File,
                            missing ? $"Missing required mod '{id}'" : $"'{id}' does not match the required version",
                            $"{node.Id} requires {id} {wanted}. " + (missing ? "Install a compatible version or disable the mod that requires it." : "Installed: " + (builtin ? (id == "minecraft" ? minecraft : loaderVersion) : string.Join(", ", candidates.Select(n => n.Version))) + ". Use matching versions."));
                    else if (kind == "depends" && match == null && !missing && !(id == "java" || id == "fabricloader" && string.IsNullOrWhiteSpace(loaderVersion)))
                        Add(false, "version-unverified", node.File, $"Could not verify '{id}' version rule", $"Required: {wanted}. The version, provider alias or predicate needs the loader's resolver; launch is not blocked.");
                    else if (kind != "depends" && !missing && match == true)
                        Add(kind == "breaks" && certain && (builtin || mandatory.Length > 0), "declared-conflict", node.File,
                            $"Declares {kind} with '{id}'", $"Matching rule: {wanted}. Check the two mods' compatible releases or disable one. Optional bundled alternatives are warnings only.");
                    else if (kind != "depends" && !missing && match == null)
                        Add(false, "conflict-unverified", node.File, $"Could not verify a {kind} rule for '{id}'", $"Rule: {wanted}. Review this combination if the loader reports a problem.");
                }
            }
        }
        return new(issues.OrderByDescending(i => i.IsError).ThenBy(i => i.File, StringComparer.Ordinal).ToArray(), checkedFiles, disabled, unknown);
    }
    private static string Text(JsonElement json, string name) => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    private static string Clean(string text) => new(text.Take(2400).Select(c => char.IsControl(c) ? ' ' : c).ToArray());
}
