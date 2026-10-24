// Vortex Launcher update-feed signer.
//
//   generate <private-key-file> [key-id]   create a new ECDSA P-256 release key
//   public <private-key-file>              print the public key of an existing release key
//   sign <latest.json>                     sign with VORTEX_UPDATE_SIGNING_KEY / VORTEX_UPDATE_KEY_ID
//   verify <latest.json>                   check against the keys trusted by this launcher build
//
// The private key never needs to be in the repository: keep it in a GitHub secret and an offline backup.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VortexLauncher.Core.Network;

try
{
    return args switch
    {
        ["generate", string file, ..] => Generate(file, args.Length > 2 ? args[2] : "vortex-" + DateTime.UtcNow.ToString("yyyy-MM")),
        ["public", string file] => PrintPublic(file),
        ["sign", string manifest] => Sign(manifest),
        ["verify", string manifest] => Verify(manifest),
        _ => Usage(),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine("ERROR: " + ex.Message);
    return 1;
}

static int Usage()
{
    Console.Error.WriteLine("usage: UpdateSigner generate <private-key-file> [key-id] | public <private-key-file> | sign <latest.json> | verify <latest.json>");
    return 2;
}

static int Generate(string file, string keyId)
{
    if (File.Exists(file)) throw new IOException($"{file} already exists; refusing to overwrite a release key.");
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    File.WriteAllText(file, Convert.ToBase64String(key.ExportPkcs8PrivateKey()) + "\n", new UTF8Encoding(false));
    Console.WriteLine($"KEY_ID={keyId}");
    Console.WriteLine($"PUBLIC_KEY={Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())}");
    Console.WriteLine($"Private key written to {file}. Store it as the VORTEX_UPDATE_SIGNING_KEY secret and keep an offline backup.");
    return 0;
}

static int PrintPublic(string file)
{
    using var key = ECDsa.Create();
    key.ImportPkcs8PrivateKey(Convert.FromBase64String(File.ReadAllText(file).Trim()), out _);
    Console.WriteLine($"PUBLIC_KEY={Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())}");
    return 0;
}

static int Sign(string manifestPath)
{
    string secret = Environment.GetEnvironmentVariable("VORTEX_UPDATE_SIGNING_KEY")?.Trim() ?? "";
    string keyId = Environment.GetEnvironmentVariable("VORTEX_UPDATE_KEY_ID")?.Trim() ?? "";
    if (secret.Length == 0 || keyId.Length == 0) throw new InvalidOperationException("VORTEX_UPDATE_SIGNING_KEY and VORTEX_UPDATE_KEY_ID must be set.");
    using var key = ECDsa.Create();
    key.ImportPkcs8PrivateKey(Convert.FromBase64String(secret), out _);

    var node = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
    node.Remove("keyId");
    node.Remove("signature");
    var update = node.Deserialize<LauncherUpdate>() ?? throw new InvalidDataException("Manifest is empty.");
    if (update.Packages.Count == 0) throw new InvalidDataException("Manifest has no checksummed packages to sign.");
    node["keyId"] = keyId;
    node["signature"] = UpdateManifestSignature.Sign(update, key);

    // Self-check with the matching public key before writing anything.
    update.KeyId = keyId;
    update.Signature = node["signature"]!.GetValue<string>();
    var publicKey = new Dictionary<string, string> { [keyId] = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) };
    if (!UpdateManifestSignature.Verify(update, publicKey)) throw new CryptographicException("Signature self-check failed.");

    File.WriteAllText(manifestPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    Console.WriteLine($"SIGNED: {manifestPath} ({update.Version}, key {keyId})");
    return 0;
}

static int Verify(string manifestPath)
{
    var update = JsonSerializer.Deserialize<LauncherUpdate>(File.ReadAllBytes(manifestPath)) ?? throw new InvalidDataException("Manifest is empty.");
    if (!UpdateManifestSignature.Verify(update, UpdateManifestSignature.ProductionKeys))
    {
        Console.Error.WriteLine($"NOT TRUSTED: {manifestPath} is unsigned or signed with a key this launcher does not trust.");
        return 1;
    }
    Console.WriteLine($"VERIFIED: {manifestPath} ({update.Version}, key {update.KeyId})");
    return 0;
}
