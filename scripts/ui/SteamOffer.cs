using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Padfin;

/// <summary>
/// Offers to add Padfin to Steam as a non-Steam game and start it from there, following RomBox's
/// flow: close Steam if a shortcut has to be written, write it with artwork and Steam Input turned
/// on, then start the shortcut through Steam and quit this copy.
/// </summary>
public static class SteamOffer
{
    private const double StatusVisibleSeconds = 2.0;

    private const string Benefits =
        "Open Padfin from your Steam library, in Big Picture and in Game Mode on a Steam Deck. Steam Input makes any controller work, and the Steam overlay is available.";

    /// <summary>
    /// Shows the offer when it can help: a desktop build started outside Steam, with Steam
    /// installed, and the user has not said to stop asking.
    /// </summary>
    public static async Task OfferIfUsefulAsync()
    {
        if (!SteamShortcuts.IsSupportedPlatform || SteamShortcuts.IsLaunchedThroughSteam || !Config.I.OfferSteamLaunch) { return; }
        var state = SteamShortcuts.GetState();
        if (state == SteamShortcutState.SteamNotFound) { return; }

        var (title, accept, how) = state switch
        {
            SteamShortcutState.Current => ("Open Padfin from Steam?", "Restart Through Steam", "Padfin is already in your Steam library. It will close and reopen from Steam."),
            SteamShortcutState.Outdated => ("Fix Padfin's Steam shortcut?", "Fix and Restart Through Steam", "Your Steam shortcut is out of date. If Steam is open, it will restart. Padfin will then reopen from Steam."),
            _ => ("Add Padfin to Steam?", "Add to Steam and Restart", "If Steam is open, it will restart. Padfin will then reopen from Steam."),
        };
        var choice = await App.I.ChooseAsync(title, new[] { accept, "Not Now", "Don't Ask Again" }, $"{Benefits}\n\n{how}");
        if (choice == 0) { await StartThroughSteamAsync(); }
        else if (choice == 2)
        {
            Config.I.OfferSteamLaunch = false;
            GD.Print("[Steam] the user chose not to be asked about starting through Steam again.");
        }
    }

    /// <summary>Adds or repairs the shortcut as needed, then restarts this app through Steam.</summary>
    public static async Task StartThroughSteamAsync()
    {
        if (!SteamShortcuts.IsSupportedPlatform) { return; }
        var state = SteamShortcuts.GetState();
        if (state == SteamShortcutState.SteamNotFound)
        {
            await TellAsync("Steam wasn't found", "Install Steam and sign in, then choose Steam library in Settings > System.");
            return;
        }

        if (state == SteamShortcutState.Current)
        {
            // Artwork and Steam Input can only be written while Steam is closed.
            if (!SteamShortcuts.IsSteamRunning()) { SteamShortcuts.AddOrUpdateThisApp(true); }
            await RelaunchAsync();
            return;
        }

        if (SteamShortcuts.IsSteamRunning())
        {
            var restart = await App.I.ChooseAsync("Restart Steam?", new[] { "Restart Steam", "Not Now" },
                "Steam only reads new library entries when it starts. Padfin will close Steam, add itself, and reopen from Steam.");
            if (restart != 0) { return; }
            App.I.Toast("Closing Steam…");
            await SteamShortcuts.CloseSteam();
            if (SteamShortcuts.IsSteamRunning())
            {
                await TellAsync("Steam didn't close", "Close Steam yourself, then choose Steam library in Settings > System.");
                return;
            }
        }

        App.I.Toast("Adding Padfin to Steam…");
        var result = SteamShortcuts.AddOrUpdateThisApp(true);
        if (result is SteamShortcutResult.SteamNotFound or SteamShortcutResult.Failed)
        {
            await TellAsync(result == SteamShortcutResult.SteamNotFound ? "Steam wasn't found" : "Padfin couldn't add itself to Steam",
                result == SteamShortcutResult.SteamNotFound
                    ? "Install Steam and sign in, then choose Steam library in Settings > System."
                    : "Add it yourself: in Steam, choose Games > Add a Non-Steam Game to My Library and pick this app. Then open its Properties > Controller and choose Enable Steam Input.");
            SteamShortcuts.OpenSteamLibrary();
            return;
        }
        await RelaunchAsync();
    }

    private static async Task RelaunchAsync()
    {
        App.I.Toast("Starting Padfin through Steam…");
        SteamShortcuts.LaunchThroughSteam();
        await Task.Delay((int)(StatusVisibleSeconds * 1000));
        GD.Print("[Steam] quitting so Steam's copy takes over.");
        await App.I.QuitAsync();
    }

    private static Task TellAsync(string title, string body) => App.I.ChooseAsync(title, new List<string> { "OK" }, body);

    public static string DescribeStatus()
    {
        if (SteamShortcuts.IsLaunchedThroughSteam) { return "Running Through Steam"; }
        return SteamShortcuts.GetState() switch
        {
            SteamShortcutState.Current => "In Your Steam Library",
            SteamShortcutState.Outdated => "Steam Shortcut Out of Date",
            SteamShortcutState.SteamNotFound => "Steam Not Found",
            _ => "Not in Your Steam Library",
        };
    }

    public static string DescribeAction() => SteamShortcuts.GetState() switch
    {
        SteamShortcutState.Current => "Restart through Steam",
        SteamShortcutState.Outdated => "Fix the Steam shortcut",
        _ => "Add to Steam",
    };
}
