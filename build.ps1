# Builds the Windows release of Padfin into build\windows: exports the project with Godot, then
# puts libmpv beside the executable.
#
# Godot is taken from the GODOT_BIN environment variable, or from PATH. It must be the 4.7.2 .NET
# build, with its export templates installed.
#
# Usage: .\build.ps1 [-Output <folder>] [-Release]
#   -Release also writes releases\padfin-windows.zip, the file to attach to a GitHub release.
param([string]$Output, [switch]$Release)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = if ($Output) { [IO.Path]::GetFullPath((Join-Path $root $Output)) } else { Join-Path $root 'build\windows' }
$exe = Join-Path $out 'padfin.exe'
$mpv = Join-Path $root 'native\win-x64\libmpv-2.dll'

$godot = $env:GODOT_BIN
if (-not $godot) {
    foreach ($name in 'godot', 'godot-mono', 'godot4', 'Godot_v4.7.2-stable_mono_win64_console') {
        $found = Get-Command $name -ErrorAction SilentlyContinue
        if ($found) { $godot = $found.Source; break }
    }
}
if (-not $godot -or -not (Test-Path $godot)) {
    throw 'Godot was not found. Set GODOT_BIN to the Godot 4.7.2 .NET console executable.'
}

if (-not (Test-Path $mpv)) {
    Write-Host 'libmpv is missing. Fetching it.'
    & (Join-Path $root 'tools\fetch-mpv.ps1')
}

# A copy of Padfin running from the output folder locks its files. Stop before deleting any of them.
$running = Get-Process | Where-Object { $_.Path -and $_.Path.StartsWith($out + '\', [StringComparison]::OrdinalIgnoreCase) }
if ($running) {
    throw "Padfin is running from $out. Close it, then run the build again."
}

# Emptied, not removed, so the build works while the folder is open in a terminal or Explorer.
New-Item -ItemType Directory -Force $out | Out-Null
Get-ChildItem $out -Force | Remove-Item -Recurse -Force -Confirm:$false

Write-Host "Exporting with $godot"
$log = Join-Path $root 'build\export.log'
# Through cmd so Godot's stderr lands in the log as plain text.
cmd /c "`"$godot`" --headless --path `"$root`" --export-release `"Windows Desktop`" `"$exe`" > `"$log`" 2>&1"

# Godot can exit cleanly after a failed C# build, so the output is checked too.
$data = Get-ChildItem $out -Directory -Filter 'data_*' -ErrorAction SilentlyContinue
if (-not (Test-Path $exe) -or -not $data) {
    Select-String -Path $log -Pattern '^ERROR' | Select-Object -First 10 | ForEach-Object { Write-Host $_.Line }
    throw "The export failed. The full log is at $log"
}

Copy-Item $mpv $out -Force

$size = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ('Built {0} ({1:N0} MB)' -f $out, $size)

if ($Release) {
    # Only the shipped files are named. The build folder can pick up other files when the app is
    # run in place, and none of those belong in a download.
    $releases = Join-Path $root 'releases'
    $zip = Join-Path $releases 'padfin-windows.zip'
    New-Item -ItemType Directory -Force $releases | Out-Null
    if (Test-Path $zip) { Remove-Item $zip -Force -Confirm:$false }
    $shipped = @('padfin.exe', 'padfin.console.exe', 'padfin.pck', 'libmpv-2.dll') + @(Get-ChildItem $out -Directory -Filter 'data_*' | ForEach-Object { $_.Name })
    Push-Location $out
    try { tar.exe -a -c -f $zip @shipped } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $zip)) { throw 'The release zip could not be made.' }
    Write-Host ('Packed {0} ({1:N0} MB)' -f $zip, ((Get-Item $zip).Length / 1MB))
}
