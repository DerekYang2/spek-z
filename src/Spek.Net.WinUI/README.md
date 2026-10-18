# Spek.NET.WinUI

`SpekView` is a WinUI 3 control that shows [Spek](https://github.com/alexkay/spek)'s spectrogram of an audio file and draws the columns as the file decodes. It's drawn with Win2D and uses [Spek.NET](https://www.nuget.org/packages/Spek.NET) for the analysis.

```xml
<Page xmlns:spek="using:Spek.Net.WinUI">
    <spek:SpekView x:Name="Spectrogram" FilePath="C:\Music\song.flac" />
</Page>
```

```csharp
Spectrogram.FfmpegPath = @"C:\Tools\ffmpeg.exe"; // optional; otherwise ffmpeg is taken from PATH
Spectrogram.Palette = Palette.Sox;
await Spectrogram.SaveAsync(@"C:\Temp\song.png");
```

- Changing `FilePath`, `Stream`, `Channel`, `FftBits` or `WindowFunction`, or the control's width, analyses again. Changing `Palette`, `LowerDb` or `UpperDb` recolours what's there.
- `StreamCount`, `ChannelCount`, `Description` and `IsAnalyzing` report the current file, and `AnalysisCompleted` fires when a file is done.
- `ShowFullPath`, `ShowDetailedDescription`, `BrandName`, `BrandVersion` and `Interpolation` change how it looks.
- With focus, the control takes Spek's keys: `c`, `f`, `l`, `p`, `s`, `u` and `w`, with Shift to step back.
- `SpekExporter.ExportAsync` renders a file to a PNG without a window.

## Inspection

- Hover or use arrow keys for the time, frequency and level readout in `ReadoutText`.
- `ReadoutTimeText`, `ReadoutFrequencyText` and `ReadoutLevelText` expose the same values separately. Hosts can bind them to fixed-width fields so changing digits do not move neighboring controls. All four readout properties clear when the inspection cursor leaves the plot.
- `TimeZoomFactor` and `FrequencyZoomFactor` report each axis's magnification relative to the full file. `DurationSeconds` and `NyquistFrequency` report the full time and frequency extents. Read these properties when `InspectionChanged` fires. `PanTo(timeSeconds, frequencyHz)` recenters both axes in one update, clamps at file boundaries and preserves their spans and the profile selection.
- Add a `SpectrogramOverview` with its `Source` set to the `SpekView` to provide a two-axis minimap. Its default size is 200 × 100 device-independent pixels. It appears whenever either zoom factor, converted to a percentage and rounded to two decimal places, exceeds 100%. It hides at the whole-file view. It reuses the cached whole-file bitmap without another decode. Its rectangle has the actual proportions of the visible time and frequency ranges, with a larger invisible hit target for very small rectangles. Click to recenter or drag to pan both axes. Left/right pan time; up/down pan frequency. Page Up, Page Down, Home and End navigate time. Panning preserves zoom and the profile selection. UI Automation clients can pan both axes through the Scroll pattern, or set the visible start time in seconds through RangeValue. The minimap is not included in PNG exports.
- The minimap overlays a progress ring while its source is refining the visible time interval. It hides the ring on completion, reset, source changes and unload. Hosts can keep `RefinementStatus` for errors without displaying progress or completion text. The host must merge the standard `XamlControlsResources` dictionary in `App.xaml` for the progress ring, as with other WinUI controls.
- Ctrl+wheel zooms both time and frequency around the pointer. `+` and `-` zoom around the center of the view, and `R` resets both axes. `MinimumFrequency` and `MaximumFrequency` expose the frequency interval; a maximum of 0 selects Nyquist. `MinimumTime` and `MaximumTime` expose the time interval in seconds; a maximum of 0 selects the end of the file. `VisibleTimeRange` reports the effective interval. `ZoomToTimeRange` and `ZoomToFrequencyRange` set each interval independently. `ResetZoom` resets both axes, while `ResetFrequencyZoom` resets only frequency for existing hosts.
- Whole-file analysis and light time zoom keep the original fast path. At 4x time zoom or deeper, refinement switches to dense, file-anchored FFTs with context on both sides and linear-power pixel averages. Detailed views share a measurement grid; the fast overview deliberately does not. Crossing that threshold can change brightness and event placement slightly. A cached fast result cannot suppress a required detailed analysis.
- Zooming first scales the current image. Refinement waits 350 ms, then runs in the background while the old image stays visible. A new zoom cancels obsolete work; reset uses the cached overview without another decode. One completed detail interval is cached, with value/pixel buffers capped at 32 MiB. `IsRefining` includes the debounce wait, and `RefinementStatus` reports progress, completion or failure without changing `IsAnalyzing`.
- The FFT size does not change. Interactive time zoom stops at one FFT window or 0.01 seconds, whichever is longer. Hover and keyboard readout use refined data where available, and selection uses absolute file times. The profile and cutoff still use the overview's selected passage, or the whole file if no passage is selected; zoom refinement does not change those measurements.
- `SaveAsync` exports the current view, including completed detail. It does not wait for pending refinement or start another decode. Failures preserve the existing image, and unloading cancels work and clears the detail cache.
- Drag across the plot or use Shift+left/right to select a passage. `SelectTimeRange` takes start and end seconds, and `ClearSelection` or Escape selects the whole file.
- Set `ShowSpectrum` to get `SpectrumProfile` after analysis. `SpectrumProfileView` draws that profile. Set its `Profile` and `FrequencyRange` when `SpekView.InspectionChanged` fires.
- Set `SpectrumProfileView.Caption` to include the selected passage's times without a separate status row. The default is "Average spectral level of the whole track". The control uses the caption as its accessible name too.
- Set `ShowCutoffMarker` to draw the optional `MeasuredDrop`. This measures a sustained spectral edge, not the original bitrate, codec or lossless authenticity.
- Set `ReferenceLines` to `ReferenceLines.Mp3Lame` for approximate LAME stereo CBR low-pass references. The default is `None`.

`SaveAsync` exports the current zoom, reference lines and optional cutoff marker. `ExportOptions` provides the same reference and frequency options for a windowless export.

The control is written in C# with no XAML file, so there's no compiled XAML to merge into your app. It needs Windows App SDK 2.5.1 or later, Win2D 1.4.0, and an app built for x64 or ARM64.

Licensed under GPL-3.0, like Spek. Source: https://github.com/DerekYang2/spek-z
