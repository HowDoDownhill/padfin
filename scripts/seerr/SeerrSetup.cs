using System.Threading.Tasks;
using Godot;

namespace Padfin;

/// <summary>The steps for connecting Padfin to a Seerr server, opened from the Requests page.</summary>
public static class SeerrSetup
{
    /// <summary>Asks for the address and a sign-in, then connects. True when Seerr is connected at the end.</summary>
    public static async Task<bool> RunAsync()
    {
        var address = await App.I.PromptAsync("Seerr address, such as 192.168.1.50:5055", Config.I.SeerrUrl);
        if (string.IsNullOrWhiteSpace(address)) { return false; }

        var choice = await App.I.ChooseAsync("Sign in to Seerr with", new[] { "Jellyfin account", "Seerr account", "API key" });
        if (choice < 0) { return false; }

        string auth;
        var username = "";
        string secret;
        if (choice == 0)
        {
            auth = Seerr.AuthJellyfin;
            username = Config.I.UserName;
            secret = await App.I.PromptAsync($"Jellyfin password for {username}", "", true);
        }
        else if (choice == 1)
        {
            auth = Seerr.AuthLocal;
            username = await App.I.PromptAsync("Seerr email address");
            if (string.IsNullOrWhiteSpace(username)) { return false; }
            secret = await App.I.PromptAsync("Seerr password", "", true);
        }
        else
        {
            auth = Seerr.AuthApiKey;
            secret = await App.I.PromptAsync("Seerr API key");
        }
        if (secret == null) { return false; }

        try
        {
            await Seerr.I.ConnectAsync(address, auth, username.Trim(), secret.Trim());
            App.I.Toast($"Connected to Seerr as {Seerr.I.User.Name}.");
            return true;
        }
        catch (SeerrException e)
        {
            App.I.Toast(e.Message);
            return false;
        }
    }

    /// <summary>A panel inviting the user to connect, for pages that need Seerr, and its button to focus.</summary>
    public static (Control Panel, Button Button) Invitation(System.Action connected)
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(620, 0) };
        column.AddThemeConstantOverride("separation", 14);
        column.AddChild(Ui.Label("Seerr", variation: "DisplayLabel"));
        var text = Ui.Label("Connect to your Seerr server to find movies and shows in Search and request them for your library.", variation: "CaptionLabel", size: 24);
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(text);
        var button = Ui.ActionButton("Connect to Seerr");
        button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        button.Pressed += async () =>
        {
            if (await RunAsync()) { connected(); }
        };
        column.AddChild(button);

        var panel = Ui.Panel(column, 36, 30);
        panel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        return (Ui.Pad(panel, Ui.Margin, 30, Ui.Margin, 0), button);
    }
}
