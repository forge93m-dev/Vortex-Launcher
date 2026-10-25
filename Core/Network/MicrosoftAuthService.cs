using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VortexLauncher.Core.Network;

/// <summary>A sign-in problem the player can understand and act on. Never carries tokens or server bodies.</summary>
public sealed class MicrosoftAuthException : Exception
{
    public string Code { get; }
    public MicrosoftAuthException(string code, string message) : base(message) => Code = code;
}

/// <summary>
/// Microsoft account sign-in for Minecraft Java Edition using the device-code flow: the player enters a short code on
/// microsoft.com in their own browser, so the launcher never sees a password. The chain is Microsoft → Xbox Live → XSTS →
/// Minecraft services, ending with the game profile and a check that the account owns the game.
/// Only the fixed official hosts below are ever contacted.
/// </summary>
public sealed class MicrosoftAuthService
{
    public const string Scope = "XboxLive.signin offline_access";
    internal const string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    internal const string TokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    internal const string XboxUserUrl = "https://user.auth.xboxlive.com/user/authenticate";
    internal const string XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
    internal const string MinecraftLoginUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";
    internal const string EntitlementsUrl = "https://api.minecraftservices.com/entitlements/mcstore";
    internal const string ProfileUrl = "https://api.minecraftservices.com/minecraft/profile";
    private const int MaxResponseBytes = 64 * 1024;
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public sealed record DeviceCode(string Value, string UserCode, string VerificationUri, DateTimeOffset ExpiresAt, int IntervalSeconds);
    public sealed record Tokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
    /// <summary>A ready-to-launch Minecraft session. <see cref="Uuid"/> has dashes; the refresh token is the long-lived secret to store.</summary>
    public sealed record Session(string AccessToken, DateTimeOffset ExpiresAt, string Uuid, string Name, string RefreshToken);

    private readonly string _clientId;
    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _now;

    public MicrosoftAuthService(string clientId, HttpClient? http = null, Func<DateTimeOffset>? now = null)
    {
        _clientId = (clientId ?? "").Trim();
        _http = http ?? SharedClient;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>An Azure "Application (client) ID" is a GUID.</summary>
    public static bool IsValidClientId(string? value) => Guid.TryParse(value?.Trim(), out var id) && id != Guid.Empty;

    /// <summary>Only Microsoft's own pages are ever opened for the player.</summary>
    public static bool IsTrustedVerificationUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) && (uri.Host is "www.microsoft.com" or "microsoft.com" or "login.live.com" or "login.microsoftonline.com" || uri.Host.EndsWith(".microsoft.com", StringComparison.Ordinal));

    private void RequireClientId()
    {
        if (!IsValidClientId(_clientId))
            throw new MicrosoftAuthException("client-id", "Microsoft sign-in needs the Application (client) ID of your own Azure app. Enter it in the Microsoft account section first.");
    }

