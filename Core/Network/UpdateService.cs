using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using VortexLauncher.Core.Launch;

namespace VortexLauncher.Core.Network;

public sealed record UpdateCheckResult(LauncherUpdate? Update, string Message);

public sealed class UpdateService
{
    private static readonly HttpClient DefaultClient = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly HttpClient _client;
    public string CurrentVersion { get; }
    private readonly string _runtimeId;
    private readonly IReadOnlyDictionary<string, string> _trustedKeys;
    public UpdateService() : this(DefaultClient, typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.1.0", PlatformSupport.RuntimeId) { }
    internal UpdateService(HttpClient client, string currentVersion, string runtimeId, IReadOnlyDictionary<string, string>? trustedKeys = null)
    { _client = client; CurrentVersion = currentVersion; _runtimeId = runtimeId; _trustedKeys = trustedKeys ?? UpdateManifestSignature.ProductionKeys; }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(string manifestUrl, CancellationToken token = default)
    {
        if (!LauncherUpdate.IsHttps(manifestUrl)) return new(null, "The update source must be a valid HTTPS URL.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var request = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 1048576) throw new InvalidDataException("Update manifest is too large.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(bytes, timeout.Token)) > 0)
            {
                if (buffer.Length + read > 1048576) throw new InvalidDataException("Update manifest is too large.");
                buffer.Write(bytes, 0, read);
            }
            var update = JsonSerializer.Deserialize<LauncherUpdate>(buffer.ToArray(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (update == null || !Version.TryParse(update.Version, out var remote) || remote.Build < 0)
                return new(null, "The publisher's update version is invalid.");
            if (!Version.TryParse(CurrentVersion, out var current)) return new(null, "The installed launcher version could not be read.");
            if (remote <= current) return new(null, $"Vortex Launcher {CurrentVersion} is up to date.");
            // Only a feed signed by the publisher's release key may offer an update.
            if (!UpdateManifestSignature.Verify(update, _trustedKeys))
                return new(null, $"Version {update.Version} was found, but the update feed is not signed by the Vortex Launcher publisher. The update was not offered.");
            if (update.GetDownloadUrl(_runtimeId) == null)
                return new(null, $"Version {update.Version} is published, but no package is available for {_runtimeId}. Contact the publisher.");
            update.Url = update.GetDownloadUrl(_runtimeId)!;
            return new(update, $"Vortex Launcher {update.Version} is available for {_runtimeId}.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { return new(null, "Could not check for updates: " + ex.Message); }
    }

    public async Task<LauncherUpdate?> CheckAsync(string manifestUrl) => (await CheckForUpdateAsync(manifestUrl)).Update;

    internal async Task<string> DownloadAsync(LauncherUpdate update, string directory, CancellationToken token, TransferProgress? transfer)
    {
        var package = update.RequireVerifiedPackage(_runtimeId);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "package" + Path.GetExtension(new Uri(package.Url).AbsolutePath));
        string temporary = path + ".part";
        try
        {
            using var response = await _client.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != package.Size)
                throw new IOException("The update download size does not match its manifest.");
            transfer?.Begin("Launcher update", 1, package.Size);
            long received = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[262144];
                int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    received += count;
                    if (received > package.Size) throw new IOException("Update download exceeded its declared size.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    transfer?.Received(count);
                }
            }
            if (received != package.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Update checksum verification failed. The installed launcher was not changed.");
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, false);
            transfer?.Complete(received, received);
            return path;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<bool> InstallAsync(LauncherUpdate update, CancellationToken token = default, TransferProgress? transfer = null)
    {
        var package = update.RequireVerifiedPackage(_runtimeId);
        string directory = Path.Combine(LauncherUpdateInstaller.StagingRoot, Guid.NewGuid().ToString("N"));
        string archive = await DownloadAsync(update, directory, token, transfer);
        transfer?.Begin("Verifying and preparing application", 1, 0);
        string staged = await Task.Run(() => LauncherUpdateInstaller.ExtractPackage(archive, Path.Combine(directory, "staged"), _runtimeId, update.Version, token: token), token);
        if (OperatingSystem.IsMacOS())
        {
            // Restore Apple metadata/tickets and permissions with the system ZIP reader.
            await LauncherUpdateInstaller.RunToolAsync("/usr/bin/ditto", new[] { "-x", "-k", archive, Path.GetDirectoryName(staged)! }, token);
            await LauncherUpdateInstaller.VerifyMacSignatureAsync(staged, token);
        }
        token.ThrowIfCancellationRequested();
        transfer?.Complete(1, 0);
        return await LauncherUpdateInstaller.StartAsync(staged, directory, update.Version, package.Kind, token);
    }
}
