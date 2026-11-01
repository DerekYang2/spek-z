param(
    [ValidateSet('x64', 'arm64')]
    [string[]] $Architecture = @('x64', 'arm64'),
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\store')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\SpekZ\SpekZ.csproj'
[xml] $manifest = Get-Content (Join-Path $root 'src\SpekZ\Package.appxmanifest') -Raw
$version = [version] $manifest.Package.Identity.Version
if ($version.Major -lt 1 -or $version.Revision -ne 0) { throw 'Store package versions must have a positive major version and end in .0.' }
if (-not (Get-Command winapp -ErrorAction SilentlyContinue)) { throw 'Install WinApp CLI 0.7.1 or later before packaging.' }
$cliOutput = @(& winapp --version)
if ($LASTEXITCODE -ne 0) { throw "WinApp CLI could not report its version: $($cliOutput -join ' ')" }
$cliVersion = $cliOutput | ForEach-Object { "$($_)".Trim() } |
    Where-Object { $_ -match '^\d+\.\d+\.\d+(?:\.\d+)?(?:[-+]\S+)?$' } | Select-Object -Last 1
if (-not $cliVersion -or [version] ($cliVersion -split '[-+]')[0] -lt [version] '0.7.1') {
    throw "WinApp CLI 0.7.1 or later is required. Output: $($cliOutput -join ' ')"
}

$output = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory $output -Force
foreach ($arch in ($Architecture | Select-Object -Unique)) {
    $ffmpeg = Join-Path $root "ffmpeg\win-$arch\ffmpeg.exe"
    if (-not (Test-Path $ffmpeg)) { & (Join-Path $PSScriptRoot 'Get-Ffmpeg.ps1') -Architecture $arch }
    $package = Join-Path $output "Spek-Z_${version}_${arch}.msix"
    & winapp package $project --arch $arch --no-sign --output $package
    if ($LASTEXITCODE -ne 0) { throw "The $arch Store build failed." }
    & (Join-Path $PSScriptRoot 'Test-Package.ps1') -Package $package -Architecture $arch -Channel Store
    Write-Host "Store upload artifact: $package"
}