    public async Task<DeviceCode> StartAsync(CancellationToken cancellationToken)
    {
        RequireClientId();
        using var document = await PostFormAsync(DeviceCodeUrl, new() { ["client_id"] = _clientId, ["scope"] = Scope }, cancellationToken, allowErrorBody: true);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out _)) throw Failure(root, "Microsoft did not start the sign-in.");
        string code = Text(root, "device_code"), user = Text(root, "user_code"), uri = Text(root, "verification_uri");
        int expires = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out int seconds) ? Math.Clamp(seconds, 30, 1800) : 900;
        int interval = root.TryGetProperty("interval", out var i) && i.TryGetInt32(out int step) ? Math.Clamp(step, 1, 30) : 5;
        if (code.Length == 0 || user.Length == 0 || !IsTrustedVerificationUri(uri)) throw new MicrosoftAuthException("device-code", "Microsoft sent an unexpected sign-in answer. Try again.");
        return new DeviceCode(code, user, uri, _now().AddSeconds(expires), interval);
    }

    /// <summary>Waits until the player has approved the code in the browser. <paramref name="delay"/> exists so tests do not really wait.</summary>
    public async Task<Tokens> WaitAsync(DeviceCode code, CancellationToken cancellationToken, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        RequireClientId();
        delay ??= (span, token) => Task.Delay(span, token);
        int interval = code.IntervalSeconds;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_now() >= code.ExpiresAt) throw new MicrosoftAuthException("expired", "The code expired before it was entered. Start the sign-in again.");
            await delay(TimeSpan.FromSeconds(interval), cancellationToken);
            using var document = await PostFormAsync(TokenUrl, new()
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code", ["client_id"] = _clientId, ["device_code"] = code.Value
            }, cancellationToken, allowErrorBody: true);
            var root = document.RootElement;
            if (!root.TryGetProperty("error", out var error)) return ReadTokens(root);
            switch (error.GetString())
            {
                case "authorization_pending": break;
                case "slow_down": interval = Math.Min(interval + 5, 60); break;
                case "authorization_declined": throw new MicrosoftAuthException("declined", "The sign-in was declined in the browser.");
                case "expired_token": throw new MicrosoftAuthException("expired", "The code expired before it was entered. Start the sign-in again.");
                default: throw Failure(root, "Microsoft refused the sign-in.");
            }
        }
    }

    public async Task<Tokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        RequireClientId();
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new MicrosoftAuthException("signed-out", "This account is not signed in. Sign in with Microsoft again.");
        using var document = await PostFormAsync(TokenUrl, new()
        {
            ["grant_type"] = "refresh_token", ["client_id"] = _clientId, ["refresh_token"] = refreshToken, ["scope"] = Scope
        }, cancellationToken, allowErrorBody: true);
        if (document.RootElement.TryGetProperty("error", out _))
            throw new MicrosoftAuthException("signed-out", "Your Microsoft sign-in has expired. Sign in with Microsoft again.");
        return ReadTokens(document.RootElement, refreshToken);
    }

    /// <summary>Microsoft token → Xbox → XSTS → Minecraft, then the ownership check and the profile.</summary>
    public async Task<Session> CompleteAsync(Tokens microsoft, CancellationToken cancellationToken)
    {
        using var xbox = await PostJsonAsync(XboxUserUrl, new
        {
            Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + microsoft.AccessToken },
            RelyingParty = "http://auth.xboxlive.com", TokenType = "JWT"
        }, null, cancellationToken);
        string xboxToken = Text(xbox.RootElement, "Token");
        if (xboxToken.Length == 0) throw new MicrosoftAuthException("xbox", "Xbox Live did not accept this Microsoft account.");

        using var xsts = await PostJsonAsync(XstsUrl, new
        {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xboxToken } },
            RelyingParty = "rp://api.minecraftservices.com/", TokenType = "JWT"
        }, null, cancellationToken, allowErrorBody: true);
        if (xsts.RootElement.TryGetProperty("XErr", out var xerr)) throw XstsFailure(xerr.ValueKind == JsonValueKind.Number && xerr.TryGetInt64(out long code) ? code : 0);
        string xstsToken = Text(xsts.RootElement, "Token"), hash = UserHash(xsts.RootElement);
        if (xstsToken.Length == 0 || hash.Length == 0) throw new MicrosoftAuthException("xsts", "Xbox Live did not accept this Microsoft account.");

        using var minecraft = await PostJsonAsync(MinecraftLoginUrl, new { identityToken = $"XBL3.0 x={hash};{xstsToken}" }, null, cancellationToken, allowErrorBody: true, forbiddenMessage:
            "Microsoft accepted you, but Minecraft refused this app registration. A new Azure app must be approved for Minecraft by Mojang before it can sign players in.");
        string accessToken = Text(minecraft.RootElement, "access_token");
        int lifetime = minecraft.RootElement.TryGetProperty("expires_in", out var life) && life.TryGetInt32(out int seconds) ? Math.Clamp(seconds, 60, 86400) : 86400;
        if (accessToken.Length == 0) throw new MicrosoftAuthException("minecraft", "Minecraft did not accept this sign-in.");

        using var entitlements = await GetJsonAsync(EntitlementsUrl, accessToken, cancellationToken);
        if (!OwnsJava(entitlements.RootElement))
            throw new MicrosoftAuthException("no-game", "This Microsoft account does not own Minecraft: Java Edition. Buy the game, or keep using a local profile.");

        using var profile = await GetJsonAsync(ProfileUrl, accessToken, cancellationToken, notFoundMessage:
            "This account owns Minecraft but has no Java profile yet. Open minecraft.net/msaprofile/mygames/editprofile once to choose a player name.");
        string id = Text(profile.RootElement, "id"), name = Text(profile.RootElement, "name");
        if (!TryDashedUuid(id, out string uuid) || !ValidName(name)) throw new MicrosoftAuthException("profile", "Minecraft sent an unexpected profile. Try again.");
        return new Session(accessToken, _now().AddSeconds(lifetime), uuid, name, microsoft.RefreshToken);
    }

    /// <summary>The whole chain for a stored refresh token; the returned session carries the newest refresh token.</summary>
    public async Task<Session> RefreshSessionAsync(string refreshToken, CancellationToken cancellationToken) =>
        await CompleteAsync(await RefreshAsync(refreshToken, cancellationToken), cancellationToken);

    internal static bool OwnsJava(JsonElement root)
    {
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return false;
        foreach (var item in items.EnumerateArray())
            if (item.TryGetProperty("name", out var name) && name.GetString() is "game_minecraft" or "product_minecraft") return true;
        return false;
    }

    internal static bool TryDashedUuid(string value, out string uuid)
    {
        uuid = "";
        if (value.Length != 32 || !value.All(Uri.IsHexDigit)) return false;
        uuid = Guid.ParseExact(value, "N").ToString();
        return true;
    }

    internal static bool ValidName(string value) => value.Length is >= 1 and <= 16 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private static MicrosoftAuthException XstsFailure(long code) => code switch
    {
        2148916233 => new("no-xbox", "This Microsoft account has no Xbox profile. Sign in once at xbox.com to create it, then try again."),
        2148916235 => new("region", "Xbox Live is not available in your country or region."),
        2148916236 or 2148916237 => new("adult", "Xbox needs an adult verification for this account before it can be used."),
        2148916238 => new("child", "This is a child account. An adult must add it to a Microsoft family group before it can play online."),
        _ => new("xsts", "Xbox Live refused this account (code " + code + ").")
    };

    private static string UserHash(JsonElement root) =>
        root.TryGetProperty("DisplayClaims", out var claims) && claims.TryGetProperty("xui", out var xui) && xui.ValueKind == JsonValueKind.Array && xui.GetArrayLength() > 0 &&
        xui[0].TryGetProperty("uhs", out var uhs) && uhs.GetString() is { } value && value.All(c => char.IsAsciiLetterOrDigit(c)) ? value : "";

    private Tokens ReadTokens(JsonElement root, string? previousRefresh = null)
    {
        string access = Text(root, "access_token"), refresh = Text(root, "refresh_token");
        if (refresh.Length == 0) refresh = previousRefresh ?? "";
        int seconds = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out int value) ? Math.Clamp(value, 60, 86400) : 3600;
        if (access.Length == 0 || refresh.Length == 0) throw new MicrosoftAuthException("token", "Microsoft sent an incomplete sign-in answer. Try again.");
        return new Tokens(access, refresh, _now().AddSeconds(seconds));
    }

    private static string Text(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static MicrosoftAuthException Failure(JsonElement root, string fallback) => new("microsoft", Text(root, "error") switch
    {
        "invalid_client" or "unauthorized_client" => "Microsoft does not accept this app ID. Check the Application (client) ID, and that the app allows \"personal Microsoft accounts\" and public client flows.",
        "invalid_scope" => "Microsoft refused the Xbox sign-in permission. The Azure app must allow personal accounts.",
        _ => fallback
    });

    private async Task<JsonDocument> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken, bool allowErrorBody = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        return await SendAsync(request, cancellationToken, allowErrorBody, null);
    }

    private async Task<JsonDocument> PostJsonAsync(string url, object body, string? bearer, CancellationToken cancellationToken, bool allowErrorBody = false, string? forbiddenMessage = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await SendAsync(request, cancellationToken, allowErrorBody, forbiddenMessage);
    }

    private async Task<JsonDocument> GetJsonAsync(string url, string bearer, CancellationToken cancellationToken, string? notFoundMessage = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await SendAsync(request, cancellationToken, false, null, notFoundMessage);
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken, bool allowErrorBody, string? forbiddenMessage, string? notFoundMessage = null)
    {
        HttpResponseMessage response;
        try { response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken); }
        catch (HttpRequestException) { throw new MicrosoftAuthException("network", "Could not reach Microsoft. Check your internet connection."); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new MicrosoftAuthException("network", "Microsoft did not answer in time. Try again."); }
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden && forbiddenMessage != null) throw new MicrosoftAuthException("forbidden", forbiddenMessage);
            if (response.StatusCode == HttpStatusCode.NotFound && notFoundMessage != null) throw new MicrosoftAuthException("not-found", notFoundMessage);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new MicrosoftAuthException("busy", "Microsoft asked to slow down. Wait a minute and try again.");
            if (!response.IsSuccessStatusCode && !allowErrorBody) throw new MicrosoftAuthException("http", $"Microsoft answered with an error ({(int)response.StatusCode}). Try again later.");
            if (response.Content.Headers.ContentLength > MaxResponseBytes) throw new MicrosoftAuthException("size", "Microsoft sent an unexpectedly large answer.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new MemoryStream();
            var chunk = new byte[8192]; int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxResponseBytes) throw new MicrosoftAuthException("size", "Microsoft sent an unexpectedly large answer.");
                buffer.Write(chunk, 0, read);
            }
            buffer.Position = 0;
            try { return await JsonDocument.ParseAsync(buffer, cancellationToken: cancellationToken); }
            catch (JsonException) { throw new MicrosoftAuthException("format", "Microsoft sent an unreadable answer. Try again."); }
        }
    }
}
