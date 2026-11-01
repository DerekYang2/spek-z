param(
    [Parameter(Mandatory)]
    [string] $Package,
    [Parameter(Mandatory)]
    [ValidateSet('x64', 'arm64')]
    [string] $Architecture,
    [ValidateSet('Store', 'Sideload')]
    [string] $Channel = 'Store'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path $PSScriptRoot -Parent
[xml] $store = Get-Content (Join-Path $root 'src\SpekZ\Package.appxmanifest') -Raw
$source = $store
if ($Channel -eq 'Sideload') {
    [xml] $source = Get-Content (Join-Path $root 'src\SpekZ\Package.Sideload.appxmanifest') -Raw
    if ($source.Package.Identity.Name -cne $store.Package.Identity.Name -or $source.Package.Identity.Version -cne $store.Package.Identity.Version) {
        throw 'The Store and sideload manifests must have matching names and versions.'
    }
}
$expected = $source.Package.Identity
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $Package).Path)
try {
    function RequireEntry([string] $Name) {
        $entry = $archive.GetEntry($Name)
        if ($null -eq $entry) {
            $entry = $archive.Entries | Where-Object { $_.FullName -ieq $Name } | Select-Object -First 1
        }
        if ($null -eq $entry) { throw "The package is missing $Name." }
        return $entry
    }

    function ReadXml([string] $Name) {
        $reader = [IO.StreamReader]::new((RequireEntry $Name).Open())
        try { return [xml] $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }

    function CheckMachine([string] $Name) {
        $stream = (RequireEntry $Name).Open()
        $memory = [IO.MemoryStream]::new()
        try {
            $stream.CopyTo($memory)
            $data = $memory.ToArray()
            if ($data.Length -lt 64 -or $data[0] -ne 0x4d -or $data[1] -ne 0x5a) {
                throw "$Name is not a PE file."
            }
            $offset = [BitConverter]::ToInt32($data, 0x3c)
            if ($offset -lt 0 -or $offset + 6 -gt $data.Length -or [BitConverter]::ToUInt32($data, $offset) -ne 0x4550) {
                throw "$Name has an invalid PE header."
            }
            $actual = [BitConverter]::ToUInt16($data, $offset + 4)
            $machine = if ($Architecture -eq 'x64') { 0x8664 } else { 0xaa64 }
            if ($actual -ne $machine) { throw "$Name has PE machine 0x$($actual.ToString('x4')), not $Architecture." }
        }
        finally { $stream.Dispose(); $memory.Dispose() }
    }

    $manifest = ReadXml 'AppxManifest.xml'
    $identity = $manifest.Package.Identity
    foreach ($field in 'Name', 'Publisher', 'Version') {
        if ($identity.$field -cne $expected.$field) { throw "Package identity $field does not match Package.appxmanifest." }
    }
    if ($identity.ProcessorArchitecture -ne $Architecture) { throw 'Package architecture does not match the requested build.' }
    $version = [version] $identity.Version
    if ($Channel -eq 'Store' -and ($version.Major -lt 1 -or $version.Revision -ne 0)) { throw 'Store package versions must have a positive major version and end in .0.' }
    if ($manifest.Package.Properties.PublisherDisplayName -cne $source.Package.Properties.PublisherDisplayName) {
        throw 'PublisherDisplayName does not match the Store identity.'
    }
    if ($null -ne $archive.GetEntry('AppxSignature.p7x')) { throw 'The artifact must be unsigned.' }

    $application = $manifest.Package.Applications.Application
    if ($application.Executable -ne 'Spek-Z.exe' -or $application.EntryPoint -ne 'Windows.FullTrustApplication') {
        throw 'Unexpected application executable or entry point.'
    }
    if (-not $manifest.SelectSingleNode("//*[local-name()='Capability' and @Name='runFullTrust']")) {
        throw 'The desktop app needs runFullTrust.'
    }
    if (-not $manifest.SelectSingleNode("//*[local-name()='ExecutionAlias' and @Alias='spek-z.exe']")) {
        throw 'The command-line execution alias is missing.'
    }
    if (-not $manifest.SelectSingleNode("//*[local-name()='FileType' and text()='.flac']")) {
        throw 'The FLAC file association is missing.'
    }

    $runtimeDependency = $manifest.SelectSingleNode("//*[local-name()='PackageDependency' and starts-with(@Name, 'Microsoft.WindowsAppRuntime.')]")
    if ($Channel -eq 'Store' -and -not $runtimeDependency) {
        throw 'The Store package must declare its Windows App Runtime dependency.'
    }
    $unsignedOid = ', OID.2.25.311729368913984317654407730594956997722=1'
    if ($Channel -eq 'Sideload') {
        if ($identity.Publisher -cne "$($store.Package.Identity.Publisher)$unsignedOid") { throw 'The sideload publisher is missing the unsigned-package marker.' }
        if ($runtimeDependency) { throw 'The sideload package must bundle the Windows App Runtime.' }
        CheckMachine 'Microsoft.UI.Xaml.dll'
        if ([version] $manifest.Package.Dependencies.TargetDeviceFamily.MinVersion -lt [version] '10.0.22000.0') {
            throw 'Unsigned sideload installation requires Windows 11.'
        }
    }
    foreach ($name in 'Spek-Z.exe', 'ffmpeg/ffmpeg.exe', 'coreclr.dll', 'Microsoft.Graphics.Canvas.dll') {
        CheckMachine $name
    }
    foreach ($name in 'LICENSE.txt', 'Assets/spek-z.svg', 'Assets/WindowIcon.ico', 'resources.pri', 'ffmpeg/BUILD.txt',
        'ffmpeg/licenses/FFmpeg-LGPL-2.1.txt', 'ffmpeg/licenses/FDK-AAC.txt', 'ffmpeg/licenses/LAME-LGPL.txt',
        'ffmpeg/licenses/Ogg.txt', 'ffmpeg/licenses/Opus.txt', 'ffmpeg/licenses/Vorbis.txt', 'ffmpeg/licenses/zlib.txt') {
        $null = RequireEntry $name
    }
    foreach ($name in 'Square44x44Logo', 'Square150x150Logo', 'SmallTile', 'LargeTile', 'Wide310x150Logo', 'SplashScreen', 'StoreLogo') {
        $null = RequireEntry "Assets/$name.scale-100.png"
    }
    Write-Host "Validated unsigned $Architecture $Channel package $($identity.Name) $($identity.Version)."
}
finally { $archive.Dispose() }
