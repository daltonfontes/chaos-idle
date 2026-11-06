using ChaosIdle.App;
using ChaosIdle.Content;
using Xunit;

namespace ChaosIdle.Tests;

/// <summary>
/// The account screen's flows in <see cref="GameSession"/> against the real server, with Google
/// ID tokens signed by the test identity provider. Part of <see cref="ServerTests"/>, so it also
/// runs on PostgreSQL.
/// </summary>
public partial class ServerTests
{
    private string Google(string subject, string? email = null) => TestIdentityProvider.Token(_factory.Time.Now, subject, email);

    [SkippableFact]
    public async Task A_guest_links_Google_and_a_reinstalled_phone_gets_the_game_back()
    {
        var (phone, _) = await ConnectedPhone();
        Play(phone);
        await phone.SyncAsync(Now);
        Assert.True((await phone.RefreshAccountAsync()).IsGuest);

        var linked = await phone.SignInWithGoogleAsync(Google("g-1", "ana@example.com"), Now);
        Assert.Equal(AccountStep.Done, linked.Step);
        Assert.Equal(new AccountInfo(true, false, "ana@example.com", true, phone.Account.AccountId), phone.Account);

        // Reinstall: a fresh phone with a new game signs in with Google.
        var reinstalled = Phone();
        Assert.False(reinstalled.HasCloudAccount);
        var signedIn = await reinstalled.SignInWithGoogleAsync(Google("g-1"), Now);
        Assert.Equal(AccountStep.Done, signedIn.Step);
        Assert.Equal(phone.Account.AccountId, reinstalled.Account.AccountId);
        Assert.Equal(CloudState.Conflict, reinstalled.Cloud.State); // its fresh game vs the cloud: the player picks

        await reinstalled.ResolveConflictAsync(keepThisDevice: false, Now);
        Assert.Equal(Checksum(phone), Checksum(reinstalled));
    }

    [SkippableFact]
    public async Task Signing_in_on_a_phone_without_an_account_uploads_its_game_to_a_new_one()
    {
        var phone = Phone();
        Play(phone);
        var result = await phone.SignInWithGoogleAsync(Google("g-2"), Now);
        Assert.Equal(AccountStep.Done, result.Step);
        Assert.Contains("now saved", result.Message);
        Assert.False(phone.Account.IsGuest);
        Assert.Equal(CloudState.Synced, phone.Cloud.State);
        _linkPaths[phone] = Path.Combine(_deviceDirs[^1], GameSession.CloudFile);
        Assert.Equal(Checksum(phone), await CloudChecksum(phone));
    }

    [SkippableFact]
    public async Task A_guest_whose_Google_has_another_game_can_switch_to_it()
    {
        var (owner, _) = await ConnectedPhone();
        await owner.SignInWithGoogleAsync(Google("g-3"), Now);
        Play(owner);
        await owner.SyncAsync(Now);

        var (guest, _) = await ConnectedPhone();
        var offer = await guest.SignInWithGoogleAsync(Google("g-3"), Now);
        Assert.Equal(AccountStep.CanSwitch, offer.Step);
        Assert.True((await guest.RefreshAccountAsync()).IsGuest); // nothing changed yet

        var switched = await guest.SwitchAccountAsync(Now);
        Assert.Equal(AccountStep.Done, switched.Step);
        Assert.Equal(owner.Account.AccountId, guest.Account.AccountId);
        await guest.ResolveConflictAsync(keepThisDevice: false, Now);
        Assert.Equal(Checksum(owner), Checksum(guest));

        Assert.Equal(AccountStep.Failed, (await guest.SwitchAccountAsync(Now)).Step); // the offer is used up
    }

    [SkippableFact]
    public async Task Bad_Google_tokens_change_nothing()
    {
        var (phone, _) = await ConnectedPhone();
        var forged = TestIdentityProvider.Token(_factory.Time.Now, "g-4", key: TestIdentityProvider.OtherKey);
        var result = await phone.SignInWithGoogleAsync(forged, Now);
        Assert.Equal(AccountStep.Failed, result.Step);
        Assert.True((await phone.RefreshAccountAsync()).IsGuest);

        var fresh = Phone();
        Assert.Equal(AccountStep.Failed, (await fresh.SignInWithGoogleAsync("not-a-token", Now)).Step);
        Assert.False(fresh.HasCloudAccount);
    }

    [SkippableFact]
    public async Task Signing_out_keeps_the_game_and_guests_cannot_sign_out()
    {
        var (guest, _) = await ConnectedPhone();
        var refused = await guest.SignOutAsync(everywhere: false);
        Assert.Equal(AccountStep.Failed, refused.Step);
        Assert.True(guest.HasCloudAccount);

        var phone = Phone();
        await phone.SignInWithGoogleAsync(Google("g-5"), Now);
        var tablet = Phone();
        await tablet.SignInWithGoogleAsync(Google("g-5"), Now);
        string game = Checksum(phone);

        Assert.Equal(AccountStep.Done, (await phone.SignOutAsync(everywhere: false)).Step);
        Assert.False(phone.HasCloudAccount);
        Assert.Equal(CloudState.NotConnected, phone.Cloud.State);
        Assert.Equal(game, Checksum(phone));
        Assert.True((await tablet.RefreshAccountAsync()).Connected); // only this phone

        Assert.Equal(AccountStep.Done, (await tablet.SignOutAsync(everywhere: true)).Step);
        Assert.False(tablet.HasCloudAccount);
    }
}
