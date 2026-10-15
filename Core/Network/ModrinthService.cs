using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VortexLauncher.Core.Network;

public class ModrinthSearchResult
{
    [JsonPropertyName("hits")]
    public List<ModrinthModItem> Hits { get; set; } = new();

    [JsonPropertyName("total_hits")]
    public int TotalHits { get; set; }
}

public class ModrinthModItem
{
    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("icon_url")]
    public string IconUrl { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;
}

public class ModrinthVersionItem
{
    [JsonPropertyName("changelog")] public string? Changelog { get; set; }
    [JsonPropertyName("version_type")] public string VersionType { get; set; } = "release";
    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = string.Empty;
    [JsonPropertyName("dependencies")]
    public List<ModrinthDependency> Dependencies { get; set; } = new();
    [JsonPropertyName("version_number")]
    public string VersionNumber { get; set; } = string.Empty;

    [JsonPropertyName("date_published")]
    public DateTimeOffset DatePublished { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(VersionNumber) ? Name : VersionNumber;

    [JsonPropertyName("game_versions")]
    public List<string> GameVersions { get; set; } = new();

    [JsonPropertyName("loaders")]
    public List<string> Loaders { get; set; } = new();
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("files")]
    public List<ModrinthFileItem> Files { get; set; } = new();
}

public class ModrinthFileItem
{
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("hashes")]
    public Dictionary<string, string> Hashes { get; set; } = new();
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }
}

public class ModrinthDependency
{
    [JsonPropertyName("project_id")] public string? ProjectId { get; set; }
    [JsonPropertyName("version_id")] public string? VersionId { get; set; }
    [JsonPropertyName("file_name")] public string? FileName { get; set; }
    [JsonPropertyName("dependency_type")] public string Type { get; set; } = string.Empty;
}

public class ModrinthService
{
    private static readonly HttpClient HttpClient = new();

    public ModrinthService()
    {
        if (!HttpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            HttpClient.DefaultRequestHeaders.Add("User-Agent", "VortexLauncher/1.0.0");
        }
    }

