# Builds the Windows release of Padfin into build\windows: exports the project with Godot, then
# puts libmpv beside the executable.
#
# Godot is taken from the GODOT_BIN environment variable, or from PATH. It must be the 4.7.2 .NET
# build, with its export templates installed.
#
# Usage: .\build.ps1 [-Output <folder>] [-Release]
#   -Release also exports the Linux build and writes releases\padfin-windows.zip and
#   releases\padfin-linux.zip, the files to attach to a GitHub release.
param([string]$Output, [switch]$Release)

$ErrorActionPreference = 'Stop'
# The release of this repository that holds the Linux libmpv. Delete native\linux-x64 after changing it.
$LinuxMpvTag = 'libmpv-linux-1'
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

    # The Linux build. Its libmpv is built from source by this repository's "Linux libmpv" workflow
    # and published as a release, with FFmpeg and the codecs inside the one library.
    $linuxMpv = Join-Path $root 'native\linux-x64\libmpv.so.2'
    if (-not (Test-Path $linuxMpv)) {
        $linuxNative = Split-Path $linuxMpv
        New-Item -ItemType Directory -Force $linuxNative | Out-Null
        $archive = Join-Path $linuxNative 'libmpv-linux-x86_64.tar.gz'
        Write-Host "Fetching libmpv for Linux ($LinuxMpvTag)"
        Invoke-WebRequest -Uri "https://github.com/HowDoDownhill/padfin/releases/download/$LinuxMpvTag/libmpv-linux-x86_64.tar.gz" -OutFile $archive -UseBasicParsing
        tar.exe -xzf $archive -C $linuxNative
        Remove-Item $archive -Force -Confirm:$false
        if (-not (Test-Path $linuxMpv)) { throw 'libmpv for Linux could not be fetched.' }
    }
    $linux = Join-Path $root 'build\linux'
    New-Item -ItemType Directory -Force $linux | Out-Null
    Get-ChildItem $linux -Force | Remove-Item -Recurse -Force -Confirm:$false
    Write-Host 'Exporting the Linux build'
    $linuxLog = Join-Path $root 'build\export-linux.log'
    cmd /c "`"$godot`" --headless --path `"$root`" --export-release `"Linux`" `"$linux\padfin.x86_64`" > `"$linuxLog`" 2>&1"
    if (-not (Test-Path "$linux\padfin.x86_64") -or -not (Get-ChildItem $linux -Directory -Filter 'data_*')) {
        Select-String -Path $linuxLog -Pattern '^ERROR' | Select-Object -First 10 | ForEach-Object { Write-Host $_.Line }
        throw "The Linux export failed. The full log is at $linuxLog"
    }
    Copy-Item (Join-Path (Split-Path $linuxMpv) '*') $linux -Force

    # Written entry by entry so the launcher files keep their executable bit, which a zip made by
    # Windows tools would drop. Names use forward slashes, as Linux expects.
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $linuxZip = Join-Path $releases 'padfin-linux.zip'
    if (Test-Path $linuxZip) { Remove-Item $linuxZip -Force -Confirm:$false }
    $archive = [IO.Compression.ZipFile]::Open($linuxZip, 'Create')
    try {
        foreach ($file in Get-ChildItem $linux -Recurse -File) {
            $name = $file.FullName.Substring($linux.Length + 1).Replace('\', '/')
            $entry = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $name, 'Optimal')
            $executable = $name -eq 'padfin.x86_64' -or $name -eq 'padfin.sh'
            $mode = if ($executable) { 0x81ED } else { 0x81A4 }   # -rwxr-xr-x or -rw-r--r--
            $entry.ExternalAttributes = $mode -shl 16
        }
    }
    finally { $archive.Dispose() }

    # Each entry is marked as written on Unix. Zip tools on Linux ignore the permission bits of an
    # entry marked as written on Windows, and the launcher would unpack without its executable bit.
    $bytes = [IO.File]::ReadAllBytes($linuxZip)
    $end = $bytes.Length - 22
    while ($end -ge 0 -and -not ($bytes[$end] -eq 0x50 -and $bytes[$end + 1] -eq 0x4B -and $bytes[$end + 2] -eq 5 -and $bytes[$end + 3] -eq 6)) { $end-- }
    if ($end -lt 0) { throw 'The Linux zip has no directory.' }
    $count = [BitConverter]::ToUInt16($bytes, $end + 10)
    $at = [int][BitConverter]::ToUInt32($bytes, $end + 16)
    for ($i = 0; $i -lt $count; $i++) {
        if ([BitConverter]::ToUInt32($bytes, $at) -ne 0x02014B50) { throw 'The Linux zip directory could not be read.' }
        $bytes[$at + 5] = 3
        $at += 46 + [BitConverter]::ToUInt16($bytes, $at + 28) + [BitConverter]::ToUInt16($bytes, $at + 30) + [BitConverter]::ToUInt16($bytes, $at + 32)
    }
    [IO.File]::WriteAllBytes($linuxZip, $bytes)
    Write-Host ('Packed {0} ({1:N0} MB)' -f $linuxZip, ((Get-Item $linuxZip).Length / 1MB))
}
