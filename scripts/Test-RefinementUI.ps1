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
function ScrollState { (Ui @('get-property', 'SpectrogramOverview', '--json') | ConvertFrom-Json).properties }
function Nodes($items) { foreach ($item in $items) { if ($item) { $item; Nodes $item.children } } }
function InspectionText {
    $tree = Ui @('inspect', '--json', '--depth', '12') | ConvertFrom-Json
    (Nodes @($tree.windows | ForEach-Object elements) | Where-Object automationId -eq 'InspectionStatus' | Select-Object -First 1).name
}
function Element([string] $Id) {
    $tree = Ui @('inspect', '--json', '--depth', '12') | ConvertFrom-Json
    $match = Nodes @($tree.windows | ForEach-Object elements) | Where-Object { $_.automationId -eq $Id -and -not $_.isOffscreen } | Sort-Object width -Descending | Select-Object -First 1
    if (-not $match) { throw "No visible $Id element." }
    $match
}
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
    Ui @('wait-for', 'ZoomReadout', '-t', '3000') | Out-Null
    Ui @('hover', 'Spectrogram', '--dwell-time', '200') | Out-Null
    Test-Ui 'Whole-file view has 100 percent zoom and hides the minimap' {
        Ui @('send-keys', 'r', '--target', 'Spectrogram', '--via', 'send-input')
        Ui @('wait-for', 'SpectrogramOverview', '--gone', '-t', '3000')
        if ((Text 'ZoomReadout') -ne 'Zoom: 100%') { throw 'Whole-file zoom readout is not 100 percent.' }
    }
    Test-Ui 'Light center zoom keeps the FFT and shows the minimap' {
        $fft = Text 'FftButton'
        Ui @('send-keys', 'r vk=0xBB', '--target', 'Spectrogram', '--via', 'send-input')
        if ((Text 'FftButton') -ne $fft) { throw 'Zoom changed the FFT size.' }
        Ui @('wait-for', 'SpectrogramOverview', '-t', '3000')
        Ui @('wait-for', 'ZoomRefinementProgress', '--gone', '-t', '10000')
        if ((Text 'ZoomReadout') -ne 'Zoom: 150%') { throw 'The matching zoom readout did not update.' }
        Screenshot '01-refined-center.png'
    }
    Test-Ui 'Reset restores both axes' {
        Ui @('invoke', 'ResetZoom')
        if ((Text 'ZoomReadout') -ne 'Zoom: 100%') { throw 'Reset did not restore the overview.' }
        Screenshot '02-reset.png'
    }
    Test-Ui 'Pointer wheel preserves time and frequency after refinement' {
        Ui @('send-keys', 'vk=0xBB', '--target', 'Spectrogram', '--via', 'send-input')
        Ui @('hover', 'Spectrogram', '--dwell-time', '200')
        $before = Text 'HoverReadout'
        $code = @'
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class ZoomInput { [DllImport("user32.dll")] public static extern void keybd_event(byte k,byte s,uint f,UIntPtr e); [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e); }'
[ZoomInput]::keybd_event(17,0,0,[UIntPtr]::Zero)
try { [ZoomInput]::mouse_event(2048,0,0,240,[UIntPtr]::Zero); Start-Sleep -Milliseconds 100 }
finally { [ZoomInput]::keybd_event(17,0,2,[UIntPtr]::Zero) }
'@
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($code))
        Checked @('target', 'exec', 'sandbox', '--', 'powershell.exe', '-NoProfile', '-NonInteractive', '-EncodedCommand', $encoded)
        Ui @('wait-for', 'ZoomRefinementProgress', '--gone', '-t', '10000')
        $after = Text 'HoverReadout'
        # The refined level can differ. The coordinate before "Hz" must remain fixed under the pointer.
        if (($before -split 'Hz')[0] -ne ($after -split 'Hz')[0]) { throw "Pointer coordinates changed: $before became $after" }
        Screenshot '03-refined-pointer.png'
    }
    Test-Ui 'Rapid keyboard zooms settle on the final view' {
        Ui @('send-keys', 'r vk=0xBB vk=0xBB vk=0xBB vk=0xBB', '--target', 'Spectrogram', '--via', 'send-input')
        Ui @('wait-for', 'ZoomRefinementProgress', '--gone', '-t', '10000')
        if ((Text 'ZoomReadout') -ne 'Zoom: 506.25%') { throw 'A stale zoom replaced the final zoom level.' }
        $scroll = ScrollState
        if ([Math]::Abs([double]$scroll.ScrollHorizontalPercent - 50) -gt 0.1 -or [Math]::Abs([double]$scroll.ScrollVerticalPercent - 50) -gt 0.1) { throw 'Final zoom was not centered on both axes.' }
        Screenshot '04-final-zoom.png'
    }
    Test-Ui 'Compact controls and readout do not overlap the height-limited minimap' {
        $map = Element 'SpectrogramOverview'
        $label = Element 'ReferenceLinesLabel'
        $zoom = Element 'ZoomReadout'
        $hover = Element 'HoverReadout'
        foreach ($id in @('ReferenceLines', 'ShowSpectrum', 'ShowCutoff', 'ResetZoom', 'HoverReadout', 'ZoomReadout')) {
            $item = Element $id
            if ($item.x + $item.width -gt $map.x + 1) { throw "Minimap overlaps $id." }
        }
        if ($map.height -gt ($zoom.y + $zoom.height - $label.y + 1)) { throw 'Minimap exceeds the controls/readout height.' }
        if ([Math]::Abs($map.y - $label.y) -gt 4) { throw 'Minimap is not aligned with the reference controls.' }
        if ([Math]::Abs($hover.y - $zoom.y) -gt 1) { throw 'Coordinates and zoom are not on the same row.' }
        if ((InspectionText) -match 'Zoom refined|Refining zoom|Zoom ready') { throw 'Progress or completion text remains in the footer.' }
    }
    Test-Ui 'Numeric readout fields and zoom stay fixed as coordinates change' {
        $ids = @('TimeReadout', 'FrequencyReadout', 'LevelReadout', 'ZoomReadout')
        $before = @{}
        foreach ($id in $ids) { $before[$id] = Element $id }
        Ui @('send-keys', 'right down right down', '--target', 'Spectrogram', '--via', 'send-input')
        foreach ($id in $ids) {
            $after = Element $id
            if ($after.x -ne $before[$id].x -or $after.width -ne $before[$id].width) { throw "$id moved as the inspected values changed." }
        }
    }
    Test-Ui 'Minimap click and diagonal drag pan both axes without changing selection or zoom' {
        if ((Text 'ShowSpectrum') -ne 'On') { Ui @('invoke', 'ShowSpectrum') }
        Ui @('send-keys', 'right shift+right', '--target', 'Spectrogram', '--via', 'send-input')
        $passage = Text 'SpectrumProfile'
        if ($passage -notmatch 'selected passage, [\d.]+ to [\d.]+ s') { throw 'No passage selected before panning.' }
        $zoom = Text 'ZoomReadout'
        $strip = Element 'SpectrogramOverview'
        $width = $strip.width - 8
        $height = $strip.height - 8
        $left = "$([int]($strip.x + 4 + $width * 0.2)),$([int]($strip.y + 4 + $height * 0.7))"
        $right = "$([int]($strip.x + 4 + $width * 0.6)),$([int]($strip.y + 4 + $height * 0.3))"
        Ui @('drag', $left, $left, '--hold-ms', '80')
        $scrollBefore = ScrollState
        if ([Math]::Abs([double]$scrollBefore.ScrollHorizontalPercent - 12.62) -gt 2) { throw 'Clicking the minimap did not recenter time.' }
        Ui @('drag', $left, $right)
        $scrollAfter = ScrollState
        if ([Math]::Abs([double]$scrollAfter.ScrollHorizontalPercent - 62.46) -gt 3) { throw 'Dragging the minimap did not pan time.' }
        if ([double]$scrollAfter.ScrollVerticalPercent -ge [double]$scrollBefore.ScrollVerticalPercent) { throw 'Diagonal dragging did not pan frequency upward.' }
        if ((Text 'ZoomReadout') -ne $zoom -or (Text 'SpectrumProfile') -ne $passage) { throw 'Panning changed zoom or the selected passage.' }
        Screenshot '05-overview-pan.png'
    }
    Test-Ui 'Overview supports keyboard and accessible range navigation' {
        $zoom = Text 'ZoomReadout'
        Ui @('send-keys', 'home', '--target', 'SpectrogramOverview', '--via', 'send-input')
        if ([double](ScrollState).ScrollHorizontalPercent -ne 0) { throw 'Home did not reach the start of the track.' }
        Ui @('send-keys', 'end', '--target', 'SpectrogramOverview', '--via', 'send-input')
        if ([Math]::Abs([double](ScrollState).ScrollHorizontalPercent - 100) -gt 0.0001) { throw 'End did not reach the end of the track.' }
        Ui @('set-value', 'SpectrogramOverview', '6')
        if ([Math]::Abs([double](ScrollState).ScrollHorizontalPercent - 37.38) -gt 0.1) { throw 'Accessible panning did not reach six seconds.' }
        $before = [double](ScrollState).ScrollVerticalPercent
        Ui @('send-keys', 'down', '--target', 'SpectrogramOverview', '--via', 'send-input')
        if ([double](ScrollState).ScrollVerticalPercent -le $before) { throw 'Down arrow did not pan frequency.' }
        if ((Text 'ZoomReadout') -ne $zoom) { throw 'Accessible panning changed zoom.' }
        Ui @('send-keys', 'r', '--target', 'SpectrogramOverview', '--via', 'send-input')
        Ui @('wait-for', 'SpectrogramOverview', '--gone', '-t', '3000')
        if ((Text 'ZoomReadout') -ne 'Zoom: 100%') { throw 'Reset did not normalize both zoom factors.' }
    }
    Test-Ui 'Disabling the spectrum profile clears its time passage' {
        Ui @('send-keys', 'r', '--target', 'Spectrogram', '--via', 'send-input')
        if ((Text 'ShowSpectrum') -ne 'On') { Ui @('invoke', 'ShowSpectrum') }
        Ui @('send-keys', 'right shift+right', '--target', 'Spectrogram', '--via', 'send-input')
        Ui @('wait-for', 'SpectrumProfile', '-p', 'Name', '--value', 'selected passage,', '--contains', '-t', '3000')
        Ui @('invoke', 'ShowSpectrum')
        if ((InspectionText) -match 'Passage [0-9]') { throw 'The profile is off but a passage is still selected.' }
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
