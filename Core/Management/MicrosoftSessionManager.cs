using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VortexLauncher.Core.Network;

namespace VortexLauncher.Core.Management;

/// <summary>
/// Keeps Microsoft players signed in: the Minecraft access token lives only in memory and is renewed from the stored
/// refresh token shortly before it expires. A player whose sign-in cannot be renewed has to sign in again.
/// </summary>
public sealed class MicrosoftSessionManager
{
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(5);
    private readonly MicrosoftTokenStore _store;
    private readonly Func<string, MicrosoftAuthService> _service;
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, MicrosoftAuthService.Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MicrosoftSessionManager(MicrosoftTokenStore store, Func<string, MicrosoftAuthService>? service = null, Func<DateTimeOffset>? now = null)
    {
        _store = store;
        _service = service ?? (clientId => new MicrosoftAuthService(clientId));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>False when this system cannot protect the long-lived token, so the player signs in again each time the launcher starts.</summary>
    public bool CanPersist => _store.CanPersist;

    /// <summary>Called right after a successful sign-in.</summary>
    public void Remember(MicrosoftAuthService.Session session)
    {
        lock (_sessions) _sessions[session.Uuid] = session;
        _store.Save(session.Uuid, session.RefreshToken);
    }

    public void Forget(string uuid)
    {
        lock (_sessions) _sessions.Remove(uuid);
        _store.Remove(uuid);
    }

    public async Task<MicrosoftAuthService.Session> GetAsync(string uuid, string clientId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            MicrosoftAuthService.Session? cached;
            lock (_sessions) _sessions.TryGetValue(uuid, out cached);
            if (cached != null && cached.ExpiresAt - _now() > RenewBefore) return cached;
            string? refresh = cached?.RefreshToken ?? _store.Get(uuid);
            if (string.IsNullOrEmpty(refresh))
                throw new MicrosoftAuthException("signed-out", "This Microsoft account is not signed in on this computer. Sign in again on the Profiles page.");
            MicrosoftAuthService.Session renewed;
            try { renewed = await _service(clientId).RefreshSessionAsync(refresh, cancellationToken); }
            catch (MicrosoftAuthException ex) when (ex.Code == "signed-out") { Forget(uuid); throw; }
            if (!string.Equals(renewed.Uuid, uuid, StringComparison.OrdinalIgnoreCase))
                throw new MicrosoftAuthException("account", "The saved sign-in belongs to a different Microsoft account. Sign in again.");
            Remember(renewed);
            return renewed;
        }
        finally { _gate.Release(); }
    }
}
