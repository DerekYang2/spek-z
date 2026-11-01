param([switch] $Elevated)

$ErrorActionPreference = 'Stop'
try {
    if ([Environment]::OSVersion.Version.Build -lt 22000) { throw 'Unsigned sideload installation requires Windows 11.' }
    $packages = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.msix' -File)
    if ($packages.Count -ne 1) { throw "Expected exactly one MSIX next to this installer; found $($packages.Count)." }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
    try {
        $entry = $archive.GetEntry('AppxManifest.xml')
        if (-not $entry) { throw 'The package has no AppxManifest.xml.' }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml] $manifest = $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    finally { $archive.Dispose() }
    $publisher = 'CN=DD45FB67-1F5A-4180-88DE-453755BBC3D4, OID.2.25.311729368913984317654407730594956997722=1'
    if ($manifest.Package.Identity.Name -cne 'DerekYang2.Spek-Z' -or $manifest.Package.Identity.Publisher -cne $publisher) {
        throw 'This installer expects the unsigned Spek-Z sideload package, not a Store package.'
    }

    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Add-AppxPackage -Path $packages[0].FullName -AllowUnsigned -ForceApplicationShutdown
    }
    else {
        if ($Elevated) { throw 'Administrator privileges are required to install this unsigned package.' }
        Write-Host 'Installing Spek-Z. Please accept the administrator prompt.'
        $process = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList (
            '-NoProfile -ExecutionPolicy Bypass -File "{0}" -Elevated' -f $PSCommandPath
        )
        if ($process.ExitCode -ne 0) { throw "Installation failed with exit code $($process.ExitCode)." }
    }

    if (-not $Elevated) {
        $installed = @(Get-AppxPackage -Name 'DerekYang2.Spek-Z' | Where-Object { $_.Publisher -ceq $publisher })
        if ($installed.Count -ne 1) { throw 'The package was not found for the current user. Accept elevation using the same Windows account.' }
        Start-Process "shell:AppsFolder\$($installed[0].PackageFamilyName)!App"
        Write-Host 'Spek-Z installed successfully.'
    }
    exit 0
}
catch {
    [Console]::Error.WriteLine("ERROR: $($_.Exception.Message)")
    exit 1
}
