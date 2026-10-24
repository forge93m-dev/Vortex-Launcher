using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace VortexLauncher.Core.Network;

public sealed class LauncherUpdatePackage
{
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
}

public sealed class LauncherUpdate
{
    [JsonPropertyName("platforms")] public Dictionary<string, string> Platforms { get; set; } = new();
    [JsonPropertyName("packages")] public Dictionary<string, LauncherUpdatePackage> Packages { get; set; } = new();
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
    [JsonPropertyName("keyId")] public string? KeyId { get; set; }
    [JsonPropertyName("signature")] public string? Signature { get; set; }

    internal static bool IsHttps(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo);

    public string? GetDownloadUrl(string runtimeId)
    {
        string candidate = Packages.TryGetValue(runtimeId, out var package) ? package.Url :
            Platforms.TryGetValue(runtimeId, out var url) ? url : Platforms.Count == 0 ? Url : "";
        if (!IsHttps(candidate)) return null;
        string extension = Path.GetExtension(new Uri(candidate).AbsolutePath).ToLowerInvariant();
        return runtimeId.StartsWith("osx-", StringComparison.Ordinal)
            ? extension is ".zip" or ".dmg" or ".pkg" ? candidate : null
            : runtimeId.StartsWith("win-", StringComparison.Ordinal) && extension is ".exe" or ".zip" ? candidate : null;
    }

    public LauncherUpdatePackage RequireVerifiedPackage(string runtimeId)
    {
        if (!Packages.TryGetValue(runtimeId, out var package) || GetDownloadUrl(runtimeId) == null ||
            package.Size <= 0 || package.Size > 2147483648L ||
            package.Sha256.Length != 64 || !System.Text.RegularExpressions.Regex.IsMatch(package.Sha256, "^[0-9a-fA-F]{64}$"))
            throw new InvalidDataException("The publisher must provide a platform package with its SHA-256 checksum and size. This old/incomplete manifest cannot be installed safely.");
        string expectedKind = runtimeId.StartsWith("osx-", StringComparison.Ordinal) ? "mac-app-zip" : "windows-singlefile-zip";
        if (package.Kind != expectedKind || !new Uri(package.Url).AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This updater requires a verified app ZIP for the current platform.");
        return package;
    }
}

/// <summary>
/// Publisher signature for the update feed (latest.json).
/// The SHA-256 checksums in the feed only prove that a download matches the feed; this signature
/// proves that the feed itself was produced by the holder of the release key, so a compromised
/// repository or CDN cannot point installed launchers at a different program.
/// </summary>
internal static class UpdateManifestSignature
{
    public const string PayloadHeader = "vortex-launcher-update/1";

    /// <summary>
    /// Trusted ECDSA P-256 public keys (SubjectPublicKeyInfo, Base64), by key id.
    /// To rotate: ship a release that trusts both the old and the new key, then sign with the new one.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> ProductionKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["vortex-2026-10b"] = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEnc3P6R2M0YufmZNn6h9KUvlyTjjuL+Uv7uMKFKfCLZAQST2jtlRSimVRKA2g+j3RzW2GjQrKVv+6H9l2n8sawQ==",
    };

    /// <summary>
    /// Deterministic text covering everything the updater acts on or shows: version, notes and,
    /// for every platform package, kind, size, SHA-256 and URL. Legacy fields are not used to install.
    /// </summary>
    public static byte[] BuildPayload(LauncherUpdate update)
    {
        var text = new StringBuilder();
        text.Append(PayloadHeader).Append('\n');
        text.Append("version:").Append(update.Version).Append('\n');
        text.Append("notes:").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(update.Notes ?? ""))).Append('\n');
        foreach (var (runtime, package) in update.Packages.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            text.Append("package:").Append(runtime).Append('|').Append(package.Kind).Append('|')
                .Append(package.Size.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                .Append(package.Sha256.ToLowerInvariant()).Append('|').Append(package.Url).Append('\n');
        }
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    public static string Sign(LauncherUpdate update, ECDsa privateKey) =>
        Convert.ToBase64String(privateKey.SignData(BuildPayload(update), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    public static bool Verify(LauncherUpdate update, IReadOnlyDictionary<string, string> trustedKeys)
    {
        if (string.IsNullOrEmpty(update.KeyId) || string.IsNullOrEmpty(update.Signature) ||
            !trustedKeys.TryGetValue(update.KeyId, out string? publicKey)) return false;
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return key.VerifyData(BuildPayload(update), Convert.FromBase64String(update.Signature),
                HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (FormatException) { return false; }
        catch (CryptographicException) { return false; }
    }
}
