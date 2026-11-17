using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HttpClient = System.Net.Http.HttpClient;

namespace Padfin;

/// <summary>An update found on GitHub.</summary>
public record UpdateOffer(string Version, string Notes, bool GoesBackToFullRelease);

/// <summary>
/// Finds new releases on GitHub, downloads the build for this platform and swaps it in, following
/// RomBox's updater. Full releases are offered to everyone. Test builds, which GitHub calls
/// pre-releases, are offered when the setting for them is on.
/// </summary>
public partial class Updater : Node
{
    public const string CurrentVersion = "v" + Jellyfin.ClientVersion;
    private const string RepoOwner = "HowDoDownhill";
    private const string RepoName = "padfin";
    private const int BufferBytes = 1024 * 1024;

    /// <summary>Points the updater at another server in place of GitHub's API, for testing.</summary>
    private const string ApiOverrideVariable = "PADFIN_UPDATE_API";

    public static Updater I { get; private set; }

    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static string Api =>
        (System.Environment.GetEnvironmentVariable(ApiOverrideVariable) ?? "https://api.github.com").TrimEnd('/') + $"/repos/{RepoOwner}/{RepoName}";

    private static string WorkDirectory => Path.Combine(OS.GetUserDataDir(), "update");
    private static string ZipPath => Path.Combine(WorkDirectory, "update.zip");
    private static string StagingPath => Path.Combine(WorkDirectory, "staging");

