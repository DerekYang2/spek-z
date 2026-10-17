#Requires -Version 7
# Checks a Sandbox Spek-Z window with a 20-second, 48 kHz audio file already open.
# Open the fixture before running this script. Native file-picker automation is deliberately excluded.
param(
    [Parameter(Mandatory)] [int] $AppPid,
    [Parameter(Mandatory)] [string] $ArtifactDirectory
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = "spek-z-refinement-$([guid]::NewGuid().ToString('N'))"
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()
function Checked([string[]] $Arguments) {
    $output = @(& winapp @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "winapp $($Arguments -join ' ') failed: $($output -join ' ')" }
    $output
}
function Ui([string[]] $Arguments) { Checked (@('ui') + $Arguments + @('--on', 'sandbox', '-a', "$AppPid")) }
function Text([string] $Id) { (Ui @('get-value', $Id, '--json') | ConvertFrom-Json).text }
function Nodes($items) { foreach ($item in $items) { if ($item) { $item; Nodes $item.children } } }
function Test-Ui([string] $Name, [scriptblock] $Action) {
    try { & $Action | Out-Null; $results.Add(@{ name = $Name; status = 'PASS' }) }
    catch { $results.Add(@{ name = $Name; status = 'FAIL'; detail = "$_" }) }
}
function Screenshot([string] $Name) {
    $path = Join-Path $ArtifactDirectory $Name
    Ui @('screenshot', '--output', $path) | Out-Null
    if (-not (Test-Path $path) -or (Get-Item $path).Length -eq 0) { throw 'Screenshot was not delivered.' }
    $screenshots.Add($path)
}
try {
    $ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
    New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null
    Ui @('wait-for', 'SaveButton', '-p', 'IsEnabled', '--value', 'True', '-t', '15000') | Out-Null
    Ui @('hover', 'Spectrogram', '--dwell-time', '200') | Out-Null
    Test-Ui 'Center zoom refines without changing the FFT' {
        $fft = Text 'FftButton'
        Ui @('send-keys', 'r vk=0xBB', '--target', 'Spectrogram', '--via', 'send-input')
        Ui @('wait-for', 'InspectionStatus', '--value', 'Zoom refined.', '--contains', '-t', '10000')
        $status = Text 'InspectionStatus'
        if ($status -notmatch 'Time view 3\.33 to 16\.67 s\b' -or $status -notmatch 'Frequency view 4\.00 to 20\.00 kHz\b') { throw "Wrong center zoom: $status" }
        if ((Text 'FftButton') -ne $fft) { throw 'Zoom changed the FFT size.' }
        Screenshot '01-refined-center.png'
    }
    Test-Ui 'Reset restores both axes' {
        Ui @('invoke', 'ResetZoom')
        if ((Text 'InspectionStatus') -match 'Time view|Frequency view|Refining') { throw 'Reset did not restore the overview.' }
        Screenshot '02-reset.png'
    }
    Test-Ui 'Pointer wheel preserves time and frequency after refinement' {
        Ui @('hover', 'Spectrogram', '--dwell-time', '200')
        $before = Text 'HoverReadout'
        $code = @'
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class ZoomInput { [DllImport("user32.dll")] public static extern void keybd_event(byte k,byte s,uint f,UIntPtr e); [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e); }'
[ZoomInput]::keybd_event(17,0,0,[UIntPtr]::Zero)
try { [ZoomInput]::mouse_event(2048,0,0,120,[UIntPtr]::Zero); Start-Sleep -Milliseconds 100 }
finally { [ZoomInput]::keybd_event(17,0,2,[UIntPtr]::Zero) }
'@
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($code))
        Checked @('target', 'exec', 'sandbox', '--', 'powershell.exe', '-NoProfile', '-NonInteractive', '-EncodedCommand', $encoded)
        Ui @('wait-for', 'InspectionStatus', '--value', 'Zoom refined.', '--contains', '-t', '10000')
        $after = Text 'HoverReadout'
        # The refined level can differ. The coordinate before "Hz" must remain fixed under the pointer.
        if (($before -split 'Hz')[0] -ne ($after -split 'Hz')[0]) { throw "Pointer coordinates changed: $before became $after" }
        Screenshot '03-refined-pointer.png'
    }
    Test-Ui 'Rapid keyboard zooms settle on the final view' {
        Ui @('send-keys', 'r vk=0xBB vk=0xBB vk=0xBB vk=0xBB', '--target', 'Spectrogram', '--via', 'send-input')
        Ui @('wait-for', 'InspectionStatus', '--value', 'Zoom refined.', '--contains', '-t', '10000')
        if ((Text 'InspectionStatus') -notmatch 'Time view 8\.02 to 11\.98 s\b') { throw 'A stale zoom replaced the final interval.' }
        Screenshot '04-final-zoom.png'
    }
    Test-Ui 'Disabling the spectrum profile clears its time passage' {
        Ui @('send-keys', 'r', '--target', 'Spectrogram', '--via', 'send-input')
        if ((Text 'ShowSpectrum') -ne 'On') { Ui @('invoke', 'ShowSpectrum') }
        Ui @('send-keys', 'right shift+right', '--target', 'Spectrogram', '--via', 'send-input')
        Ui @('wait-for', 'InspectionStatus', '--value', 'Passage', '--contains', '-t', '3000')
        Ui @('invoke', 'ShowSpectrum')
        if ((Text 'InspectionStatus') -match 'Passage [0-9]') { throw 'The profile is off but a passage is still selected.' }
        Screenshot '05-profile-off.png'
    }
    Test-Ui 'Settings dB spinners remain inline with the fields' {
        Ui @('invoke', 'Settings')
        Ui @('wait-for', 'LowerBox', '-t', '3000')
        $tree = Ui @('inspect', '--json', '--depth', '12') | ConvertFrom-Json
        $lower = Nodes @($tree.windows | ForEach-Object elements) | Where-Object { $_.automationId -eq 'LowerBox' -and -not $_.isOffscreen } | Select-Object -First 1
        $children = @(Nodes @($lower.children))
        $inputBox = $children | Where-Object automationId -eq 'InputBox' | Select-Object -First 1
        $increase = $children | Where-Object automationId -eq 'UpSpinButton' | Select-Object -First 1
        $decrease = $children | Where-Object automationId -eq 'DownSpinButton' | Select-Object -First 1
        if (-not $inputBox -or -not $increase -or -not $decrease) { throw 'Missing inline dB controls.' }
        Ui @('invoke', $increase.selector)
        Ui @('wait-for', $inputBox.selector, '--value', '-119', '-t', '2000')
        Screenshot '06-settings-db.png'
        Ui @('invoke', $decrease.selector)
        Ui @('wait-for', $inputBox.selector, '--value', '-120', '-t', '2000')
        Ui @('invoke', 'CloseButton')
    }
} catch { $results.Add(@{ name = 'Test setup'; status = 'FAIL'; detail = "$_" }) }
finally {
    try { Checked @('ui', 'yield', '--on', 'sandbox') | Out-Null }
    finally { $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow }
}
$failed = @($results | Where-Object status -eq 'FAIL').Count
@{ target = 'sandbox'; appPid = $AppPid; passed = $results.Count - $failed; failed = $failed; results = @($results.ToArray()); screenshots = @($screenshots.ToArray()) } |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $ArtifactDirectory 'test-results.json')
$results | ForEach-Object { "$($_.status) $($_.name) $($_.detail)" }
if ($failed) { exit 1 }
