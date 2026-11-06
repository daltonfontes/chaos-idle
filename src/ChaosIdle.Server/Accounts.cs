namespace ChaosIdle.Server;

public sealed record SignedIn(string AccountId, string Token, bool Created, IReadOnlyList<string> SignIns);

public sealed record IdentityView(string Provider, string? Email, DateTimeOffset LinkedAt);

/// <param name="IsGuest">True while no platform sign-in is linked: the device token is then the only way back in.</param>
public sealed record AccountView(string AccountId, DateTimeOffset CreatedAt, bool IsGuest, IReadOnlyList<IdentityView> SignIns);

public sealed record SignInRequest(string? IdToken);

public sealed record SignedOut(int TokensRevoked);

/// <summary>
/// Accounts and sign-in. A player starts as a guest (a device token only). Linking Google or
/// Apple keeps the same account and its progress, and lets the player sign in on another device
/// or after reinstalling; each device gets its own token, which can be revoked.
/// </summary>
public sealed class AccountService
{
    private const string Unavailable = "Sign-in is unavailable right now. Try again in a minute.";

    private readonly IServerStore _store;
    private readonly IIdentityVerifier _verifier;
    private readonly TimeProvider _time;

    public AccountService(IServerStore store, IIdentityVerifier verifier, TimeProvider time)
    {
        _store = store;
        _verifier = verifier;
        _time = time;
    }

    public SignedIn CreateGuest()
    {
        var account = new Account("acc_" + Guid.NewGuid().ToString("N"), _time.GetUtcNow());
        _store.AddAccount(account);
        return new SignedIn(account.Id, IssueToken(account.Id), Created: true, Array.Empty<string>());
    }

    /// <summary>
    /// Signs in with a platform ID token: the linked account if there is one, else a new account
    /// linked to it. Returns a new device token either way.
    /// </summary>
    public async Task<ServiceResult<SignedIn>> SignInAsync(string provider, string? idToken, CancellationToken cancel = default)
    {
        if (!_verifier.IsEnabled(provider)) return new(Outcome.NotFound, null, $"Sign-in with '{provider}' is not available.");
        VerifiedIdentity? identity;
        try { identity = await _verifier.VerifyAsync(provider, idToken ?? "", cancel); }
        catch (SignInUnavailableException) { return new(Outcome.Unavailable, null, Unavailable); }
        if (identity == null) return new(Outcome.Unauthorized, null, "The sign-in token is not valid. Sign in again.");

        bool created = false;
        var linked = _store.FindIdentity(provider, identity.Subject);
        if (linked == null)
        {
            var account = new Account("acc_" + Guid.NewGuid().ToString("N"), _time.GetUtcNow());
            _store.AddAccount(account);
            if (_store.TryLinkIdentity(new LinkedIdentity(provider, identity.Subject, account.Id, identity.Email, _time.GetUtcNow())))
                created = true;
            // else: a parallel sign-in with the same identity won; use its account (this new one stays empty, with no token).
            linked = _store.FindIdentity(provider, identity.Subject)!;
        }

        return new(Outcome.Ok, new SignedIn(linked.AccountId, IssueToken(linked.AccountId), created, Providers(linked.AccountId)));
    }

    /// <summary>Links a platform sign-in to the signed-in account (typically a guest), keeping its progress.</summary>
    public async Task<ServiceResult<AccountView>> LinkAsync(Account account, string provider, string? idToken, CancellationToken cancel = default)
    {
        if (!_verifier.IsEnabled(provider)) return new(Outcome.NotFound, null, $"Sign-in with '{provider}' is not available.");
        VerifiedIdentity? identity;
        try { identity = await _verifier.VerifyAsync(provider, idToken ?? "", cancel); }
        catch (SignInUnavailableException) { return new(Outcome.Unavailable, null, Unavailable); }
        if (identity == null) return new(Outcome.Unauthorized, null, "The sign-in token is not valid. Sign in again.");

        return _store.WithAccountLock(account.Id, () =>
        {
            var existing = _store.FindIdentity(provider, identity.Subject);
            if (existing != null && existing.AccountId == account.Id) return new ServiceResult<AccountView>(Outcome.Ok, View(account)); // already linked
            if (existing != null)
                return new ServiceResult<AccountView>(Outcome.Conflict, null,
                    "This sign-in already belongs to another game account. Sign in with it to switch to that account; this device's guest progress would stay behind.",
                    new { reason = "identity_in_use" });
            if (_store.IdentitiesOf(account.Id).Any(i => i.Provider == provider))
                return new ServiceResult<AccountView>(Outcome.Conflict, null,
                    $"This account is already linked to a different {provider} sign-in.", new { reason = "provider_already_linked" });

            if (!_store.TryLinkIdentity(new LinkedIdentity(provider, identity.Subject, account.Id, identity.Email, _time.GetUtcNow())))
                return new ServiceResult<AccountView>(Outcome.Conflict, null,
                    "This sign-in was just linked to another game account.", new { reason = "identity_in_use" });
            return new ServiceResult<AccountView>(Outcome.Ok, View(account));
        });
    }

    public AccountView View(Account account)
    {
        var ids = _store.IdentitiesOf(account.Id);
        return new AccountView(account.Id, account.CreatedAt, ids.Count == 0,
            ids.Select(i => new IdentityView(i.Provider, i.Email, i.LinkedAt)).ToList());
    }

    /// <summary>Revokes this device's token, or every token. Refused for guests: they would lose the account.</summary>
    public ServiceResult<SignedOut> SignOut(Account account, string tokenHash, bool everywhere)
    {
        if (_store.IdentitiesOf(account.Id).Count == 0)
            return new(Outcome.Conflict, null,
                "This is a guest account: signing out would lose it. Link Google or Apple first.", new { reason = "guest_account" });

        var now = _time.GetUtcNow();
        if (everywhere) return new(Outcome.Ok, new SignedOut(_store.RevokeAllTokens(account.Id, now)));
        _store.RevokeToken(tokenHash, now);
        return new(Outcome.Ok, new SignedOut(1));
    }

    private string IssueToken(string accountId)
    {
        string token = Auth.NewToken();
        _store.AddToken(new DeviceToken(Auth.Hash(token), accountId, _time.GetUtcNow()));
        return token;
    }

    private IReadOnlyList<string> Providers(string accountId) => _store.IdentitiesOf(accountId).Select(i => i.Provider).ToList();
}
