using System;
using System.Threading.Tasks;
using ChaosIdle.App;
using Godot;

namespace ChaosIdle.Client;

/// <summary>
/// One row on the Hero tab: the cloud save status in words and the one action that fits it
/// (connect, sync, or choose a save after a conflict). All rules live in <see cref="GameSession"/>.
/// </summary>
public partial class CloudPanel : VBoxContainer
{
    private readonly Func<GameSession?> _session;
    private readonly Action _changed;
    private readonly Label _status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Button _action = new();
    private readonly Label _conflict = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
    private readonly HBoxContainer _choices = new() { Visible = false };
    private bool _busy;

    /// <param name="changed">Called after a sync may have replaced the game, so the screens refresh.</param>
    public CloudPanel(Func<GameSession?> session, Action changed)
    {
        _session = session;
        _changed = changed;

        var row = new HBoxContainer();
        row.AddChild(_status);
        row.AddChild(_action);
        AddChild(row);
        AddChild(_conflict);

        var keepHere = new Button { Text = "Keep this phone's save", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var useCloud = new Button { Text = "Use the cloud save", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        keepHere.Pressed += () => Run(s => s.ResolveConflictAsync(keepThisDevice: true, DateTime.UtcNow));
        useCloud.Pressed += () => Run(s => s.ResolveConflictAsync(keepThisDevice: false, DateTime.UtcNow));
        _choices.AddChild(keepHere);
        _choices.AddChild(useCloud);
        AddChild(_choices);

        _action.Pressed += () => Run(s => s.HasCloudAccount ? s.SyncAsync(DateTime.UtcNow) : s.ConnectCloudAsync(DateTime.UtcNow));
        Refresh();
    }

    /// <summary>True when this build has a server address; otherwise the panel explains and stays idle.</summary>
    public bool Enabled { get; set; }

    public async Task SyncNow()
    {
        var s = _session();
        if (s == null || !Enabled || !s.HasCloudAccount) return;
        await RunAsync(x => x.SyncAsync(DateTime.UtcNow));
    }

    public async Task ConnectNow()
    {
        if (_session() == null || !Enabled) return;
        await RunAsync(x => x.ConnectCloudAsync(DateTime.UtcNow));
    }

    public void Refresh()
    {
        var s = _session();
        if (!Enabled || s == null)
        {
            _status.Text = "Cloud save: not set up in this build.";
            _action.Visible = false;
            _conflict.Visible = _choices.Visible = false;
            return;
        }

        var cloud = s.Cloud;
        string revision = cloud.Revision > 0 ? $" (#{cloud.Revision})" : "";
        _status.Text = cloud.State switch
        {
            CloudState.Synced => "Cloud: " + cloud.Message + revision,
            CloudState.NotConnected => "Cloud: off. " + cloud.Message,
            _ => "Cloud: " + cloud.Message,
        };
        _action.Visible = cloud.State != CloudState.Conflict;
        _action.Text = _busy ? "Syncing…" : s.HasCloudAccount ? "Sync now" : "Save to cloud";
        _action.Disabled = _busy;

        var conflict = s.PendingConflict;
        _conflict.Visible = _choices.Visible = conflict != null;
        if (conflict != null) _conflict.Text = conflict.ThisDevice.Replace("This device", "This phone") + "\n" + conflict.Cloud;
    }

    private async void Run(Func<GameSession, Task<CloudStatus>> step) => await RunAsync(step);

    private async Task RunAsync(Func<GameSession, Task<CloudStatus>> step)
    {
        var s = _session();
        if (s == null || _busy) return;
        _busy = true;
        Refresh();
        try
        {
            await step(s);
        }
        finally
        {
            _busy = false;
            Refresh();
            _changed();
        }
    }
}
