using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace VortexLauncher.Core.Accounts;

public sealed record VortexIdentity(string Id, string Login, string Nickname);
public sealed record VortexSession(VortexIdentity Profile, string Token, long Expires)
{
    public override string ToString() => "Vortex session (credentials redacted)";
}

public sealed class VortexAccountClient
{
    private static readonly Uri Origin = new("https://vortex-accounts.forge93-m.workers.dev/v3/accounts/");
    private static readonly HttpClient Shared = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _http;
    public VortexAccountClient() : this(Shared) { }
    internal VortexAccountClient(HttpClient http) => _http = http;
    public static bool ValidLogin(string? value) => value!=null && Regex.IsMatch(value, "^[A-Za-z0-9_]{3,24}\\z", RegexOptions.CultureInvariant);
    public static bool ValidNickname(string? value) => value!=null && Regex.IsMatch(value, "^[A-Za-z0-9_]{3,16}\\z", RegexOptions.CultureInvariant);
    public static bool ValidToken(string? value) => value!=null && Regex.IsMatch(value, "^[a-f0-9]{64}\\z", RegexOptions.CultureInvariant);
    public async Task<VortexSession> AuthenticateAsync(string login, string password, string? nickname, CancellationToken token = default)
    {
        if (!ValidLogin(login) || password.Length > 128) throw new InvalidOperationException("Use a 3–24 character login: letters, numbers or underscore.");
        if (nickname != null && (!ValidNickname(nickname) || password.EnumerateRunes().Count() < 15)) throw new InvalidOperationException("Use a 3–16 character nickname and a password of at least 15 characters.");
        using var result = await SendAsync(nickname == null ? "login" : "register", HttpMethod.Post, new { login, password, nickname }, null, token);
        var profile = ParseProfile(result.RootElement);
        if (!result.RootElement.TryGetProperty("token", out var credential) || credential.ValueKind != JsonValueKind.String ||
            !result.RootElement.TryGetProperty("expires", out var expiry) || !expiry.TryGetInt64(out var expires))
            throw new InvalidOperationException("The account service returned an invalid session.");
        var session = new VortexSession(profile, credential.GetString() ?? "", expires);
        if (!ValidToken(session.Token) || session.Expires <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) throw new InvalidOperationException("The account service returned an invalid session.");
        return session;
    }
    public async Task<VortexIdentity> MeAsync(VortexSession session, CancellationToken token = default)
    {
        using var result = await SendAsync("me", HttpMethod.Get, null, session.Token, token);
        var profile = ParseProfile(result.RootElement);
        if (profile.Id != session.Profile.Id) throw new InvalidOperationException("The account service returned a different identity. Sign in again.");
        return profile;
    }
    public async Task<VortexIdentity> RenameAsync(VortexSession session, string nickname, CancellationToken token = default)
    {
        if (!ValidNickname(nickname)) throw new InvalidOperationException("Use 3–16 characters: letters, numbers or underscore.");
        using var result = await SendAsync("nickname", HttpMethod.Put, new { nickname }, session.Token, token);
        var profile = ParseProfile(result.RootElement);
        if (profile.Id != session.Profile.Id) throw new InvalidOperationException("The account service returned a different identity.");
        return profile;
    }
    public async Task LogoutAsync(VortexSession session, CancellationToken token = default)
    {
        try { using var result = await SendAsync("logout", HttpMethod.Post, new { }, session.Token, token); }
        catch (UnauthorizedAccessException) { /* Already revoked or expired. */ }
    }
    public async Task<string> WardrobeSessionAsync(VortexSession session, string uuid, CancellationToken token = default)
    {
        if (!Guid.TryParse(uuid,out var profile) || profile==Guid.Empty) throw new InvalidOperationException("Choose a valid player profile first.");
        using var result=await SendAsync("wardrobe-session",HttpMethod.Post,new {uuid=profile.ToString()},session.Token,token);
        if(!result.RootElement.TryGetProperty("token",out var value) || value.ValueKind!=JsonValueKind.String || !ValidToken(value.GetString()))throw new InvalidOperationException("The wardrobe service returned invalid access.");
        return value.GetString()!;
    }
    private async Task<JsonDocument> SendAsync(string route, HttpMethod method, object? body, string? session, CancellationToken cancellation)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var transportToken=timeout.Token;
        using var request = new HttpRequestMessage(method, new Uri(Origin, route));
        if (body != null) request.Content = JsonContent.Create(body);
        if (session != null)
        {
            if (!ValidToken(session)) throw new UnauthorizedAccessException("Your session is invalid. Sign in again.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session);
        }
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, transportToken);
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                throw new InvalidOperationException("Account service redirects are not allowed.");
            using var input = await response.Content.ReadAsStreamAsync(transportToken);
            using var bytes = new MemoryStream();
            byte[] buffer = new byte[4096]; int read;
            while ((read = await input.ReadAsync(buffer, transportToken)) > 0)
            {
                if (bytes.Length + read > 16384) throw new InvalidOperationException("The account service response was too large.");
                await bytes.WriteAsync(buffer.AsMemory(0, read), transportToken);
            }
            JsonDocument parsed;
            try { parsed = JsonDocument.Parse(bytes.ToArray()); }
            catch (JsonException) { throw new InvalidOperationException("The account service returned an invalid response."); }
            if(parsed.RootElement.ValueKind!=JsonValueKind.Object){parsed.Dispose();throw new InvalidOperationException("The account service returned an invalid response.");}
            if (response.IsSuccessStatusCode) return parsed;
            string code = parsed.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String ? error.GetString() ?? "" : "";
            if (response.StatusCode == HttpStatusCode.TooManyRequests && code == "rate_limited")
            {
                long seconds = response.Headers.RetryAfter?.Delta is { } delay ? (long)Math.Ceiling(delay.TotalSeconds) : 0;
                if (seconds <= 0 && parsed.RootElement.TryGetProperty("retry_after", out var retry) && retry.ValueKind == JsonValueKind.Number && retry.TryGetInt64(out var fallback)) seconds = fallback;
                parsed.Dispose();
                if (seconds > 0 && seconds <= 86400)
                {
                    var remaining=TimeSpan.FromSeconds(seconds);
                    throw new InvalidOperationException($"Too many attempts. Try again in {(int)remaining.TotalMinutes} min {remaining.Seconds} sec. Repeated clicks will not shorten the wait.");
                }
                throw new InvalidOperationException("Too many attempts. Please wait before trying again.");
            }
            parsed.Dispose();
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException(session == null ? "Incorrect login or password." : "Your session expired or was revoked. Sign in again.");
            throw new InvalidOperationException(code switch
            {
                "accounts_not_enabled" => "Vortex accounts are not enabled on the server yet. Local player profiles still work.",
                "name_unavailable" => "That Vortex login or nickname is unavailable. Choose another one.",
                "invalid_registration" => "Use a 3–16 character nickname and a password of at least 15 characters.",
                "invalid_nickname" => "Use 3–16 characters: letters, numbers or underscore.",
                "rate_limited" => "Too many attempts. Please wait before trying again.",
                _ => "The account service is unavailable. Your local profiles have not changed."
            });
        }
        catch (HttpRequestException) { throw new InvalidOperationException("Could not connect to the account service. Check your connection."); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new InvalidOperationException("The account service timed out. Please try again."); }
    }
    private static VortexIdentity ParseProfile(JsonElement root)
    {
        try
        {
            var p = root.GetProperty("profile");
            var id = p.GetProperty("id").GetString() ?? ""; var login = p.GetProperty("login").GetString() ?? ""; var nickname = p.GetProperty("nickname").GetString() ?? "";
            if (!Guid.TryParse(id, out var guid) || guid == Guid.Empty || !ValidLogin(login) || !ValidNickname(nickname)) throw new FormatException();
            return new VortexIdentity(guid.ToString(), login, nickname);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException) { throw new InvalidOperationException("The account service returned an invalid profile."); }
    }
}
