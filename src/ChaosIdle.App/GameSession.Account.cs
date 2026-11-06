using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChaosIdle.App.Cloud;

namespace ChaosIdle.App
{
    public enum AccountStep
    {
        Done,
        /// <summary>This Google account already has a game account; the player may switch to it (<see cref="GameSession.SwitchAccountAsync"/>).</summary>
        CanSwitch,
        Failed,
    }

    public sealed record AccountResult(AccountStep Step, string Message);

    /// <summary>What the account screen shows.</summary>
    public sealed record AccountInfo(bool Connected, bool IsGuest, string? GoogleEmail, bool GoogleLinked, string AccountId)
    {
        public static readonly AccountInfo None = new AccountInfo(false, true, null, false, "");
    }

    /// <summary>
    /// Sign-in with Google (an ID token from the platform) on top of the cloud account:
    /// a guest links Google and keeps its progress; a phone without an account, or one that
    /// chooses to switch, signs in to the Google account's game; signing out keeps the local game.
    /// </summary>
    public sealed partial class GameSession
    {
        public const string Google = "google";

        private string? _pendingSwitchToken;

        public AccountInfo Account { get; private set; } = AccountInfo.None;

        /// <summary>Reads the account from the server (who is signed in, is it a guest).</summary>
        public async Task<AccountInfo> RefreshAccountAsync(CancellationToken cancel = default)
        {
            if (_api == null || _link == null) return Account = AccountInfo.None;
            var me = await _api.GetAccountAsync(cancel);
            if (me.Status == CallStatus.SignedOut) Cloud = Failed(me.Status, me.Message);
            if (!me.Ok) return Account = _link == null ? AccountInfo.None : Account;

            var google = me.Value!.SignIns.FirstOrDefault(i => i.Provider == Google);
            return Account = new AccountInfo(true, me.Value.IsGuest, google?.Email, google != null, me.Value.AccountId);
        }

        /// <summary>
        /// The player signed in with Google on this phone. A guest account gets Google linked (same
        /// progress); a phone without a cloud account signs in to that Google account's game.
        /// </summary>
        public async Task<AccountResult> SignInWithGoogleAsync(string idToken, DateTime nowUtc, CancellationToken cancel = default)
        {
            if (_api == null) throw new InvalidOperationException("Call UseCloud first.");
            if (_link == null) return await SignInTo(idToken, nowUtc, cancel);

            var link = await _api.LinkAsync(Google, idToken, cancel);
            if (link.Ok)
            {
                await RefreshAccountAsync(cancel);
                return new AccountResult(AccountStep.Done, "Google is linked. Sign in with it on any phone to get this game.");
            }

            if (link.Status == CallStatus.Conflict && Reason(link) == "identity_in_use")
            {
                _pendingSwitchToken = idToken;
                return new AccountResult(AccountStep.CanSwitch,
                    "This Google account already has a game. Switch to it? You'll choose which save to keep; this phone's current account is left behind.");
            }

            if (link.Status == CallStatus.Conflict) return new AccountResult(AccountStep.Failed, "This game is already linked to a different Google account.");
            if (link.Status == CallStatus.SignedOut) Cloud = Failed(link.Status, link.Message);
            return new AccountResult(AccountStep.Failed, SignInProblem(link.Status, link.Message));
        }

        /// <summary>After <see cref="AccountStep.CanSwitch"/>: move this phone to the Google account's game.</summary>
        public Task<AccountResult> SwitchAccountAsync(DateTime nowUtc, CancellationToken cancel = default)
        {
            string? token = _pendingSwitchToken;
            _pendingSwitchToken = null;
            return token == null
                ? Task.FromResult(new AccountResult(AccountStep.Failed, "Sign in with Google again."))
                : SignInTo(token, nowUtc, cancel);
        }

        public void CancelSwitch() => _pendingSwitchToken = null;

        /// <summary>
        /// Signs this phone out (or every phone). The game stays on this phone; the cloud stops
        /// syncing until the player signs in again. Guests can't sign out: they would lose the account.
        /// </summary>
        public async Task<AccountResult> SignOutAsync(bool everywhere, CancellationToken cancel = default)
        {
            if (_api == null || _link == null) return new AccountResult(AccountStep.Failed, "Not signed in.");
            var result = await _api.SignOutAsync(everywhere, cancel);
            if (result.Status == CallStatus.Conflict)
                return new AccountResult(AccountStep.Failed, "Link Google first: signing out a guest would lose the account.");
            if (!result.Ok && result.Status != CallStatus.SignedOut)
                return new AccountResult(AccountStep.Failed, Explain(result.Status, result.Message));

            ForgetLink();
            Cloud = Status(CloudState.NotConnected, "Signed out. Your game stays on this phone.");
            return new AccountResult(AccountStep.Done, everywhere ? "Signed out on every phone." : "Signed out on this phone.");
        }

        private async Task<AccountResult> SignInTo(string idToken, DateTime nowUtc, CancellationToken cancel)
        {
            var signedIn = await _api!.SignInAsync(Google, idToken, cancel);
            if (!signedIn.Ok) return new AccountResult(AccountStep.Failed, SignInProblem(signedIn.Status, signedIn.Message));

            // A new link: this phone has matched nothing in that account yet, so a sync either
            // uploads (empty account) or asks the player which save to keep.
            _link = new CloudLink { AccountId = signedIn.Value!.AccountId, Token = signedIn.Value.Token };
            _api.Token = _link.Token;
            SaveLink();
            await SyncAsync(nowUtc, cancel);
            await RefreshAccountAsync(cancel);
            return new AccountResult(AccountStep.Done, signedIn.Value.Created
                ? "Signed in with Google. This game is now saved to your Google account."
                : "Signed in with Google.");
        }

        private void ForgetLink()
        {
            _link = null;
            _pendingSwitchToken = null;
            _conflictCloud = null;
            PendingConflict = null;
            if (_api != null) _api.Token = null;
            if (File.Exists(LinkPath)) File.Delete(LinkPath);
            Account = AccountInfo.None;
        }

        private static string? Reason<T>(CallResult<T> r) =>
            r.Body is { } body && body.TryGetProperty("details", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.Object
            && d.TryGetProperty("reason", out var reason) ? reason.GetString() : null;

        private static string SignInProblem(CallStatus status, string message) => status switch
        {
            CallStatus.Offline => "No connection. Try again when you're online.",
            CallStatus.SignedOut => "Google sign-in didn't work. Try again.",
            CallStatus.NotFound => "Google sign-in isn't available on this server.",
            _ => message,
        };
    }
}
