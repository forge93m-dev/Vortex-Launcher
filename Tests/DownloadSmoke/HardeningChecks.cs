using System.Net;
using System.Security.Cryptography;
using VortexLauncher.Core.Launch;
using VortexLauncher.Core.Network;
using VortexLauncher.ViewModels;

/// <summary>Regression checks for the 0.2.6 hardening fixes (synthetic fixtures only, no network).</summary>
internal static class HardeningChecks
{
    public static async Task RunAsync(string testRoot)
    {
        // Custom JVM arguments keep quoted values with spaces together.
        var split = GameLauncher.SplitUserArguments("-Xss4M  -Dpath=\"C:/My Folder/x\" '-Dname=a b' -XX:+UseG1GC");
        Check(split.SequenceEqual(new[] { "-Xss4M", "-Dpath=C:/My Folder/x", "-Dname=a b", "-XX:+UseG1GC" }), "JVM argument field respects quotes");
        Check(GameLauncher.SplitUserArguments("   ").Count == 0, "blank JVM argument field adds nothing");

        // @argfile quoting: spaces, '#' (comment marker) and quotes.
        Check(GameLauncher.FormatArgument("-Xmx4G") == "-Xmx4G", "plain arguments stay unquoted");
        Check(GameLauncher.FormatArgument(@"C:\Users\C#\game") == "\"C:/Users/C#/game\"", "paths containing '#' are quoted in the argument file");
        Check(GameLauncher.FormatArgument("a b") == "\"a b\"", "arguments with spaces are quoted");
        Check(GameLauncher.FormatArgument("say\"hi") == "\"say\\\"hi\"", "embedded quotes are escaped");

        // Asset index follows the installed version metadata, not directory order.
        string instance = Directory.CreateDirectory(Path.Combine(testRoot, "hardening-assets")).FullName;
        string versionDir = Directory.CreateDirectory(Path.Combine(instance, "versions", "1.21.11")).FullName;
        File.WriteAllText(Path.Combine(versionDir, "1.21.11.json"), "{\"assetIndex\":{\"id\":\"29\"}}");
        var indexes = new[] { Path.Combine(instance, "assets", "indexes", "17.json"), Path.Combine(instance, "assets", "indexes", "29.json") };
        Check(GameLauncher.ChooseLocalAssetIndex(instance, "1.21.11", indexes) == "29", "asset index comes from version metadata");
        Check(GameLauncher.ChooseLocalAssetIndex(instance, "1.20.1", indexes) == null, "ambiguous asset indexes are not guessed");
        Check(GameLauncher.ChooseLocalAssetIndex(instance, "1.20.1", new[] { indexes[0] }) == "17", "single asset index is still used");

        // Forge installer entries cannot escape the libraries folder.
        string libraries = Path.Combine(testRoot, "hardening-libraries");
        Check(ForgeService.ResolveInside(libraries, "net/minecraftforge/forge/x.jar") is { } inside && inside.StartsWith(Path.GetFullPath(libraries)), "normal Maven path resolves inside libraries");
        foreach (string evil in new[] { "../outside.jar", "net/../../outside.jar", "/absolute.jar", "C:/evil.jar", @"..\outside.jar" })
            Check(ForgeService.ResolveInside(libraries, evil) == null, "unsafe Forge installer path is rejected: " + evil);

        // The long Forge installer run reports readable stages from its real output.
        Check(ForgeService.InstallerStage("  MainClass: net.minecraftforge.installertools.ConsoleTool") == "Forge 1/4: preparing mappings...", "Forge mapping stage is named");
        Check(ForgeService.InstallerStage("  MainClass: net.minecraftforge.jarsplitter.ConsoleTool") == "Forge 2/4: splitting the game files...", "Forge splitting stage is named");
        Check(ForgeService.InstallerStage("  MainClass: net.minecraftforge.fart.Main")!.StartsWith("Forge 3/4: remapping"), "Forge remapping stage is named");
        Check(ForgeService.InstallerStage("  MainClass: net.minecraftforge.binarypatcher.ConsoleTool") == "Forge 4/4: applying Forge patches...", "Forge patching stage is named");
        Check(ForgeService.InstallerStage("Injecting profile") == "Forge: finishing setup...", "Forge finishing stage is named");
        Check(new[] { "  Patching net/minecraft/world/item/Items$1 1/1", "Cant Find Class: org/slf4j/Logger", "  net/minecraft/util/thread/ProcessorMailbox.class", "Task: MERGE_MAPPING", "" }
            .All(line => ForgeService.InstallerStage(line) == null), "per-file installer output does not flood the progress text");

        // Modrinth requires an identifying User-Agent with the real version.
        Check(ModrinthService.UserAgent.Contains("Vortex-Launcher/") && !ModrinthService.UserAgent.Contains("/1.0.0 "), "Modrinth User-Agent carries the launcher version");

        // A failing loader lookup must not affect the other loader.
        Check(await CreateInstanceViewModel.SafeLookupAsync(() => throw new HttpRequestException("down")) == null, "failed loader lookup is isolated");
        Check(await CreateInstanceViewModel.SafeLookupAsync(() => Task.FromResult<string?>("0.29.0")) == "0.29.0", "successful loader lookup is kept");

        await JavaChecksAsync(testRoot);
        Console.WriteLine("HARDENING_OK");
    }

    private static async Task JavaChecksAsync(string testRoot)
    {
        byte[] payload = RandomNumberGenerator.GetBytes(4096);
        string sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        string json = "[{\"binary\":{\"image_type\":\"jre\",\"package\":{\"link\":\"https://example.invalid/OpenJDK21U-jre.zip\",\"checksum\":\"" +
            sha + "\",\"size\":" + payload.Length + "}}}]";
        var package = JavaRuntimeManager.ParsePackage(json, "jre");
        Check(package != null && package.Size == payload.Length && package.Sha256 == sha, "Adoptium metadata supplies link, SHA-256 and size");
        Check(JavaRuntimeManager.ParsePackage(json.Replace("https://", "http://"), "jre") == null, "plain-HTTP Java packages are refused");
        Check(JavaRuntimeManager.ParsePackage(json.Replace(sha, "abc"), "jre") == null, "Java packages without a valid checksum are refused");
        Check(JavaRuntimeManager.GetAssetsUrl(21, "windows", System.Runtime.InteropServices.Architecture.X64, "jre").Contains("image_type=jre"), "JRE is requested first");

        string target = Path.Combine(testRoot, "hardening-java.zip");
        using (var good = new HttpClient(new StaticHandler(payload)))
            await JavaRuntimeManager.DownloadVerifiedAsync(good, package!, target, default);
        Check(File.ReadAllBytes(target).SequenceEqual(payload), "verified Java download is kept");

        byte[] tampered = (byte[])payload.Clone();
        tampered[10] ^= 0xFF;
        bool rejected = false;
        try
        {
            using var bad = new HttpClient(new StaticHandler(tampered));
            await JavaRuntimeManager.DownloadVerifiedAsync(bad, package!, target, default);
        }
        catch (IOException) { rejected = true; }
        Check(rejected, "tampered Java download fails checksum verification");
    }

    private sealed class StaticHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
