using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VortexLauncher.Core.Launch;
using VortexLauncher.Core.Management;

namespace VortexLauncher.Core.Diagnostics;

public static class DiagnosticReport
{
    private const int MaxTextBytes = 2 * 1024 * 1024;
    private static string Replace(string input, string expression, string replacement) => Regex.Replace(input, expression, replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    public static string Redact(string text, string? instanceRoot = null)
    {
        // Remove whole command lines: JVM arguments may contain credentials
        // unknown to the launcher or account secrets split across many switches.
        text = Replace(text, @"(?m)^.*(?:--accessToken|--access-token|--refreshToken|Authorization\s*:|\[CHAT\]|Connecting to|server[_ ]?address|server[_ ]?ip).*$", "[private command/chat/server line removed]");
        text = Replace(text, "([\\\"']?(?:access[_-]?token|refresh[_-]?token|client[_-]?token|id[_-]?token|password|passwd|secret|api[_-]?key|session(?:id)?|username|displayname|uuid|xuid|clientid)[\\\"']?\\s*[:=]\\s*)(?:\\\"[^\\\"]*\\\"|'[^']*'|[^\\s,;}&]+)", "$1[redacted]");
        text = Replace(text, @"(--(?:username|uuid|xuid|clientId|server)\s+)(?:""[^""]*""|'[^']*'|\S+)", "$1[redacted]");
        text = Replace(text, @"\bBearer\s+\S+", "Bearer [redacted]");
        text = Replace(text, @"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)?", "[token]");
        text = Replace(text, @"\b[\w.!#$%&'*+/=?^`{|}~-]+@[\w.-]+\.[A-Za-z]{2,}\b", "[email]");
        text = Replace(text, @"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b|\b[0-9a-f]{32}\b", "[id]");
        text = Replace(text, @"\b(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?\b|(?<![\w])(?:[0-9a-f]{0,4}:){3,}[0-9a-f:]{0,39}", "[ip]");
        foreach (string root in new[] { instanceRoot ?? "", PlatformSupport.DataRoot, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) }.Where(p => p.Length > 3).OrderByDescending(p => p.Length))
        {
            text = text.Replace(root, "[local-path]", StringComparison.OrdinalIgnoreCase)
                .Replace(root.Replace('\\', '/'), "[local-path]", StringComparison.OrdinalIgnoreCase)
                .Replace(Uri.EscapeDataString(root), "[local-path]", StringComparison.OrdinalIgnoreCase)
                .Replace(root.Replace('\\', '/').Replace(" ", "%20"), "[local-path]", StringComparison.OrdinalIgnoreCase);
        }
        text = Replace(text, @"[A-Z]:[\\/][^\r\n""<>]*", "[local-path]");
        text = Replace(text, @"/(?:Users|home)/[^/\s\\]+", "/[user]");
        text = Replace(text, @"(Setting user:\s*|Logged in as\s+|Player:\s*)\S+", "$1[player]");
        if (Environment.UserName.Length > 1) text = Replace(text, @"(?<!\w)" + Regex.Escape(Environment.UserName) + @"(?!\w)", "[user]");
        return text;
    }

    public static async Task<string> CreateAsync(string destination, string? instanceRoot = null,
        IReadOnlyList<string>? captured = null, CancellationToken token = default)
    {
        destination = Path.GetFullPath(destination);
        if (File.Exists(destination)) throw new IOException("A file with this name already exists. Choose another name.");
        ModFiles.RejectLinks(destination);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                async Task Add(string name, string text)
                {
                    token.ThrowIfCancellationRequested();
                    var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                    await using var stream = entry.Open();
                    await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                    await writer.WriteAsync(Redact(text, instanceRoot).AsMemory(), token);
                }
                await Add("README.txt", "Vortex diagnostic report\nCreated locally; nothing was uploaded.\nIncludes only a bounded console tail, latest.log, the newest crash .txt, Minecraft/loader metadata and mod filenames. No accounts, settings, tokens databases, worlds, servers.dat, options or screenshots are included.\nCommon credentials, identifiers, chat/server lines, emails and local paths are redacted. Redaction is best-effort: review the text files before sharing; arbitrary mod output may contain additional private information.\n");
                string version = typeof(DiagnosticReport).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "unknown";
                string summary = $"Launcher: {version}\nOS: {RuntimeInformation.OSDescription}\nArchitecture: {RuntimeInformation.ProcessArchitecture}\n.NET: {Environment.Version}\nCreated UTC: {DateTimeOffset.UtcNow:O}\n";
                if (instanceRoot != null)
                {
                    ModFiles.RejectLinks(instanceRoot);
                    string metadata = Path.Combine(instanceRoot, "instance.json");
                    if (File.Exists(metadata))
                    {
                        ModFiles.RejectLinks(metadata);
                        if (new FileInfo(metadata).Length <= 65536)
                        {
                            try
                            {
                                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(metadata, token));
                                foreach (var property in doc.RootElement.EnumerateObject().Where(p => p.Name.Equals("Version", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("Loader", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("LoaderVersion", StringComparison.OrdinalIgnoreCase)))
                                    summary += property.Name + ": " + property.Value + "\n";
                            }
                            catch (JsonException) { summary += "Instance metadata could not be read.\n"; }
                        }
                    }
                    string mods = Path.Combine(instanceRoot, "mods"); ModFiles.RejectLinks(mods);
                    if (Directory.Exists(mods)) await Add("mods.txt", string.Join('\n', Directory.GetFiles(mods).Where(ModFiles.IsMod).Take(2000).Select(Path.GetFileName)));
                    async Task IncludeFile(string source, string entry)
                    {
                        if (!File.Exists(source)) return;
                        try
                        {
                            ModFiles.RejectLinks(source);
                            await using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, true);
                            bool truncated = stream.Length > MaxTextBytes;
                            if (truncated) stream.Seek(-MaxTextBytes, SeekOrigin.End);
                            byte[] bytes = new byte[(int)Math.Min(stream.Length - stream.Position, MaxTextBytes)];
                            int count = 0, read;
                            while (count < bytes.Length && (read = await stream.ReadAsync(bytes.AsMemory(count), token)) > 0) count += read;
                            string text = Encoding.UTF8.GetString(bytes, 0, count);
                            if (truncated) { int firstNewline = text.IndexOf('\n'); text = firstNewline < 0 ? "[oversized line omitted]" : text[(firstNewline + 1)..]; }
                            await Add(entry, (truncated ? "[tail only; earlier output omitted]\n" : "") + text);
                        }
                        catch (IOException) { summary += entry + ": unavailable or linked; not included.\n"; }
                        catch (UnauthorizedAccessException) { summary += entry + ": access denied; not included.\n"; }
                    }
                    await IncludeFile(Path.Combine(instanceRoot, "logs", "latest.log"), "latest.log");
                    string crashes = Path.Combine(instanceRoot, "crash-reports"); ModFiles.RejectLinks(crashes);
                    if (Directory.Exists(crashes))
                    {
                        string? newest = Directory.EnumerateFiles(crashes, "*.txt", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                        if (newest != null) await IncludeFile(newest, "crash-report.txt");
                    }
                }
                if (captured != null)
                {
                    var text = new StringBuilder(); int size = 0;
                    foreach (string line in captured.Reverse().Take(5000))
                    {
                        string bounded = line.Length > 16384 ? "[oversized line omitted]" : line;
                        if (size + bounded.Length > MaxTextBytes / 2) break;
                        text.Insert(0, bounded + "\n"); size += bounded.Length + 1;
                    }
                    await Add("console.log", text.ToString());
                }
                await Add("summary.txt", summary);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, false);
            return destination;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