    internal static string BuildSearchUrl(string query, string gameVersion, string loader, int offset,
        int limit, string projectType, string? category, string sort)
    {
        var facets = new List<string[]> { new[] { $"project_type:{projectType}" } };
        if (!string.IsNullOrWhiteSpace(gameVersion)) facets.Add(new[] { $"versions:{gameVersion.Trim()}" });
        if (!string.IsNullOrWhiteSpace(loader) && projectType is "mod" or "modpack")
            facets.Add(new[] { $"categories:{loader.ToLowerInvariant()}" });
        if (!string.IsNullOrWhiteSpace(category)) facets.Add(new[] { $"categories:{category}" });
        if (sort is not ("relevance" or "downloads" or "follows" or "newest" or "updated")) sort = "relevance";
        return $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}&index={sort}&offset={offset}&limit={limit}";
    }

    public virtual async Task<ModrinthSearchResult> SearchModsAsync(string query, string gameVersion, string loader,
        int offset = 0, int limit = 20, string projectType = "mod", string? category = null,
        string sort = "relevance", CancellationToken cancellationToken = default)
    {
        string url = BuildSearchUrl(query, gameVersion, loader, offset, limit, projectType, category, sort);
        string json = await HttpClient.GetStringAsync(url, cancellationToken);
        return JsonSerializer.Deserialize<ModrinthSearchResult>(json) ?? new ModrinthSearchResult();
    }

    public Task<ModrinthSearchResult> SearchModpacksAsync(string query, int offset = 0, int limit = 20,
        string? category = null, string sort = "relevance", string gameVersion = "", string loader = "",
        CancellationToken cancellationToken = default) =>
        SearchModsAsync(query, gameVersion, loader, offset, limit, "modpack", category, sort, cancellationToken);

    public virtual async Task<List<ModrinthVersionItem>> GetProjectVersionsAsync(string projectId)
    {
        try
        {
            string url = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}/version";
            string json = await HttpClient.GetStringAsync(url);
            return JsonSerializer.Deserialize<List<ModrinthVersionItem>>(json) ?? new List<ModrinthVersionItem>();
        }
        catch
        {
            return new List<ModrinthVersionItem>();
        }
    }

    public async Task<ModrinthVersionItem?> GetVersionFromHashAsync(string sha512, CancellationToken token = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            using var response = await HttpClient.GetAsync(
                $"https://api.modrinth.com/v2/version_file/{sha512}?algorithm=sha512", timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(timeout.Token);
            return JsonSerializer.Deserialize<ModrinthVersionItem>(json);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException || ex is JsonException || ex is OperationCanceledException)
        {
            return null;
        }
    }

    public virtual async Task<ModrinthVersionItem> GetVersionAsync(string id)
    {
        string json = await HttpClient.GetStringAsync($"https://api.modrinth.com/v2/version/{Uri.EscapeDataString(id)}");
        return JsonSerializer.Deserialize<ModrinthVersionItem>(json) ?? throw new InvalidOperationException("Version metadata is missing.");
    }

    public virtual async Task<string> GetProjectTitleAsync(string id)
    {
        string json = await HttpClient.GetStringAsync($"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(id)}");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("title").GetString() ?? id;
    }

    public virtual async Task<ModrinthVersionItem?> IdentifyInstalledVersionAsync(string hash)
    {
        using var response = await HttpClient.GetAsync($"https://api.modrinth.com/v2/version_file/{hash}?algorithm=sha512");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<ModrinthVersionItem>(await response.Content.ReadAsStringAsync());
    }

    public async Task<string?> GetDownloadUrlAsync(string projectId, string gameVersion, string loader, string projectType = "mod")
    {
        try
        {
            string loaderQuery = string.Equals(projectType, "mod", StringComparison.OrdinalIgnoreCase)
                ? $"&loaders=[\"{(loader ?? string.Empty).ToLowerInvariant()}\"]"
                : string.Empty;
            string url = $"https://api.modrinth.com/v2/project/{projectId}/version?game_versions=[\"{gameVersion}\"]{loaderQuery}";
            string json = await HttpClient.GetStringAsync(url);
            var versions = JsonSerializer.Deserialize<List<ModrinthVersionItem>>(json);

            if (versions != null && versions.Count > 0)
            {
                var files = versions[0].Files;
                if (files == null || files.Count == 0) return null;
                var primaryFile = files.Find(f => f.Primary) ?? files[0];
                return primaryFile?.Url;
            }
        }
        catch { }

        return null;
    }

    public async Task<string?> FindLatestDownloadUrlAsync(string projectName, string gameVersion, string loader)
    {
        var result = await SearchModsAsync(projectName, gameVersion, loader, 0, 5, "mod");
        var project = result.Hits
            .OrderByDescending(x => string.Equals(x.Title, projectName, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        return project == null
            ? null
            : await GetDownloadUrlAsync(project.ProjectId, gameVersion, loader, "mod");
    }

    // Unlike the legacy browser method, update discovery must surface network
    // failures rather than calling an empty response "everything up to date".
    public virtual async Task<List<ModrinthVersionItem>> GetUpdateVersionsAsync(string projectId, CancellationToken token)
    {
        string json = await HttpClient.GetStringAsync($"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}/version", token);
        return JsonSerializer.Deserialize<List<ModrinthVersionItem>>(json) ?? throw new JsonException("Missing version list.");
    }

    public virtual async Task<ModrinthVersionItem?> IdentifyForUpdateAsync(string hash, CancellationToken token)
    {
        using var response = await HttpClient.GetAsync($"https://api.modrinth.com/v2/version_file/{hash}?algorithm=sha512", token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<ModrinthVersionItem>(await response.Content.ReadAsStringAsync(token));
    }
}
