# Spek-Z

Spek-Z is a spectrogram analyser for Windows, built with WinUI 3. It draws a spectrogram of an audio file as it decodes it.

Based on [Spek](https://github.com/alexkay/spek) and [spek-X](https://github.com/MikeWang000000/spek-X).

## Projects

| Project | What it is |
| --- | --- |
| `src/Spek.Net` | Spek.NET. Decodes audio with ffmpeg and turns it into spectrogram columns. Has no UI and targets `net10.0`. |
| `src/Spek.Net.WinUI` | Spek.NET.WinUI. The `SpekView` control, which draws the spectrogram with Win2D, and PNG export. |
| `src/SpekZ` | The standalone Spek-Z app. |
| `tests/Spek.Net.Tests` | MSTest tests for Spek.NET. |

## Building

You need the .NET 10 SDK and ffmpeg.

```powershell
.\scripts\Get-Ffmpeg.ps1            # ffmpeg for x64 into .\ffmpeg; add -Architecture x64, arm64 for both
dotnet build src\SpekZ               # add -p:Platform=arm64 for ARM64
dotnet test
```

`Get-Ffmpeg.ps1` downloads an audio-only LGPL build of FFmpeg from [ffmpeg-audio](https://github.com/DerekYang2/ffmpeg-audio), about 8 MB instead of 130 MB for a full build, into `ffmpeg\win-x64` or `ffmpeg\win-arm64`, which git ignores. It checks each zip's SHA-256 against the hashes in the script. The app build copies `ffmpeg.exe`, its licenses and `BUILD.txt` to `ffmpeg\` next to `Spek-Z.exe`. To move to a newer ffmpeg-audio release, change the release tag and the two hashes in the script.

Spek-Z looks for ffmpeg in this order:

1. The path in its settings.
2. `ffmpeg\ffmpeg.exe` or `ffmpeg.exe` next to `Spek-Z.exe`.
3. `ffmpeg.exe` on `PATH`.

The tests that run ffmpeg use `SPEK_Z_FFMPEG`, then the old `FLUENTSPEK_FFMPEG` variable, then `PATH`. They report inconclusive if none has it.

`scripts/Test-RefinementUI.ps1` checks a running Spek-Z window in Sandbox with a 20-second, 48 kHz audio fixture already open. Pass its guest process ID with `-AppPid` and a host evidence folder with `-ArtifactDirectory`. It checks centered zoom, reset, pointer-anchored wheel zoom and rapid zoom changes after background refinement. Results and screenshots go to the evidence folder. This script does not automate file pickers.

## Using Spek-Z

Open a file with the Open button, Ctrl+O, by dropping it on the window, or from the command line. The toolbar changes the palette, window function, FFT size, channel and stream, and Ctrl+S saves the view as a PNG.

With the spectrogram focused, Spek's keys work too. Each key steps forward, and with Shift it steps back:

| Key | Changes |
| --- | --- |
| `c` | Channel |
| `f` | Window function: Hann, Hamming, Blackman-Harris |
| `l` | Lower limit of the dB range |
| `p` | Palette: Spectrum, SoX, Mono |
| `s` | Audio stream |
| `u` | Upper limit of the dB range |
| `w` | FFT size, from 256 to 16384 samples |

Settings (Ctrl+E) has the dB limits, the full path and detailed description options, and the ffmpeg path. Columns appear as soon as they're analysed, as in Spek and spek-X. There is no artificial reveal delay.

Settings are saved in `%LOCALAPPDATA%\Spek-Z\settings.json`. If that file does not exist, Spek-Z loads the old `%LOCALAPPDATA%\FluentSpek\settings.json` file. Saving writes to the new location and leaves the old file intact.

### Inspecting a file

- Move the pointer over the plot to read the time, frequency and spectral level. Each value has a fixed-width, right-aligned field so changing digits do not move the zoom readout. The fields keep their space when the pointer leaves the plot. Arrow keys move the inspection cursor too.
- The crosshair is visible only while Spectrum profile is enabled. The time, frequency and level readout still works with the profile off.
- Hold Ctrl and scroll to zoom both time and frequency around the pointer. `+` and `-` zoom both axes around the center of the visible plot. The current image scales immediately. After zooming stops for 350 ms, the visible time range is analysed again in the background with more columns. The old image stays visible until its replacement is ready. The FFT size and frequency resolution stay unchanged. Press `R`, double-click the plot or use Reset zoom to restore the cached whole-file view immediately.
- Drag horizontally to select a time passage. Shift+left/right selects with the keyboard. Escape clears the selection.
- The coordinate/dB readout also shows zoom as a percentage. Matching axes use one value, such as `Zoom: 150%`. Different axes use `X: 150% / Y: 100%`, with X for time and Y for frequency. Reset zoom sits with the reference/profile controls. A small minimap appears to their right whenever either displayed zoom percentage is above 100%. It fits the available width and never exceeds the controls/readout block's height. Its rectangle marks the visible time and frequency ranges within the whole spectrogram. Click elsewhere in the minimap to recenter, or drag the rectangle to pan both axes without changing zoom or the selected passage. Left/right pan time; up/down pan frequency. Page Up and Page Down move by most of a time passage, and Home and End go to the track boundaries. A progress ring overlays the minimap during zoom refinement and disappears when it finishes. Refinement errors still appear as text.
- Turn on Spectrum profile to see the passage's average spectral level. With no selection it covers the whole file. The profile averages linear power from the spectrogram's dB values and excludes silent and unfinished columns. It is not a calibrated power spectral density.
- The profile caption includes the selected passage's start and end times, or says "Average spectral level of the whole track" when nothing is selected. This context no longer takes a separate footer row.
- Turning Spectrum profile off clears the time passage and its highlight. The cutoff measurement then uses the whole track.
- Cutoff marker looks for a sharp, sustained drop above 8 kHz in that profile. It reports the frequency and approximate drop in dB, not a source codec or bitrate. It may mark a deliberately filtered recording, and it may miss a transcode. It is not a real/fake verdict.
- Reference lines has None and MP3 - LAME defaults. The dotted guides are nominal LAME 3.100 stereo CBR low-pass targets: 17.0 kHz for 128 kbps, 18.6 for 192, 19.7 for 256 and 20.5 for 320. These are not MP3 requirements. The actual transition depends on sample rate; other encoders, VBR, mono and custom filters can differ.

The profile, marker and references are off by default. Settings remembers them. PNG export includes the current time and frequency zoom, available refined detail, references and cutoff marker, but not the cursor or selection highlight. Saving does not wait for pending refinement. Zooming does not change the analysis passage; select a passage to change the profile or cutoff measurement. The profile and cutoff continue to use the whole-file analysis, so their results do not change when zoom detail arrives.

### FFT samples

Samples is the FFT window size, not the song's sample rate or the image width. A smaller window gives finer timing but coarser frequency detail. A larger window gives finer frequency detail but spreads a short event across a longer time interval. At 48 kHz, 512 samples span about 11 ms, 2048 span 43 ms, and 8192 span 171 ms. The FFT bin spacing is the sample rate divided by the window size. The window function also affects how well nearby frequencies can be separated.

Use smaller windows to inspect brief events and larger windows to inspect sustained tones or spectral edges. Zoom level alone does not determine the best setting. Background refinement keeps the chosen FFT size.

Whole-file views and light time zoom retain the existing fast analysis. At 4x time zoom or deeper, background refinement uses a dense, file-anchored FFT grid, centred windows and time-weighted linear-power pixels. This spends the extra CPU on the zoomed passage, not initial loading. Crossing the quality threshold can change brightness and event placement slightly because the two modes use different measurements. See [the design decisions and benchmarks](docs/fixed-grid-analysis.md).

### Command line

```
Spek-Z [FILE] [PNG] [WIDTH] [HEIGHT]
```

With a PNG path, Spek-Z saves the spectrogram at WIDTH × HEIGHT pixels (1000 × 600 by default) and exits without opening a window. It returns 0 on success and 1 if the file or ffmpeg can't be read. In spek-X, WIDTH and HEIGHT set the window size, so the PNG comes out smaller than the numbers you pass.

## Releasing

`.github/workflows/release.yml` publishes a version when its tag is pushed:

1. Set `<Version>` in `Directory.Build.props`, commit, and push.
2. Tag the commit with the same version and push the tag: `git tag v0.3.0-preview.1` and `git push origin v0.3.0-preview.1`.

The workflow runs the tests, checks that the tag matches the version, and pushes Spek.NET and Spek.NET.WinUI to nuget.org with their symbol packages. It attaches those packages to a GitHub release too. A tag with a hyphen, such as `v0.2.0-beta.1`, makes a prerelease. Running the workflow by hand from the Actions tab builds the packages without publishing.

The workflow doesn't distribute portable app ZIPs. Microsoft Store packaging for the standalone app is a separate step. Local development builds still run as an unpackaged app.

nuget.org uses Trusted Publishing, so the repository stores no API key. It needs a policy on nuget.org with owner `DerekYang2`, repository `spek-z` and workflow `release.yml`, plus a repository secret `NUGET_USER` that holds the nuget.org profile name. Update the policy's repository name after the GitHub rename before publishing another version.

## Differences from Spek

- Detailed zoom uses file-anchored, centred FFTs and linear-power pixel averages. Normal overviews retain the fast Spek-style scheduling and dB averaging. The original pipeline also fixes Spek's repeated frame-counting bug.
- Values at or above the upper dB limit are red in the Spectrum palette. Spek's palette function skips the top value, so those pixels come out black. That shows when the limit is lowered with `U`, because with Hann windowing nothing reaches the default 0 dB limit.
- The Blackman-Harris window uses the exact cosine terms. Spek reads them from a table indexed modulo the window size.
- Changing the palette or the dB range recolours the existing analysis instead of decoding the file again.

## Credits

Based on [Spek](https://github.com/alexkay/spek) and [spek-X](https://github.com/MikeWang000000/spek-X).

## License

GPL-3.0, the same as Spek. See [LICENSE](LICENSE).
