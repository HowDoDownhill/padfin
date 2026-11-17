# Downloads libmpv for Windows x64 into native/win-x64.
# Source: https://github.com/shinchiro/mpv-winbuild-cmake (the builds mpv.io links to).
# Needs 7-Zip. Linux uses the system libmpv (libmpv.so.2).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root 'native\win-x64'
$sevenZip = @('C:\Program Files\7-Zip\7z.exe', 'C:\Program Files (x86)\7-Zip\7z.exe') | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $sevenZip) { $sevenZip = (Get-Command 7z -ErrorAction SilentlyContinue).Source }
if (-not $sevenZip) { throw '7-Zip is required to unpack libmpv. Install it from https://7-zip.org.' }

$release = Invoke-RestMethod -Uri 'https://api.github.com/repos/shinchiro/mpv-winbuild-cmake/releases/latest' -Headers @{ 'User-Agent' = 'fetch-mpv' }
$asset = $release.assets | Where-Object { $_.name -match '^mpv-dev-x86_64-\d' } | Select-Object -First 1
if (-not $asset) { throw 'No mpv-dev-x86_64 asset in the latest release.' }

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("padfin-mpv-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $tmp | Out-Null
try {
    $archive = Join-Path $tmp $asset.name
    Write-Host "Downloading $($asset.name)"
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $archive -UseBasicParsing
    & $sevenZip e $archive "-o$tmp" 'libmpv-2.dll' -r -y | Out-Null
    $dll = Join-Path $tmp 'libmpv-2.dll'
    if (-not (Test-Path $dll)) { throw 'libmpv-2.dll was not in the archive.' }
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item $dll (Join-Path $dest 'libmpv-2.dll') -Force
    Write-Host "Installed $(Join-Path $dest 'libmpv-2.dll') ($($release.tag_name))"
}
finally {
    Remove-Item -Recurse -Force $tmp -Confirm:$false
}
