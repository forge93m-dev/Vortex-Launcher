using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VortexLauncher.Core.Network;

/// <summary>
/// The official CurseForge API, used only to turn the (project, file) pairs of a CurseForge pack into download
/// addresses. It needs the user's own free API key; no key is built into the launcher.
/// </summary>
public sealed class CurseForgeService
{
    public const string ApiBase = "https://api.curseforge.com/v1/";
    public const int ChunkSize = 400;
    public const long MaxResponseBytes = 16L * 1024 * 1024;
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(45) };

    /// <summary>A file of a pack after lookup. <see cref="DownloadUrl"/> is null when its author blocks other launchers.</summary>
    public sealed record ResolvedFile(int ProjectId, int FileId, string FileName, string? DownloadUrl, string Sha1, long Size,
        int ClassId, string ProjectName, string? Website);

    private readonly string _key;
    private readonly HttpClient _client;

    public CurseForgeService(string apiKey, HttpClient? client = null)
    {
        _key = apiKey.Trim();
        _client = client ?? SharedClient;
    }

    /// <summary>Hosts CurseForge serves mod files from; anything else is never downloaded.</summary>
    public static bool IsTrustedDownload(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        uri.Host is "edge.forgecdn.net" or "mediafilez.forgecdn.net" or "media.forgecdn.net";

    public async Task<IReadOnlyList<ResolvedFile>> ResolveAsync(IReadOnlyList<(int Project, int File)> files, CancellationToken cancellationToken)
    {
        var result = new List<ResolvedFile>();
        var projects = new Dictionary<int, (int ClassId, string Name, string? Website)>();
        var byFile = new Dictionary<int, JsonElement>();
        foreach (var chunk in files.Select(f => f.File).Distinct().Chunk(ChunkSize))
        {
            using var document = await PostAsync("mods/files", new { fileIds = chunk }, cancellationToken);
            foreach (var item in document.RootElement.GetProperty("data").EnumerateArray())
                if (item.TryGetProperty("id", out var id) && id.TryGetInt32(out int fileId)) byFile[fileId] = item.Clone();
        }
        foreach (var chunk in files.Select(f => f.Project).Distinct().Chunk(ChunkSize))
        {
            try
            {
                using var document = await PostAsync("mods", new { modIds = chunk }, cancellationToken);
                foreach (var item in document.RootElement.GetProperty("data").EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var id) || !id.TryGetInt32(out int projectId)) continue;
                    string? website = item.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Object && links.TryGetProperty("websiteUrl", out var site) ? site.GetString() : null;
                    projects[projectId] = (item.TryGetProperty("classId", out var cls) && cls.TryGetInt32(out int classId) ? classId : 0,
                        item.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "", website);
                }
            }
            catch (HttpRequestException) { /* names and categories are optional: files are still resolved */ }
        }
        foreach (var (project, file) in files)
        {
            projects.TryGetValue(project, out var info);
            if (!byFile.TryGetValue(file, out var item)) { result.Add(new(project, file, "", null, "", 0, info.ClassId, info.Name ?? "", info.Website)); continue; }
            string name = item.TryGetProperty("fileName", out var fileName) ? fileName.GetString() ?? "" : "";
            string? url = item.TryGetProperty("downloadUrl", out var download) && download.ValueKind == JsonValueKind.String ? download.GetString() : null;
            long size = item.TryGetProperty("fileLength", out var length) && length.TryGetInt64(out long l) ? l : 0;
            string sha1 = "";
            if (item.TryGetProperty("hashes", out var hashes) && hashes.ValueKind == JsonValueKind.Array)
                foreach (var hash in hashes.EnumerateArray())
                    if (hash.TryGetProperty("algo", out var algo) && algo.TryGetInt32(out int a) && a == 1) sha1 = hash.GetProperty("value").GetString() ?? "";
            result.Add(new(project, file, name, url, sha1, size, info.ClassId, info.Name ?? "", info.Website));
        }
        return result;
    }

    private async Task<JsonDocument> PostAsync(string path, object body, CancellationToken cancellationToken)
    {
        if (_key.Length == 0) throw new InvalidOperationException("A CurseForge API key is required.");
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiBase + path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("x-api-key", _key);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new InvalidOperationException("CurseForge refused the API key. Check it in Settings.");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxResponseBytes) throw new InvalidDataException("CurseForge sent an unexpectedly large answer.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new MemoryStream();
        var chunk = new byte[81920]; int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes) throw new InvalidDataException("CurseForge sent an unexpectedly large answer.");
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;
        return await JsonDocument.ParseAsync(buffer, cancellationToken: cancellationToken);
    }
}
