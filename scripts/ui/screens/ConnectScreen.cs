using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Padfin;

/// <summary>Picks a server: found on the network, the last one used, or typed in.</summary>
public partial class ConnectScreen : Screen
{
    private VBoxContainer _list;
    private Label _status;
    private Button _manual;
    private bool _busy;

    public override IEnumerable<(string, string)> Hints => new[] { (Actions.Accept, "Select"), (Actions.X, "Search Again"), (Actions.Start, "Exit") };

    public override bool ShowTopBar => false;

    public override void OnEnter()
    {
        var center = new CenterContainer();
        Ui.Fill(center);
        AddChild(center);

        var column = new VBoxContainer { CustomMinimumSize = new Vector2(760, 0) };
        column.AddThemeConstantOverride("separation", 10);
        center.AddChild(Ui.MicaPanel(column, 36, 30));

        column.AddChild(Ui.Label("Padfin", 84, variation: "DisplayLabel"));
        column.AddChild(Ui.Label("Choose your Jellyfin server", 30, variation: "CaptionLabel"));
        column.AddChild(Ui.Spacer(0, 16));

        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", 10);
        column.AddChild(_list);

        _status = Ui.Label("", 22, variation: "CaptionLabel");
        column.AddChild(_status);

        _manual = Ui.Button("Enter Server Address", () => _ = EnterAddressAsync());
        _manual.Alignment = HorizontalAlignment.Left;
        column.AddChild(_manual);

        _ = DiscoverAsync();
    }

    public override void FocusDefault()
    {
        if (_list.GetChildCount() > 0) { ((Control)_list.GetChild(0)).GrabFocus(); }
        else { _manual.GrabFocus(); }
    }

    private async Task DiscoverAsync()
    {
        if (_busy) { return; }
        _busy = true;
        _status.Text = "Looking for servers on your network…";
        var servers = await Jellyfin.DiscoverAsync();
        _busy = false;
        if (!IsInstanceValid(this)) { return; }

        var hadFocusInList = GetViewport().GuiGetFocusOwner() is { } owner && _list.IsAncestorOf(owner);
        foreach (var child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }

        var seen = new HashSet<string>();
        if (Config.I.Server != "")
        {
            seen.Add(Config.I.Server);
            AddServer(Config.I.ServerName == "" ? Config.I.Server : Config.I.ServerName, Config.I.Server);
        }
        foreach (var server in servers)
        {
            var address = Jellyfin.NormalizeAddress(server.Address);
            if (seen.Add(address)) { AddServer(server.Name ?? address, address); }
        }
        _status.Text = servers.Count == 0 ? "No servers were found on your network." : "";
        if (hadFocusInList || GetViewport().GuiGetFocusOwner() == null || GetViewport().GuiGetFocusOwner() == _manual) { FocusDefault(); }
    }

    private void AddServer(string name, string address)
    {
        var button = Ui.Button($"{name}    {address}", () => _ = ConnectAsync(address));
        button.Alignment = HorizontalAlignment.Left;
        button.ClipText = true;
        _list.AddChild(button);
    }

    private async Task EnterAddressAsync()
    {
        var address = await App.I.PromptAsync("Server address, such as 192.168.1.50:8096", Config.I.Server);
        if (!string.IsNullOrWhiteSpace(address)) { await ConnectAsync(address); }
    }

    private async Task ConnectAsync(string address)
    {
        if (_busy) { return; }
        _busy = true;
        _status.Text = "Connecting…";
        try
        {
            await Jellyfin.I.ConnectAsync(address);
            _status.Text = "";
            App.I.Push(new LoginScreen());
        }
        catch (JellyfinException e)
        {
            _status.Text = "";
            App.I.Toast(e.Message);
        }
        finally { _busy = false; }
    }

    public override bool OnInput(InputEvent e)
    {
        if (e.IsActionPressed(Actions.X))
        {
            _ = DiscoverAsync();
            return true;
        }
        if (e.IsActionPressed(Actions.Start))
        {
            GetTree().Quit();
            return true;
        }
        return false;
    }

    public override bool OnBack() => true;
}
