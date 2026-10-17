#Requires -Version 7
# Tests a running Spek-Z window with WinApp CLI 0.7+. FixturePath must exist in the selected target.
param(
    [Parameter(Mandatory)] [ValidateSet('sandbox', 'local')] [string] $Target,
    [Parameter(Mandatory)] [int] $AppPid,
    [Parameter(Mandatory)] [string] $FixturePath,
    [Parameter(Mandatory)] [string] $ArtifactDirectory
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = "spek-z-test-$([guid]::NewGuid().ToString('N'))"
$scope = if ($Target -eq 'sandbox') { @('--on', 'sandbox') } else { @() }
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()

function Checked([string[]] $Arguments) {
    $output = @(& winapp @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "winapp $($Arguments -join ' ') failed: $($output -join ' ')" }
    $output
}
function Ui([string[]] $Arguments, [string] $Window) {
    $selector = if ($Window) { @('-w', $Window) } elseif ($script:hwnd) { @('-w', $script:hwnd) } else { @('-a', "$AppPid") }
    Checked (@('ui') + $Arguments + $scope + $selector)
}
function Test-Ui([string] $Name, [scriptblock] $Test) {
    try { & $Test | Out-Null; $results.Add(@{ name = $Name; status = 'PASS' }) }
    catch { $results.Add(@{ name = $Name; status = 'FAIL'; detail = "$_" }) }
}
function Elements($nodes) {
    foreach ($node in $nodes) { if ($node) { $node; Elements $node.children } }
}
function Element([string] $Id) {
    $tree = Ui @('inspect', '--json', '--depth', '8') -Window $script:hwnd | ConvertFrom-Json
    $found = @(Elements @($tree.windows | ForEach-Object elements) | Where-Object { $_.automationId -eq $Id -and -not $_.isOffscreen } | Sort-Object width -Descending)
    if ($found.Count -gt 1 -and $Id -eq 'SpectrumProfile') { return $found[0] }
    if ($found.Count -ne 1) { throw "Expected one $Id element, got $($found.Count)." }
    $found[0]
}
function Screenshot([string] $Name) {
    $path = Join-Path $ArtifactDirectory $Name
    Ui @('screenshot', '--output', $path) -Window $script:hwnd | Out-Null
    if (-not (Test-Path $path) -or (Get-Item $path).Length -eq 0) { throw 'Screenshot was not delivered.' }
    $screenshots.Add($path)
}
function Pick-File([string] $Action, [string] $Path) {
    Ui @('invoke', $Action) -Window $script:hwnd | Out-Null
    Start-Sleep -Milliseconds 500
    $windows = Checked (@('ui', 'list-windows', '--json') + $scope) | ConvertFrom-Json
    $picker = @($windows | Where-Object { "$($_.ownerHwnd)" -eq $script:hwnd -and $_.className -eq '#32770' })
    if ($picker.Count -ne 1) { throw 'Expected one file picker owned by the app.' }
    $pickerWindow = "$($picker[0].hwnd)"
    $tree = Ui @('inspect', '--interactive', '--json') -Window $pickerWindow | ConvertFrom-Json
    $edit = Elements @($tree.windows | ForEach-Object elements) | Where-Object { $_.type -eq 'Edit' -and $_.name -eq 'File name:' } | Select-Object -First 1
    if (-not $edit) { throw 'The file picker has no filename edit.' }
    # Native file dialogs can keep their previously selected item after a programmatic ValuePattern update. Type and
    # leave the filename field so the dialog commits the new path before its Open or Save button is invoked.
    Ui @('send-keys', 'ctrl+a', '--target', $edit.selector, '--via', 'send-input') -Window $pickerWindow | Out-Null
    # Forward slashes avoid the keyboard driver's text escapes such as \t in a filename beginning with "t".
    Ui @('send-keys', $Path.Replace('\', '/'), '--verbatim', '--via', 'send-input') -Window $pickerWindow | Out-Null
    Ui @('send-keys', 'tab', '--via', 'send-input') -Window $pickerWindow | Out-Null
    Ui @('invoke', '1') -Window $pickerWindow | Out-Null
}

try {
    $ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
    New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null
    $windows = Ui @('list-windows', '--json') | ConvertFrom-Json
    $window = $windows | Where-Object className -eq 'WinUIDesktopWin32WindowClass' | Select-Object -First 1
    if (-not $window) { throw 'No Spek-Z window in the selected target.' }
    $script:hwnd = "$($window.hwnd)"

    Test-Ui 'Open the generated transcode' {
        Pick-File 'Open' $FixturePath
        Ui @('wait-for', 'SaveButton', '-p', 'IsEnabled', '--value', 'True', '-t', '10000')
        $opened = Ui @('list-windows', '--json') | ConvertFrom-Json
        if (-not ($opened | Where-Object { $_.title -eq "$([IO.Path]::GetFileName($FixturePath)) - Spek-Z" })) {
            throw 'The file picker did not open the requested fixture.'
        }
        Start-Sleep -Milliseconds 1000
        Screenshot '01-file.png'
    }
    Test-Ui 'Reference lines default to None' {
        Ui @('wait-for', 'ReferenceLines', '--value', 'None', '-t', '2000')
    }
    Test-Ui 'Show MP3 reference lines' {
        Ui @('send-keys', 'home down enter', '--target', 'ReferenceLines', '--via', 'send-input')
        Ui @('send-keys', 'esc', '--via', 'send-input')
        Ui @('wait-for', 'ReferenceLines', '--value', 'MP3 - LAME defaults', '-t', '3000')
        Ui @('wait-for', 'InspectionStatus', '--value', 'nominal LAME', '--contains', '-t', '3000')
        Screenshot '02-guides.png'
    }
    Test-Ui 'Show the spectrum and measured cutoff' {
        Ui @('invoke', 'ShowSpectrum')
        Ui @('invoke', 'ShowCutoff')
        Ui @('wait-for', 'InspectionStatus', '--value', 'Measured spectral drop', '--contains', '-t', '10000')
        $profile = Element 'SpectrumProfile'
        if ($profile.isOffscreen -or $profile.height -lt 100) { throw 'The spectrum graph is not visible.' }
        Screenshot '03-profile.png'
    }
    Test-Ui 'Hover shows frequency and level' {
        Ui @('hover', 'Spectrogram', '--dwell-time', '100')
        Ui @('wait-for', 'HoverReadout', '--value', 'Hz', '--contains', '-t', '2000')
        $text = Ui @('get-value', 'HoverReadout', '--json') | ConvertFrom-Json
        if ($text.text -notmatch 'dB') { throw 'Hover readout has no spectral level.' }
    }
    Test-Ui 'Keyboard zoom changes the view, not the data' {
        Ui @('send-keys', 'vk=0xBB', '--target', 'Spectrogram', '--via', 'send-input')
        Ui @('wait-for', 'InspectionStatus', '--value', 'Frequency view', '--contains', '-t', '3000')
        Screenshot '04-zoom.png'
        Ui @('invoke', 'ResetZoom')
        $text = Ui @('get-value', 'InspectionStatus', '--json') | ConvertFrom-Json
        if ($text.text -match 'Frequency view') { throw 'Reset zoom did not restore the full interval.' }
    }
    Test-Ui 'Dragging selects a time passage' {
        $plot = Element 'Spectrogram'
        $from = "$([int]($plot.x + 60 + ($plot.width - 152) * 0.2)),$([int]($plot.y + 60 + ($plot.height - 100) * 0.5))"
        $to = "$([int]($plot.x + 60 + ($plot.width - 152) * 0.7)),$([int]($plot.y + 60 + ($plot.height - 100) * 0.5))"
        Ui @('drag', $from, $to)
        Ui @('wait-for', 'InspectionStatus', '--value', 'Passage', '--contains', '-t', '5000')
        Screenshot '05-passage.png'
    }
    Test-Ui 'Keyboard selection and clear' {
        Ui @('send-keys', 'esc', '--target', 'Spectrogram', '--via', 'send-input')
        Ui @('wait-for', 'InspectionStatus', '--value', 'Using the whole track', '--contains', '-t', '3000')
        Ui @('send-keys', 'shift+right', '--target', 'Spectrogram', '--via', 'send-input')
        Ui @('wait-for', 'InspectionStatus', '--value', 'Passage', '--contains', '-t', '3000')
        Ui @('send-keys', 'esc', '--target', 'Spectrogram', '--via', 'send-input')
    }
    Test-Ui 'Save the inspected plot' {
        $output = if ($Target -eq 'sandbox') { 'C:\WinApp\work\inspection-export.png' } else { Join-Path $ArtifactDirectory 'inspection-export.png' }
        Pick-File 'SaveButton' $output
        if ($Target -eq 'sandbox') {
            Start-Sleep -Milliseconds 1000
            Checked @('target', 'pull', 'sandbox', 'inspection-export.png', (Join-Path $ArtifactDirectory 'inspection-export.png')) | Out-Null
        }
        if (-not (Test-Path (Join-Path $ArtifactDirectory 'inspection-export.png'))) { throw 'The PNG was not saved.' }
        $screenshots.Add((Join-Path $ArtifactDirectory 'inspection-export.png'))
    }
    Test-Ui 'Turn guides and panels off' {
        Ui @('send-keys', 'home enter', '--target', 'ReferenceLines', '--via', 'send-input')
        Ui @('send-keys', 'esc', '--via', 'send-input')
        Ui @('invoke', 'ShowSpectrum')
        Ui @('invoke', 'ShowCutoff')
        Ui @('wait-for', 'ReferenceLines', '--value', 'None', '-t', '2000')
        Screenshot '06-off.png'
    }
} catch {
    $results.Add(@{ name = 'Test setup'; status = 'FAIL'; detail = "$_" })
} finally {
    try { Checked (@('ui', 'yield') + $scope) | Out-Null }
    finally { $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow }
}

$failed = @($results | Where-Object status -eq 'FAIL').Count
$report = @{ target = $Target; appPid = $AppPid; passed = $results.Count - $failed; failed = $failed; results = @($results.ToArray()); screenshots = @($screenshots.ToArray()) }
$report | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $ArtifactDirectory 'test-results.json')
$results | ForEach-Object { "$($_.status) $($_.name) $($_.detail)" }
if ($failed) { exit 1 }