    /// <summary>Windows and Linux builds update themselves. A run from the editor has nothing to replace.</summary>
    public static bool IsSupported => !OS.HasFeature("editor") && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux());

    private static string ZipName => OperatingSystem.IsWindows() ? "padfin-windows.zip" : "padfin-linux.zip";

    public override void _EnterTree()
    {
        I = this;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PadfinUpdater", "1.0"));
    }

    // ---- Finding a release ----

    private sealed class Release
    {
        public string Tag;
        public string Notes;
        public bool Prerelease;
        public bool Draft;
        public List<(string Name, string Url)> Assets = new();
    }

    /// <summary>The update to offer, or null when the installed build is current or GitHub cannot be reached.</summary>
    public async Task<UpdateOffer> CheckAsync()
    {
        try
        {
            var testBuilds = Config.I.PreReleaseBuilds;
            var release = testBuilds ? await NewestIncludingTestBuildsAsync() : await LatestFullReleaseAsync();
            GD.Print($"[Update] installed {CurrentVersion}; newest {(testBuilds ? "release or test build" : "full release")} is {release?.Tag ?? "unknown"}.");
            if (release == null || string.IsNullOrEmpty(release.Tag)) { return null; }

            if (IsNewer(release.Tag, CurrentVersion)) { return new UpdateOffer(release.Tag, release.Notes ?? "", false); }

            // Someone who turned test builds off while on one is offered the latest full release,
            // which is an older version than the one installed.
            if (!testBuilds && Config.I.DeclinedGoBackFrom != CurrentVersion && IsNewer(CurrentVersion, release.Tag) && await IsInstalledATestBuildAsync())
            {
                GD.Print($"[Update] test builds are off and {CurrentVersion} is a test build; offering {release.Tag}.");
                return new UpdateOffer(release.Tag, release.Notes ?? "", true);
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            GD.Print("[Update] could not check for updates: ", e.Message);
        }
        return null;
    }

    private async Task<JsonDocument> GetJsonAsync(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var response = await _http.GetAsync(Api + path, timeout.Token);
        if (!response.IsSuccessStatusCode) { return null; }
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
    }

    private static Release Read(JsonElement json)
    {
        var release = new Release
        {
            Tag = json.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null,
            Notes = json.TryGetProperty("body", out var body) ? body.GetString() : null,
            Prerelease = json.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True,
            Draft = json.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True,
        };
        if (json.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                release.Assets.Add((asset.GetProperty("name").GetString(), asset.GetProperty("browser_download_url").GetString()));
            }
        }
        return release;
    }

    /// <summary>GitHub's "latest" is the newest full release. Pre-releases are never it.</summary>
    private async Task<Release> LatestFullReleaseAsync()
    {
        using var json = await GetJsonAsync("/releases/latest");
        return json == null ? null : Read(json.RootElement);
    }

    /// <summary>
    /// The highest version among recent releases, full or test. A full release with a higher
    /// number still wins, so a tester moves on to it when it ships.
    /// </summary>
    private async Task<Release> NewestIncludingTestBuildsAsync()
    {
        using var json = await GetJsonAsync("/releases?per_page=20");
        if (json == null || json.RootElement.ValueKind != JsonValueKind.Array) { return null; }
        return json.RootElement.EnumerateArray().Select(Read)
            .Where(r => !r.Draft && Parse(r.Tag) != null)
            .OrderByDescending(r => Parse(r.Tag))
            .FirstOrDefault();
    }

    /// <summary>True when the installed version was published as a pre-release. A build from source with an unpublished version is left alone.</summary>
    private async Task<bool> IsInstalledATestBuildAsync()
    {
        using var json = await GetJsonAsync("/releases/tags/" + CurrentVersion.TrimStart('v', 'V'));
        return json != null && Read(json.RootElement).Prerelease;
    }

    private static Version Parse(string tag) =>
        Version.TryParse((tag ?? "").TrimStart('v', 'V'), out var version) ? version : null;

    private static bool IsNewer(string candidate, string installed)
    {
        var (a, b) = (Parse(candidate), Parse(installed));
        return a != null && b != null ? a > b : candidate.TrimStart('v', 'V') != installed.TrimStart('v', 'V');
    }

    // ---- Downloading and applying ----

    /// <summary>Downloads a release's build for this platform and unpacks it beside the app's data, ready to swap in.</summary>
    public async Task<bool> DownloadAsync(string version, Action<float> progress)
    {
        try
        {
            using var json = await GetJsonAsync("/releases/tags/" + version);
            var release = json == null ? null : Read(json.RootElement);
            var asset = release?.Assets.FirstOrDefault(a => a.Name.Equals(ZipName, StringComparison.OrdinalIgnoreCase));
            if (asset?.Url == null)
            {
                GD.PrintErr($"[Update] release {version} has no {ZipName}.");
                return false;
            }

            Directory.CreateDirectory(WorkDirectory);
            using (var response = await _http.GetAsync(asset.Value.Url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0;
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var file = new FileStream(ZipPath, FileMode.Create, System.IO.FileAccess.Write, FileShare.None, BufferBytes, true);
                var buffer = new byte[BufferBytes];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read));
                    done += read;
                    if (total > 0) { progress?.Invoke((float)done / total); }
                }
            }

            // Unpacked here, in the app, so applying it needs no unzip tool on the machine.
            await Task.Run(() =>
            {
                if (Directory.Exists(StagingPath)) { Directory.Delete(StagingPath, true); }
                ZipFile.ExtractToDirectory(ZipPath, StagingPath, true);
            });
            File.Delete(ZipPath);
            var executable = Path.Combine(StagingPath, Path.GetFileName(OS.GetExecutablePath()));
            if (!File.Exists(executable))
            {
                GD.PrintErr("[Update] the downloaded build does not contain the app's executable.");
                return false;
            }
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            GD.PrintErr("[Update] download failed: ", e.Message);
            return false;
        }
    }

    /// <summary>
    /// Starts a small script that waits for this copy to exit, copies the new build over the app
    /// folder and starts it again, then quits. A running executable cannot overwrite itself.
    /// The script leaves update.log beside the downloaded build, for when a copy goes wrong.
    /// </summary>
    public void ApplyAndRestart()
    {
        var appPath = OS.GetExecutablePath();
        var appDirectory = Path.GetDirectoryName(appPath);

        // A copy that Steam started is started through Steam again, so it keeps Steam Input and the overlay.
        string steamUrl = null;
        if (SteamShortcuts.IsLaunchedThroughSteam && SteamShortcuts.GetState() == SteamShortcutState.Current)
        {
            steamUrl = $"steam://rungameid/{((ulong)SteamShortcuts.FindShortAppId() << 32) | 0x02000000}";
        }

        var processId = OS.GetProcessId();
        var log = Path.Combine(WorkDirectory, "update.log");

        if (OperatingSystem.IsWindows())
        {
            // The script waits for this process to exit before copying. "timeout" is no use for the
            // wait: it refuses to run without a console, and the script runs hidden. Robocopy
            // retries files that are still locked, and reports success with an exit code below 8.
            var script = Path.Combine(WorkDirectory, "update.bat");
            File.WriteAllText(script, $@"@echo off
set tries=0
:wait
tasklist /FI ""PID eq {processId}"" 2>nul | find ""{processId}"" >nul
if errorlevel 1 goto copy
set /a tries+=1
if %tries% geq 60 goto copy
ping -n 2 127.0.0.1 >nul
goto wait
:copy
robocopy ""{StagingPath}"" ""{appDirectory}"" /E /R:10 /W:1 /NFL /NDL /NJH /NJS > ""{log}"" 2>&1
if errorlevel 8 (echo The update could not be copied in. >> ""{log}"") else (rmdir /S /Q ""{StagingPath}"")
start """" ""{steamUrl ?? appPath}""
del ""%~f0""
");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = script,
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            });
        }
        else
        {
            var script = Path.Combine(WorkDirectory, "update.sh");
            var lines = new[]
            {
                "#!/bin/bash",
                $"for i in $(seq 1 120); do kill -0 {processId} 2>/dev/null || break; sleep 0.5; done",
                $"if cp -rf '{StagingPath}/.' '{appDirectory}/' > '{log}' 2>&1; then rm -rf '{StagingPath}'; fi",
                steamUrl != null ? $"xdg-open '{steamUrl}'" : $"chmod +x '{appPath}'; '{appPath}' &",
                "rm -f \"$0\"",
            };
            File.WriteAllText(script, string.Join('\n', lines) + "\n");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"chmod +x '{script}' && '{script}' &\"",
                UseShellExecute = false,
            });
        }
        GD.Print("[Update] quitting so the new build can be copied in.");
        _ = App.I.QuitAsync();
    }
}
