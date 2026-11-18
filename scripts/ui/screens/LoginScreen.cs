using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Padfin;

/// <summary>Signs in with Quick Connect, which needs no typing, or with a username and password.</summary>
public partial class LoginScreen : Screen
{
    private string _username = "";
    private string _password = "";
    private Button _userField;
    private Button _passwordField;
    private Button _signIn;
    private Label _code;
    private Label _codeHelp;
    private bool _busy;
    private bool _closed;

    public override IEnumerable<(string, string)> Hints => new[] { (Actions.Accept, "Select"), (Actions.Back, "Servers") };

    public override bool ShowTopBar => false;

    public override void OnEnter()
    {
        _username = Config.I.UserName;

        var center = new CenterContainer();
        Ui.Fill(center);
        AddChild(center);
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 40);
        center.AddChild(columns);

        var form = new VBoxContainer { CustomMinimumSize = new Vector2(620, 0) };
        form.AddThemeConstantOverride("separation", 14);
        columns.AddChild(Ui.Panel(form, 36, 30));
        form.AddChild(Ui.Label("Sign In", 60, variation: "DisplayLabel"));
        form.AddChild(Ui.Label(Config.I.ServerName, 26, variation: "CaptionLabel"));
        form.AddChild(Ui.Spacer(0, 12));

        _userField = Ui.Button("", () => _ = EditAsync(false));
        _userField.Alignment = HorizontalAlignment.Left;
        form.AddChild(_userField);
        _passwordField = Ui.Button("", () => _ = EditAsync(true));
        _passwordField.Alignment = HorizontalAlignment.Left;
        form.AddChild(_passwordField);
        _signIn = Ui.ActionButton("Sign In", () => _ = SignInAsync());
        form.AddChild(_signIn);
        RefreshFields();

        var quickColumn = new VBoxContainer { CustomMinimumSize = new Vector2(460, 0) };
        quickColumn.AddThemeConstantOverride("separation", 12);
        columns.AddChild(Ui.Panel(quickColumn, 36, 30));
        quickColumn.AddChild(Ui.Label("Quick Connect", variation: "TitleLabel"));
        _code = Ui.Label("", 96, variation: "AccentLabel");
        _code.HorizontalAlignment = HorizontalAlignment.Center;
        quickColumn.AddChild(_code);
        _codeHelp = Ui.Label("Checking…", 22, variation: "CaptionLabel");
        _codeHelp.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _codeHelp.CustomMinimumSize = new Vector2(440, 0);
        quickColumn.AddChild(_codeHelp);

        _ = QuickConnectAsync();
    }

    public override void _ExitTree() => _closed = true;

    public override void FocusDefault() => (_username == "" ? _userField : _passwordField).GrabFocus();

    private void RefreshFields()
    {
        _userField.Text = _username == "" ? "Username" : $"Username    {_username}";
        _passwordField.Text = _password == "" ? "Password" : $"Password    {new string('•', _password.Length)}";
    }

    private async Task EditAsync(bool password)
    {
        var value = await App.I.PromptAsync(password ? "Password" : "Username", password ? _password : _username, password);
        if (value == null) { return; }
        if (password) { _password = value; } else { _username = value.Trim(); }
        RefreshFields();
        (password ? _signIn : _passwordField).GrabFocus();
    }

    private async Task SignInAsync()
    {
        if (_busy) { return; }
        if (_username == "")
        {
            App.I.Toast("Enter your username first.");
            return;
        }
        _busy = true;
        try
        {
            await Jellyfin.I.LoginAsync(_username, _password);
            _closed = true;
            App.I.ResetTo(new HomeScreen());
        }
        catch (JellyfinException e) { App.I.Toast(e.Message); }
        finally { _busy = false; }
    }

    private async Task QuickConnectAsync()
    {
        try
        {
            if (!await Jellyfin.I.QuickConnectEnabledAsync())
            {
                _codeHelp.Text = "Quick Connect is turned off on this server.";
                return;
            }
            var request = await Jellyfin.I.QuickConnectStartAsync();
            if (_closed || request?.Code == null) { return; }
            _code.Text = request.Code;
            _codeHelp.Text = "On a device that is already signed in, open Jellyfin, choose your profile, then Quick Connect, and enter this code.";
            while (!_closed)
            {
                await Task.Delay(2500);
                if (_closed) { return; }
                var state = await Jellyfin.I.QuickConnectPollAsync(request.Secret);
                if (state is not { Authenticated: true }) { continue; }
                await Jellyfin.I.QuickConnectFinishAsync(request.Secret);
                if (_closed) { return; }
                _closed = true;
                App.I.ResetTo(new HomeScreen());
                return;
            }
        }
        catch (JellyfinException)
        {
            if (!_closed && IsInstanceValid(this)) { _codeHelp.Text = "Quick Connect is unavailable."; }
        }
    }
}
