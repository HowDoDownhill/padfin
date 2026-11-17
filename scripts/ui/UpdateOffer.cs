using System.Threading.Tasks;

namespace Padfin;

/// <summary>Checks for an update and, when there is one, asks, downloads with a progress bar, and restarts into it.</summary>
public static class UpdateFlow
{
    private const int NotesLimit = 700;

    /// <param name="manual">True when the user asked for the check, so "nothing new" is said out loud.</param>
    /// <returns>True when an update is being installed and the app is about to restart.</returns>
    public static async Task<bool> CheckAndOfferAsync(bool manual)
    {
        if (!Updater.IsSupported)
        {
            if (manual) { App.I.Toast("Updates work in the Windows and Linux builds."); }
            return false;
        }

        var offer = await Updater.I.CheckAsync();
        if (offer == null)
        {
            if (manual) { App.I.Toast($"Padfin {Updater.CurrentVersion.TrimStart('v')} is up to date."); }
            return false;
        }

        var notes = (offer.Notes ?? "").Trim();
        if (notes.Length > NotesLimit) { notes = notes[..NotesLimit].TrimEnd() + "…"; }
        var title = offer.GoesBackToFullRelease ? $"Go back to version {offer.Version.TrimStart('v')}?" : $"Update to version {offer.Version.TrimStart('v')}?";
        var body = offer.GoesBackToFullRelease
            ? "Test builds are turned off, and this copy is a test build. The latest full release is an older version.\n\n" + notes
            : notes;
        var choice = await App.I.ChooseAsync(title, new[] { offer.GoesBackToFullRelease ? "Install Full Release" : "Install Update", "Not Now" }, body);
        if (choice != 0)
        {
            // The offer to go back is made once per test build. Switching test builds on and off brings it back.
            if (offer.GoesBackToFullRelease) { Config.I.DeclinedGoBackFrom = Updater.CurrentVersion; }
            return false;
        }

        var progress = new ProgressOverlay("Downloading the update…");
        App.I.OpenOverlay(progress);
        var downloaded = await Updater.I.DownloadAsync(offer.Version, progress.SetProgress);
        if (!downloaded)
        {
            App.I.CloseOverlay(progress);
            App.I.Toast("The update could not be downloaded. Try again later.");
            return false;
        }
        progress.SetStatus("Restarting to finish the update…");
        await Task.Delay(1500);
        Updater.I.ApplyAndRestart();
        return true;
    }
}
