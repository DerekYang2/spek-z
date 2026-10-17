# Downloads ffmpeg.exe from the ffmpeg-audio release into ffmpeg\win-<arch>\ in the repository. Spek-Z's build
# copies it next to Spek-Z.exe when it's there, and CI points the tests at it.
# ffmpeg-audio is an audio-only LGPL build of FFmpeg for Spek-Z and FluentDL: https://github.com/DerekYang2/ffmpeg-audio
param(
    [ValidateSet('x64', 'arm64')]
    [string[]] $Architecture = @('x64'),
    [string] $Destination = (Join-Path $PSScriptRoot '..\ffmpeg')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.IO.Compression.FileSystem

# To update, change the release and the hashes of its two zips.
$release = 'v9.0.2-1'
$version = '9.0.2'
$hashes = @{
    x64   = 'e863e7d620b1119f00d49ee577f5bec50f5f54469d653da059fe8e61ed44e6df'
    arm64 = 'a4a750271e5412f440c8ec93fa203009b35669c734f5b624477d7cfef612da54'
}

foreach ($arch in $Architecture) {
    $name = "ffmpeg-$version-audio-win-$arch.zip"
    $url = "https://github.com/DerekYang2/ffmpeg-audio/releases/download/$release/$name"
    $folder = Join-Path $Destination "win-$arch"
    $zip = Join-Path ([IO.Path]::GetTempPath()) $name

    Write-Host "Downloading $url"
    Invoke-WebRequest $url -OutFile $zip
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
    if ($hash -ne $hashes[$arch]) {
        Remove-Item $zip
        throw "$name has SHA-256 $hash, expected $($hashes[$arch])."
    }

    if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
    New-Item -ItemType Directory $folder | Out-Null
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($entry in $archive.Entries) {
            if (-not $entry.Name) { continue }
            # Drop the zip's top folder: ffmpeg.exe, BUILD.txt and licenses\ go straight into $folder.
            $relative = $entry.FullName.Substring($entry.FullName.IndexOf('/') + 1)
            $target = Join-Path $folder $relative
            New-Item -ItemType Directory (Split-Path $target) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    }
    finally {
        $archive.Dispose()
        Remove-Item $zip
    }

    $native = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
    if ($arch -eq $native -or ($arch -eq 'x64' -and $native -eq 'arm64')) {
        $line = (& (Join-Path $folder 'ffmpeg.exe') -hide_banner -version | Select-Object -First 1)
        Write-Host "$folder\ffmpeg.exe: $line"
    }
    else {
        # An x64 machine can't run ARM64 programs, so this one can't be checked here.
        Write-Host "$folder\ffmpeg.exe: downloaded"
    }
}
