using System.Text;
using System.Text.Json;
using VortexLauncher.Core.Diagnostics;
using VortexLauncher.Core.Launch;

internal static class InstallationPolishChecks
{
    internal static async Task RunAsync(string root, FixtureHandler handler, HttpClient client,
        GameDownloader downloader, string classifier, string nativeName)
    {
        var localizedFull = new IOException("Localized disk-full message", unchecked((int)0x80070070));
        Check(CrashAnalyzer.Diagnose(new InvalidOperationException("Install failed", localizedFull)).Title == "Not enough disk space",
            "nested disk-full error is diagnosed by OS code, not language");
        Check(CrashAnalyzer.Diagnose(new[] { "System.IO.IOException: There is not enough space on the disk." }).Title == "Not enough disk space",
            "old disk-full logs have an actionable diagnosis");
        Check(CrashAnalyzer.Diagnose(new[] { "OutOfMemoryError: Java heap space" }).Title == "Not enough memory",
            "RAM failure remains distinct from disk capacity");
        DiskSpace.ValidateAvailable("fixture", 100, 100);
        bool full = false;
        try { DiskSpace.ValidateAvailable("fixture-cache", 101, 100); }
        catch (DiskSpaceException ex) { full = ex.Message.Contains("fixture-cache") && ex.Message.Contains("101"); }
        Check(full, "space check reports the affected path and required bytes without filling the real disk");

        string url = "https://fixture.test/disk-full.jar";
        byte[] bytes = Encoding.UTF8.GetBytes(new string('d', 1024 * 1024));
        handler.Files[url] = bytes;
        string destination = Path.Combine(root, "disk-full-destination.jar");
        await File.WriteAllTextAsync(destination, "previous file");
        string sha = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes));
        int before = handler.Requests;
        full = false;
        try
        {
            await DownloadCache.EnsureAsync(url, destination, sha1: sha, size: bytes.Length,
                received: _ => throw localizedFull, httpClient: client, cacheRoot: Path.Combine(root, "disk-full-cache"));
        }
        catch (DiskSpaceException) { full = true; }
        Check(full && handler.Requests == before + 1 && await File.ReadAllTextAsync(destination) == "previous file",
            "disk-full stops without pointless retries and preserves the previous destination");
        await DownloadCache.EnsureAsync(url, destination, sha1: sha, size: bytes.Length,
            httpClient: client, cacheRoot: Path.Combine(root, "disk-full-cache"));
        Check((await File.ReadAllBytesAsync(destination)).SequenceEqual(bytes), "retry after simulated disk-full installs a verified file");

        string badNativeUrl = "https://fixture.test/invalid-native.jar";
        byte[] broken = Encoding.UTF8.GetBytes("not a ZIP archive");
        handler.Files[badNativeUrl] = broken;
        string manifestUrl = "https://fixture.test/invalid-native-version.json";
        handler.Files[manifestUrl] = JsonSerializer.SerializeToUtf8Bytes(new { id = "native-failure",
            downloads = new { client = new { url, size = bytes.Length, sha1 = sha } }, libraries = new[] {
            new { name = "fixture:native:1", downloads = new { artifact = new { url = badNativeUrl,
                path = $"fixture/bad-natives-{classifier}.jar", size = broken.Length,
                sha1 = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(broken)) } } } } });
        bool rejected = false, reportedReady = false;
        try { await downloader.DownloadGameAsync("invalid-native", "native-failure", manifestUrl, (_, percent) => reportedReady |= percent == 100); }
        catch (InvalidDataException ex) { rejected = CrashAnalyzer.Diagnose(ex).CanRepair; }
        Check(rejected && !reportedReady, "invalid native archive is not silently reported as a successful installation");

        string failureInstance = Path.Combine(PlatformSupport.DataRoot, "instances", "download-interrupted");
        Directory.CreateDirectory(Path.Combine(failureInstance, "natives"));
        string existingNative = Path.Combine(failureInstance, "natives", nativeName);
        await File.WriteAllTextAsync(existingNative, "working native");
        handler.Files[manifestUrl] = JsonSerializer.SerializeToUtf8Bytes(new { id = "native-failure", downloads = new {
            client = new { url = "https://fixture.test/unavailable-client.jar", size = 10 } } });
        try { await downloader.DownloadGameAsync("download-interrupted", "native-failure", manifestUrl, (_, _) => { }); }
        catch (HttpRequestException) { }
        Check(await File.ReadAllTextAsync(existingNative) == "working native", "failed download does not delete working native binaries");

        handler.Files[manifestUrl] = Encoding.UTF8.GetBytes("{\"id\":\"incomplete\"}");
        rejected = false;
        try { await downloader.DownloadGameAsync("incomplete", "incomplete", manifestUrl, (_, _) => { }); }
        catch (InvalidDataException ex) { rejected = ex.Message.Contains("metadata is incomplete"); }
        Check(rejected, "incomplete version metadata cannot be reported as an installed Minecraft version");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
