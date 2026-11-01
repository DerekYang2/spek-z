param(
    [ValidateSet('x64', 'arm64')]
    [string[]] $Architecture = @('x64', 'arm64'),
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\sideload')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\SpekZ\SpekZ.csproj'
[xml] $manifest = Get-Content (Join-Path $root 'src\SpekZ\Package.Sideload.appxmanifest') -Raw
[xml] $store = Get-Content (Join-Path $root 'src\SpekZ\Package.appxmanifest') -Raw
$version = [version] $manifest.Package.Identity.Version
if ($manifest.Package.Identity.Version -cne $store.Package.Identity.Version) { throw 'The Store and sideload manifest versions must match.' }
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
    if (-not (Test-Path (Join-Path $root "ffmpeg\win-$arch\ffmpeg.exe"))) {
        & (Join-Path $PSScriptRoot 'Get-Ffmpeg.ps1') -Architecture $arch
    }
    $package = Join-Path $output "Spek-Z_${version}_${arch}.msix"
    & winapp package $project --arch $arch --no-sign -p PackageChannel=Sideload --output $package
    if ($LASTEXITCODE -ne 0) { throw "The $arch sideload build failed." }
    & (Join-Path $PSScriptRoot 'Test-Package.ps1') -Package $package -Architecture $arch -Channel Sideload

    $staging = Join-Path $output ('.staging-' + [guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory $staging
    try {
        Copy-Item -LiteralPath $package -Destination $staging
        Copy-Item (Join-Path $PSScriptRoot 'install_spekz.cmd'), (Join-Path $PSScriptRoot 'install_spekz.ps1') -Destination $staging
        $zip = Join-Path $output "Spek-Z_${version}_${arch}.zip"
        Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -Force
        Write-Host "Unsigned sideload ZIP: $zip"
    }
    finally { Remove-Item -LiteralPath $staging -Recurse -Force }
}
