using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VortexLauncher.Core.Network;

namespace VortexLauncher.Core.Launch;

public class GameLauncher
{
    public async Task<Process> LaunchAsync(
        string instanceName,
        string version,
        string username,
        string uuid,
        string customJavaPath,
        int ramGb,
        string customJvmArgs,
        string loaderType,
        FabricProfile? fabricProfile,
        Action<string>? logCallback,
        Action<string, double>? progressCallback,
        string? serverAddress = null, CancellationToken cancellationToken = default, TransferProgress? transfer = null,
        string? loaderVersion = null, string? wardrobeToken = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string baseDir = Path.Combine(PlatformSupport.DataRoot, "instances", instanceName);
        var gameArchitecture = PlatformSupport.GetGameArchitecture(baseDir, version);
        bool isInvalidPath = string.IsNullOrWhiteSpace(customJavaPath)
            || customJavaPath.Contains("latest")
            || !File.Exists(customJavaPath);

        string javaPath = isInvalidPath
            ? JavaResolver.GetJavaForVersion(version, gameArchitecture)
            : customJavaPath;
        PlatformSupport.ValidateJavaArchitecture(javaPath, gameArchitecture);

        if (!File.Exists(javaPath))
        {
            int requiredJava = JavaResolver.GetRequiredJavaVersion(version);
            progressCallback?.Invoke($"Java {requiredJava} is not installed. Downloading runtime...", 0);
            javaPath = await new JavaRuntimeManager().EnsureJavaAsync(requiredJava, progressCallback, cancellationToken, gameArchitecture);
        }

        string librariesDir = Path.Combine(baseDir, "libraries");
        string nativesDir = Path.Combine(baseDir, "natives");
        string clientJar = Path.Combine(baseDir, $"{version}.jar");

        // DownloadGameAsync has already verified and extracted the native
        // archives selected by this version's manifest. Do not rescan old JARs
        // here: they can overwrite current binaries with an obsolete version.
        if (OperatingSystem.IsMacOS() && gameArchitecture == System.Runtime.InteropServices.Architecture.X64 &&
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64)
            logCallback?.Invoke("[JAVA]: This Minecraft version uses Intel native libraries. Rosetta 2 is required on Apple Silicon.");

        bool isFabricLike = (loaderType.Equals("Fabric", StringComparison.OrdinalIgnoreCase) ||
                             loaderType.Equals("Quilt", StringComparison.OrdinalIgnoreCase)) &&
                            fabricProfile != null;
        bool isForge = loaderType.Equals("Forge", StringComparison.OrdinalIgnoreCase);

        if (!isFabricLike && !isForge && !loaderType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The {loaderType} loader is not supported by the launcher yet.");
        }

        if ((loaderType.Equals("Fabric", StringComparison.OrdinalIgnoreCase) ||
             loaderType.Equals("Quilt", StringComparison.OrdinalIgnoreCase)) && fabricProfile == null)
        {
            throw new InvalidOperationException(
                $"{loaderType} profile was not found for Minecraft {version}.");
        }

        string? forgeVersion = null;
        ForgeLaunchProfile? forgeProfile = null;
        if (isForge)
        {
            var forgeService = new ForgeService();
            forgeVersion = string.IsNullOrWhiteSpace(loaderVersion)
                ? await forgeService.GetPromotedForgeVersionAsync(version).WaitAsync(cancellationToken)
                : (loaderVersion.StartsWith(version + "-", StringComparison.Ordinal)
                    ? loaderVersion : version + "-" + loaderVersion);
            if (!string.IsNullOrEmpty(forgeVersion))
            {
                await forgeService.InstallForgeProfileAsync(forgeVersion, version, baseDir, javaPath, (status, prog) =>
                {
                    progressCallback?.Invoke(status, prog);
                }, cancellationToken, transfer);

                string processedForgeClient = Path.Combine(
                    librariesDir, "net", "minecraftforge", "forge", forgeVersion,
                    $"forge-{forgeVersion}-client.jar");

                if (!File.Exists(processedForgeClient))
                {
                    string? clientDir = Path.GetDirectoryName(processedForgeClient);
                    if (!string.IsNullOrEmpty(clientDir))
                    {
                        Directory.CreateDirectory(clientDir);
                    }
                    string vanillaClient = Path.Combine(baseDir, $"{version}.jar");
                    if (File.Exists(vanillaClient))
                    {
                        File.Copy(vanillaClient, processedForgeClient, true);
                    }
                }

                if (File.Exists(processedForgeClient))
                {
                    clientJar = processedForgeClient;
                }

                forgeProfile = LoadForgeLaunchProfile(baseDir, version, forgeVersion);
            }
        }

        string staleMinecraftLibrary = Path.Combine(librariesDir, "minecraft.jar");
        if (File.Exists(staleMinecraftLibrary))
        {
            File.Delete(staleMinecraftLibrary);
        }

        var libJars = SharedGameFiles.LibrariesFor(baseDir, version) ?? (Directory.Exists(librariesDir)
            ? Directory.GetFiles(librariesDir, "*.jar", SearchOption.AllDirectories)
                .Where(IsCompatibleLibrary)
                .ToArray()
            : Array.Empty<string>());
        libJars = libJars.Where(IsCompatibleLibrary).ToArray();

        if (isForge && !string.IsNullOrEmpty(forgeVersion))
        {
            var profileLibraries = ResolveForgeProfileLibraries(
                baseDir, version, forgeVersion, librariesDir);
            if (profileLibraries.Count > 0)
            {
                libJars = profileLibraries.ToArray();
            }
        }

        if (isForge && string.IsNullOrWhiteSpace(forgeVersion))
        {
            throw new InvalidOperationException(
                $"Forge version is not available for Minecraft {version}.");
        }

        if (isForge && forgeProfile == null)
        {
            throw new InvalidOperationException(
                $"Forge profile was not found for Minecraft {version} ({forgeVersion}).");
        }

        bool isLegacyForge = version.StartsWith("1.20") || version.StartsWith("1.19") || version.StartsWith("1.18");
        bool isModernForge = isForge && string.Equals(
            forgeProfile?.MainClass,
            "net.minecraftforge.bootstrap.ForgeBootstrap",
            StringComparison.OrdinalIgnoreCase);
        bool isLegacyBootstrapForge = isForge && (isLegacyForge || string.Equals(
            forgeProfile?.MainClass,
            "cpw.mods.bootstraplauncher.BootstrapLauncher",
            StringComparison.OrdinalIgnoreCase));

        string mainClass = "net.minecraft.client.main.Main";

        if (isFabricLike)
        {
            mainClass = fabricProfile!.MainClass;
        }
        else if (isForge)
        {
            mainClass = forgeProfile?.MainClass ?? (isLegacyForge
                ? "cpw.mods.bootstraplauncher.BootstrapLauncher"
                : "net.minecraftforge.bootstrap.ForgeBootstrap");
        }

        string assetIndex = version;
        string indexesDir = Path.Combine(baseDir, "assets", "indexes");
        if (SharedGameFiles.Read(baseDir) == null && Directory.Exists(indexesDir))
        {
            var files = Directory.GetFiles(indexesDir, "*.json");
            if (files.Length > 0)
            {
                assetIndex = Path.GetFileNameWithoutExtension(files[0]);
            }
        }

        assetIndex = SharedGameFiles.IndexFor(baseDir, assetIndex);
        var sb = new StringBuilder();
        sb.AppendLine(FormatArgument($"-Xmx{ramGb}G"));
        if (OperatingSystem.IsMacOS()) sb.AppendLine("-XstartOnFirstThread");

        if (isForge)
        {
            var profileJvmArguments = forgeProfile?.JvmArguments
                .Select(argument => ExpandForgeArgument(
                    argument, baseDir, librariesDir, nativesDir, version,
                    forgeProfile.Id))
                .ToList() ?? new List<string>();

            var profileModuleJars = new List<string>();
            var filteredJvmArguments = new List<string>();

            for (int i = 0; i < profileJvmArguments.Count; i++)
            {
                string arg = profileJvmArguments[i].Trim('\"');
                if (arg == "-p" || arg == "--module-path")
                {
                    if (i + 1 < profileJvmArguments.Count)
                    {
                        string mp = profileJvmArguments[++i].Trim('\"');
                        foreach (string p in mp.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                        {
                            string fullPath = Path.GetFullPath(Path.Combine(baseDir, p));
                            if (File.Exists(fullPath) && !profileModuleJars.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                            {
                                profileModuleJars.Add(fullPath);
                            }
                        }
                    }
                }
                else if (arg.StartsWith("--module-path="))
                {
                    string mp = arg.Substring("--module-path=".Length).Trim('\"');
                    foreach (string p in mp.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string fullPath = Path.GetFullPath(Path.Combine(baseDir, p));
                        if (File.Exists(fullPath) && !profileModuleJars.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                        {
                            profileModuleJars.Add(fullPath);
                        }
                    }
                }
                else
                {
                    filteredJvmArguments.Add(profileJvmArguments[i]);
                }
            }

            profileJvmArguments = filteredJvmArguments;

            string? legacyAsmVersion = isLegacyBootstrapForge
                ? libJars
                    .Where(path => path.Replace('\\', '/').Contains("/org/ow2/asm/", StringComparison.OrdinalIgnoreCase))
                    .Select(path => Path.GetFileNameWithoutExtension(path).Split('-').LastOrDefault())
                    .Where(value => value != null && Version.TryParse(value, out _))
                    .Select(value => value!)
                    .OrderByDescending(value => Version.Parse(value))
                    .FirstOrDefault()
                : null;

            var rawModuleJars = profileModuleJars.Count > 0
                ? profileModuleJars
                : libJars.Where(f =>
                {
                    string name = Path.GetFileName(f).ToLowerInvariant();
                    if (isLegacyBootstrapForge)
                    {
                        return name.Contains("bootstraplauncher") ||
                               name.Contains("securejarhandler") ||
                               name.Contains("jarjarfilesystems") ||
                               (name.StartsWith("asm-") &&
                                legacyAsmVersion != null &&
                                name.EndsWith($"-{legacyAsmVersion}.jar", StringComparison.OrdinalIgnoreCase));
                    }

                    if (isModernForge)
                    {
                        // Forge 26+ builds its own SecureModuleLayer from the
                        // runtime classpath. Only the bootstrap layer belongs
                        // on the JVM module path; putting fmlloader/modlauncher
                        // there prevents Forge from discovering forge_client
                        // and also hides the Implementation-Version manifest.
                        return name.Contains("bootstrap") ||
                               name.Contains("securemodules") ||
                               name.Contains("securejarhandler") ||
                               (name.Contains("unsafe") && !name.StartsWith("lwjgl")) ||
                               (name.StartsWith("asm-") &&
                                !name.Contains("9.3") &&
                                !name.Contains("9.6"));
                    }

                    return false;
                })
                .Where(f => !f.EndsWith("-sources.jar", StringComparison.OrdinalIgnoreCase) &&
                            !f.EndsWith("-javadoc.jar", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var moduleJars = rawModuleJars
                .GroupBy(f => GetArtifactName(Path.GetFileNameWithoutExtension(f)))
                .Select(g => g.First())
                .ToList();

            string forgeModulePath = string.Join(Path.PathSeparator.ToString(), moduleJars.Select(f => f.Replace('\\', '/')));

            var runtimeClasspath = libJars
                    .Where(f => !moduleJars.Contains(f, StringComparer.OrdinalIgnoreCase))
                    .Where(f => !string.Equals(f, clientJar, StringComparison.OrdinalIgnoreCase))
                    .Where(f => !(isModernForge || isLegacyBootstrapForge) ||
                                !f.Replace('\\', '/').Contains("/net/minecraft/client/", StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (isModernForge)
            {
                runtimeClasspath.Insert(0, clientJar);
                string vanillaClientJar = Path.Combine(baseDir, $"{version}.jar");
                if (File.Exists(vanillaClientJar) &&
                    !string.Equals(vanillaClientJar, clientJar, StringComparison.OrdinalIgnoreCase))
                {
                    runtimeClasspath.Add(vanillaClientJar);
                }
            }
            else
            {
                runtimeClasspath.Add(clientJar);
            }

            string classpath = string.Join(Path.PathSeparator.ToString(), runtimeClasspath.Select(f => f.Replace('\\', '/')));

            if (moduleJars.Count > 0)
            {
                sb.AppendLine("--module-path");
                sb.AppendLine(FormatArgument(forgeModulePath));
                sb.AppendLine("--add-modules");
                sb.AppendLine("ALL-MODULE-PATH");
            }

            foreach (string argument in profileJvmArguments)
            {
                sb.AppendLine(FormatArgument(argument));
            }

            if (isModernForge)
            {
                sb.AppendLine("--add-exports=java.base/sun.security.util=ALL-UNNAMED");
                sb.AppendLine("--add-exports=jdk.naming.rmi/com.sun.jndi.rmi.registry=ALL-UNNAMED");
                sb.AppendLine("--add-opens=java.base/java.lang=ALL-UNNAMED");
                sb.AppendLine("--add-opens=java.base/java.lang.invoke=ALL-UNNAMED");
                sb.AppendLine("--add-opens=java.base/java.util=ALL-UNNAMED");
                sb.AppendLine("--add-opens=java.base/java.util.jar=ALL-UNNAMED");
                sb.AppendLine("--add-opens=java.base/java.nio=ALL-UNNAMED");
                sb.AppendLine("--enable-native-access=ALL-UNNAMED");
            }

            if (isLegacyBootstrapForge)
            {
                sb.AppendLine("--add-opens=java.base/java.lang.invoke=cpw.mods.securejarhandler");
                sb.AppendLine("-DignoreList=bootstraplauncher,securejarhandler,asm-commons,asm-util,asm-analysis,asm-tree,asm,JarJarFileSystems,client-extra,fmlcore,javafmllanguage,lowcodelanguage,mclanguage,forge-,${version_name}.jar");
                sb.AppendLine(FormatArgument($"-DlibraryDirectory={librariesDir}"));
            }

            sb.AppendLine(FormatArgument($"-Dforgesvc.net.minecraftforge.fml.libs={librariesDir}"));
            sb.AppendLine("-Dnet.minecraftforge.vulkan=false");
            sb.AppendLine(FormatArgument($"-Djava.library.path={nativesDir}"));
            sb.AppendLine(FormatArgument($"-Dorg.lwjgl.librarypath={nativesDir}"));
            sb.AppendLine("-cp");
            sb.AppendLine(FormatArgument(classpath));
        }
        else
        {
            string classpath = string.Join(Path.PathSeparator.ToString(), libJars.Concat(new[] { clientJar }).Select(f => f.Replace('\\', '/')));
            sb.AppendLine(FormatArgument($"-Djava.library.path={nativesDir}"));
            sb.AppendLine(FormatArgument($"-Dorg.lwjgl.librarypath={nativesDir}"));
            sb.AppendLine("-cp");
            sb.AppendLine(FormatArgument(classpath));
        }

        if (!string.IsNullOrWhiteSpace(customJvmArgs))
        {
            foreach (var arg in customJvmArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                sb.AppendLine(FormatArgument(arg));
            }
        }

        ApplyStackShadowPagesRequirement(sb, baseDir, version);

        sb.AppendLine(mainClass);

        sb.AppendLine("--username");
        sb.AppendLine(FormatArgument(username));
        sb.AppendLine("--version");
        sb.AppendLine(FormatArgument(version));
        sb.AppendLine("--gameDir");
        sb.AppendLine(FormatArgument(baseDir));
        sb.AppendLine("--assetsDir");
        sb.AppendLine(FormatArgument(SharedGameFiles.AssetsFor(baseDir)));
        sb.AppendLine("--assetIndex");
        sb.AppendLine(FormatArgument(assetIndex));
        sb.AppendLine("--uuid");
        sb.AppendLine(FormatArgument(uuid));
        sb.AppendLine("--accessToken");
        sb.AppendLine("0");
        sb.AppendLine("--userType");
        sb.AppendLine("legacy");
        sb.AppendLine("--versionType");
        sb.AppendLine("release");

        if (isForge)
        {
            var profileGameArguments = forgeProfile?.GameArguments
                .Select(argument => ExpandForgeArgument(
                    argument, baseDir, librariesDir, nativesDir, version,
                    forgeProfile.Id))
                .ToList() ?? new List<string>();

            bool HasGameOption(string option) => profileGameArguments.Any(argument =>
                string.Equals(argument, option, StringComparison.OrdinalIgnoreCase));

            foreach (string argument in profileGameArguments)
            {
                sb.AppendLine(FormatArgument(argument));
            }

            bool usesModLauncherTarget =
                isModernForge ||
                isLegacyBootstrapForge ||
                string.Equals(mainClass, "cpw.mods.modlauncher.Launcher",
                    StringComparison.OrdinalIgnoreCase);
            if (!HasGameOption("--launchTarget") && usesModLauncherTarget)
            {
                sb.AppendLine("--launchTarget");
                sb.AppendLine(isModernForge ? "forge_client" :
                              isLegacyBootstrapForge ? "forgeclient" : "fmlclient");
            }

            if (!string.IsNullOrEmpty(forgeVersion))
            {
                if (!HasGameOption("--fml.forgeGroup"))
                {
                    sb.AppendLine("--fml.forgeGroup");
                    sb.AppendLine("net.minecraftforge");
                }

                if (!HasGameOption("--fml.forgeVersion"))
                {
                    sb.AppendLine("--fml.forgeVersion");
                    sb.AppendLine(GetForgeReleaseVersion(forgeVersion));
                }

                if (!HasGameOption("--fml.mcVersion"))
                {
                    sb.AppendLine("--fml.mcVersion");
                    sb.AppendLine(version);
                }

                string? mcpConfig = Directory.Exists(librariesDir)
                    ? Directory.GetFiles(librariesDir, "mcp_config-*", SearchOption.AllDirectories)
                        .FirstOrDefault(f => Path.GetFileName(f)
                            .StartsWith("mcp_config-", StringComparison.OrdinalIgnoreCase))
                    : null;
                if (mcpConfig != null)
                {
                    string[] mcpParts = Path.GetFileNameWithoutExtension(mcpConfig).Split('-');
                    if (mcpParts.Length >= 3 && !HasGameOption("--fml.mcpVersion"))
                    {
                        sb.AppendLine("--fml.mcpVersion");
                        sb.AppendLine(mcpParts[2]);
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(serverAddress))
        {
            string host = serverAddress.Trim();
            string port = "25565";
            int separator = host.LastIndexOf(':');
            if (separator > 0 && separator < host.Length - 1 &&
                int.TryParse(host[(separator + 1)..], out int parsedPort) &&
                parsedPort is >= 1 and <= 65535)
            {
                host = host[..separator];
                port = parsedPort.ToString();
            }

            sb.AppendLine("--server");
            sb.AppendLine(FormatArgument(host));
            sb.AppendLine("--port");
            sb.AppendLine(port);
        }

        string argFilePath = Path.Combine(baseDir, "java_args.txt");
        await File.WriteAllTextAsync(argFilePath, sb.ToString(), new UTF8Encoding(false));

        string processArguments = IsJava8(javaPath)
            ? string.Join(" ", File.ReadAllLines(argFilePath)
                .Where(line => !string.IsNullOrWhiteSpace(line)))
            : $"\"@{argFilePath}\"";
        logCallback?.Invoke($"[AUTO JAVA RESOLVED]: {javaPath}");
        logCallback?.Invoke($"[ARGS FILE GENERATED]: {argFilePath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = javaPath,
            Arguments = processArguments,
            WorkingDirectory = baseDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = startInfo };
        // Never put account credentials in arguments, logs or exported game files.
        startInfo.Environment.Remove("VORTEX_WARDROBE_TOKEN");
        if (VortexLauncher.Core.Accounts.VortexAccountClient.ValidToken(wardrobeToken)) startInfo.Environment["VORTEX_WARDROBE_TOKEN"] = wardrobeToken;

        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                logCallback?.Invoke(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                logCallback?.Invoke($"[ERROR] {e.Data}");
            }
        };

        await MacMicrophonePermission.PrepareAsync(baseDir, logCallback, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        process.Start();

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    private static string FormatArgument(string p)
    {
        p = p.Replace('\\', '/').Trim('\"');
        return p.Contains(' ') ? $"\"{p}\"" : p;
    }

    private static string GetArtifactName(string filename)
    {
        int lastDashIndex = filename.LastIndexOf('-');
        while (lastDashIndex > 0)
        {
            string possibleVersion = filename.Substring(lastDashIndex + 1);
            if (char.IsDigit(possibleVersion[0]))
            {
                return filename.Substring(0, lastDashIndex);
            }
            lastDashIndex = filename.LastIndexOf('-', lastDashIndex - 1);
        }
        return filename;
    }

    private static bool IsJava8(string javaPath) => JavaResolver.GetMajorVersion(javaPath) == 8;

    private static void ApplyStackShadowPagesRequirement(
        StringBuilder arguments, string baseDir, string version)
    {
        const string prefix = "-XX:StackShadowPages=";
        string metadataPath = Path.Combine(baseDir, "versions", version, $"{version}.json");
        if (!File.Exists(metadataPath)) return;

        using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
        int requiredPages = 0;
        foreach (string argument in ReadProfileArguments(document.RootElement, "jvm"))
        {
            if (argument.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(argument[prefix.Length..], out int pages))
            {
                requiredPages = Math.Max(requiredPages, pages);
            }
        }

        // Native shader callbacks in Minecraft 26.3 need more stack shadow
        // space. Use the version's requirement, not a global JVM default,
        // so older Minecraft versions and their Java runtimes are unchanged.
        if (requiredPages == 0) return;

        var remainingArguments = new List<string>();
        foreach (string line in arguments.ToString().Split(
                     '\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string argument = line.Trim().Trim('"');
            if (!argument.StartsWith(prefix, StringComparison.Ordinal))
            {
                remainingArguments.Add(line.TrimEnd('\r'));
                continue;
            }

            // Merge Forge/custom settings into one option. A smaller manual
            // value must not override the version's native stack requirement.
            if (int.TryParse(argument[prefix.Length..], out int pages))
            {
                requiredPages = Math.Max(requiredPages, pages);
            }
        }

        arguments.Clear();
        foreach (string argument in remainingArguments)
        {
            arguments.AppendLine(argument);
        }
        arguments.AppendLine(prefix + requiredPages);
    }

    private sealed class ForgeLaunchProfile
    {
        public string Id { get; init; } = string.Empty;
        public string MainClass { get; init; } = string.Empty;
        public List<string> JvmArguments { get; init; } = new();
        public List<string> GameArguments { get; init; } = new();
    }

    private static ForgeLaunchProfile? LoadForgeLaunchProfile(
        string baseDir,
        string minecraftVersion,
        string forgeVersion)
    {
        string releaseVersion = GetForgeReleaseVersion(forgeVersion);
        string profileId = $"{minecraftVersion}-forge-{releaseVersion}";
        string profilePath = Path.Combine(
            baseDir, "versions", profileId, $"{profileId}.json");

        if (!File.Exists(profilePath))
        {
            string versionsDir = Path.Combine(baseDir, "versions");
            if (Directory.Exists(versionsDir))
            {
                profilePath = Directory.GetFiles(versionsDir, "*forge*.json", SearchOption.AllDirectories)
                    .FirstOrDefault(f => Path.GetFileName(f).Contains(minecraftVersion))
                    ?? string.Empty;
            }
        }

        if (string.IsNullOrWhiteSpace(profilePath) || !File.Exists(profilePath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(profilePath));
            JsonElement root = document.RootElement;
            string id = root.TryGetProperty("id", out var idValue)
                ? idValue.GetString() ?? profileId
                : profileId;
            string mainClass = root.TryGetProperty("mainClass", out var mainClassValue)
                ? mainClassValue.GetString() ?? string.Empty
                : string.Empty;

            var jvmArguments = ReadProfileArguments(root, "jvm");
            var gameArguments = ReadProfileArguments(root, "game");

            if (gameArguments.Count == 0 &&
                root.TryGetProperty("minecraftArguments", out var legacyArguments) &&
                legacyArguments.ValueKind == JsonValueKind.String)
            {
                gameArguments = TokenizeLegacyArguments(legacyArguments.GetString() ?? string.Empty)
                    .ToList();
                RemoveCommonGameArguments(gameArguments);
            }

            return new ForgeLaunchProfile
            {
                Id = id,
                MainClass = mainClass,
                JvmArguments = jvmArguments,
                GameArguments = gameArguments
            };
        }
        catch
        {
            return null;
        }
    }

    private static List<string> ReadProfileArguments(JsonElement root, string propertyName)
    {
        var result = new List<string>();
        if (!root.TryGetProperty("arguments", out var arguments) ||
            arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty(propertyName, out var values) ||
            values.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement value in values.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                result.Add(value.GetString() ?? string.Empty);
                continue;
            }

            if (value.ValueKind != JsonValueKind.Object ||
                !PlatformSupport.RulesAllow(value) ||
                !value.TryGetProperty("value", out var objectValue))
            {
                continue;
            }

            if (objectValue.ValueKind == JsonValueKind.String)
            {
                result.Add(objectValue.GetString() ?? string.Empty);
            }
            else if (objectValue.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in objectValue.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        result.Add(item.GetString() ?? string.Empty);
                    }
                    else if (item.ValueKind == JsonValueKind.Array)
                    {
                        var parts = new List<string>();
                        foreach (JsonElement subItem in item.EnumerateArray())
                        {
                            if (subItem.ValueKind == JsonValueKind.String)
                            {
                                parts.Add(subItem.GetString() ?? string.Empty);
                            }
                        }
                        result.Add(string.Join(Path.PathSeparator.ToString(), parts));
                    }
                }
            }
        }

        return result;
    }

    private static IEnumerable<string> TokenizeLegacyArguments(string arguments)
    {
        return arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(argument => argument.Trim('\"'));
    }

    private static void RemoveCommonGameArguments(List<string> arguments)
    {
        string[] commonOptions =
        {
            "--username", "--version", "--gameDir", "--assetsDir",
            "--assetIndex", "--uuid", "--accessToken", "--userType",
            "--versionType", "--width", "--height"
        };

        for (int i = arguments.Count - 1; i >= 0; i--)
        {
            if (!commonOptions.Contains(arguments[i], StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            arguments.RemoveAt(i);
            if (i < arguments.Count)
            {
                arguments.RemoveAt(i);
            }
        }
    }

    private static string ExpandForgeArgument(
        string argument,
        string baseDir,
        string librariesDir,
        string nativesDir,
        string minecraftVersion,
        string profileId)
    {
        return argument
            .Replace("${library_directory}", librariesDir.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase)
            .Replace("${natives_directory}", nativesDir.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase)
            .Replace("${game_directory}", baseDir.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase)
            .Replace("${assets_root}", SharedGameFiles.AssetsFor(baseDir).Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase)
            .Replace("${assets_index_name}", SharedGameFiles.IndexFor(baseDir, minecraftVersion),
                StringComparison.OrdinalIgnoreCase)
            .Replace("${version_name}", profileId,
                StringComparison.OrdinalIgnoreCase)
            // Minecraft 1.7.10 parses userProperties as a JSON object.
            // Offline accounts do not have custom properties, so an empty
            // object is the correct value instead of the unresolved token.
            .Replace("${user_properties}", "{}",
                StringComparison.OrdinalIgnoreCase)
            .Replace("${classpath_separator}", Path.PathSeparator.ToString(),
                StringComparison.OrdinalIgnoreCase);
    }

    private static string GetForgeReleaseVersion(string forgeVersion)
    {
        int separator = forgeVersion.LastIndexOf('-');
        return separator >= 0 && separator + 1 < forgeVersion.Length
            ? forgeVersion[(separator + 1)..]
            : forgeVersion;
    }

    private static bool IsCompatibleLibrary(string path)
    {
        string name = Path.GetFileName(path).ToLowerInvariant();
        return !name.Contains("-natives-");
    }

    private static List<string> ResolveForgeProfileLibraries(
        string baseDir,
        string minecraftVersion,
        string forgeVersion,
        string librariesDir)
    {
        var result = new List<string>();
        var seenProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var jarIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void ReadProfile(string profilePath)
        {
            if (!File.Exists(profilePath)) return;

            string fullProfilePath = Path.GetFullPath(profilePath);
            if (!seenProfiles.Add(fullProfilePath)) return;

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(fullProfilePath));
                JsonElement root = document.RootElement;

                if (root.TryGetProperty("inheritsFrom", out var inherits) &&
                    inherits.ValueKind == JsonValueKind.String)
                {
                    string parent = inherits.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(parent))
                    {
                        ReadProfile(Path.Combine(
                            baseDir, "versions", parent, $"{parent}.json"));
                    }
                }

                if (!root.TryGetProperty("libraries", out var libraries) ||
                    libraries.ValueKind != JsonValueKind.Array)
                {
                    return;
                }

                foreach (JsonElement library in libraries.EnumerateArray())
                {
                    if (!PlatformSupport.RulesAllow(library)) continue;

                    string path = string.Empty;
                    if (library.TryGetProperty("downloads", out var downloads) &&
                        downloads.TryGetProperty("artifact", out var artifact) &&
                        artifact.TryGetProperty("path", out var artifactPath))
                    {
                        path = artifactPath.GetString() ?? string.Empty;
                    }

                    if (string.IsNullOrWhiteSpace(path) &&
                        library.TryGetProperty("name", out var name))
                    {
                        path = MavenNameToPath(name.GetString() ?? string.Empty);
                    }

                    if (string.IsNullOrWhiteSpace(path) || path.Contains("-natives-", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string jarPath = Path.Combine(
                        librariesDir, path.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(jarPath))
                    {
                        string identity = GetLibraryIdentity(path);
                        if (jarIndexes.TryGetValue(identity, out int existingIndex))
                        {
                            result[existingIndex] = jarPath;
                        }
                        else
                        {
                            jarIndexes[identity] = result.Count;
                            result.Add(jarPath);
                        }
                    }
                }
            }
            catch { }
        }

        string forgeProfile = Path.Combine(
            baseDir, "versions", $"{minecraftVersion}-forge-{forgeVersion.Split('-').Last()}",
            $"{minecraftVersion}-forge-{forgeVersion.Split('-').Last()}.json");

        if (!File.Exists(forgeProfile))
        {
            string versionsDir = Path.Combine(baseDir, "versions");
            if (Directory.Exists(versionsDir))
            {
                forgeProfile = Directory.GetFiles(versionsDir, "*forge*.json", SearchOption.AllDirectories)
                    .FirstOrDefault(f => Path.GetFileName(f).Contains(minecraftVersion)) ?? forgeProfile;
            }
        }
        ReadProfile(forgeProfile);

        return result;
    }

    private static string GetLibraryIdentity(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/').Trim('/');
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            return normalized;
        }

        string identity = string.Join('/', parts.Take(parts.Length - 2));
        string fileName = Path.GetFileNameWithoutExtension(parts[^1]);
        if (fileName.EndsWith("-client", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("-universal", StringComparison.OrdinalIgnoreCase))
        {
            identity += fileName.EndsWith("-client", StringComparison.OrdinalIgnoreCase)
                ? ":client"
                : ":universal";
        }

        return identity;
    }

    private static string MavenNameToPath(string name)
    {
        string[] parts = name.Split(':');
        if (parts.Length < 3) return string.Empty;

        string group = parts[0].Replace('.', '/');
        string artifact = parts[1];
        string version = parts[2];
        string classifier = parts.Length > 3 ? $"-{parts[3]}" : string.Empty;
        return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.jar";
    }

}
