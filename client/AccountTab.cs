using System;
using System.Threading.Tasks;
using ChaosIdle.App;
using Godot;

namespace ChaosIdle.Client;

/// <summary>
/// The account screen: who is signed in, "Sign in with Google" (links a guest, or signs this
/// phone in to the Google account's game), the switch offer when that Google account already has
/// a game, and sign out. The rules are in GameSession.Account.cs; this only shows them.
/// </summary>
public partial class AccountTab : VBoxContainer
{
    private readonly Func<GameSession?> _session;
    private readonly Action _changed;
    private readonly Label _who = Wrapping();
    private readonly Label _message = Wrapping();
    private readonly Button _google = new() { Text = "Sign in with Google" };
    private readonly Label _googleHint = Wrapping();
    private readonly HBoxContainer _switch = new() { Visible = false };
    private readonly Button _signOut = new() { Text = "Sign out of this phone" };
    private readonly Button _signOutAll = new() { Text = "Sign out on every phone" };
    private readonly CloudPanel _cloud;
    private bool _busy;

    public AccountTab(Func<GameSession?> session, Action changed)
    {
        Name = "Account";
        _session = session;
        _changed = changed;
        AddThemeConstantOverride("separation", 14);

        AddChild(new Label { Text = "Account" });
        AddChild(_who);
        _cloud = new CloudPanel(session, changed);
        AddChild(_cloud);
        AddChild(_google);
        AddChild(_googleHint);

        var switchTo = new Button { Text = "Switch to that account", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var cancel = new Button { Text = "Stay here", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _switch.AddChild(switchTo);
        _switch.AddChild(cancel);
        AddChild(_switch);
        AddChild(_message);
        AddChild(_signOut);
        AddChild(_signOutAll);

        _google.Pressed += () => Run(SignInWithGoogle);
        switchTo.Pressed += () => Run(async s => Show(await s.SwitchAccountAsync(DateTime.UtcNow)));
        cancel.Pressed += () =>
        {
            _session()?.CancelSwitch();
            _switch.Visible = false;
            _message.Text = "";
        };
        _signOut.Pressed += () => Run(s => SignOut(s, everywhere: false));
        _signOutAll.Pressed += () => Run(s => SignOut(s, everywhere: true));
    }

    public bool CloudEnabled
    {
        get => _cloud.Enabled;
        set => _cloud.Enabled = value;
    }

    /// <summary>Shows what is known now and asks the server who is signed in.</summary>
    public async void Refresh()
    {
        Render();
        var s = _session();
        if (s == null || !CloudEnabled || !s.HasCloudAccount || _busy) return;
        await s.RefreshAccountAsync();
        Render();
    }

    private void Render()
    {
        _cloud.Refresh();
        var s = _session();
        if (s == null) return;
        var a = s.Account;
        bool cloud = CloudEnabled;

        _who.Text = !cloud ? "Cloud save isn't set up in this build, so your game is only on this phone."
            : !s.HasCloudAccount ? "Not signed in. Your game is only on this phone."
            : a.GoogleLinked ? $"Signed in with Google{(a.GoogleEmail != null ? " as " + a.GoogleEmail : "")}. Sign in on any phone to get this game."
            : "Guest account: your game is in the cloud, but only this phone can reach it. Link Google to keep it if you lose or change phones.";

        string? why = GoogleSignIn.Unavailable;
        _google.Visible = cloud && !a.GoogleLinked;
        _google.Text = s.HasCloudAccount ? "Link Google to this game" : "Sign in with Google";
        _google.Disabled = _busy || why != null;
        _googleHint.Visible = _google.Visible && why != null;
        _googleHint.Text = why ?? "";

        _signOut.Visible = _signOutAll.Visible = cloud && a.GoogleLinked;
        _signOut.Disabled = _signOutAll.Disabled = _busy;
    }

    private async Task SignInWithGoogle(GameSession s)
    {
        var (token, error) = await GoogleSignIn.SignInAsync();
        if (token == null)
        {
            _message.Text = GoogleSignIn.Explain(error);
            return;
        }

        Show(await s.SignInWithGoogleAsync(token, DateTime.UtcNow));
    }

    private async Task SignOut(GameSession s, bool everywhere)
    {
        var result = await s.SignOutAsync(everywhere);
        if (result.Step == AccountStep.Done) GoogleSignIn.SignOut();
        Show(result);
    }

    private void Show(AccountResult result)
    {
        _message.Text = result.Message;
        _switch.Visible = result.Step == AccountStep.CanSwitch;
        if (_session()?.PendingConflict != null) _message.Text += "\nChoose which save to keep above.";
    }

    private async void Run(Func<GameSession, Task> step)
    {
        var s = _session();
        if (s == null || _busy) return;
        _busy = true;
        Render();
        try
        {
            await step(s);
        }
        finally
        {
            _busy = false;
            Render();
            _changed();
        }
    }

    private static Label Wrapping() => new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
}
