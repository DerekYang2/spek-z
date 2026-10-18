# Fixed-grid analysis

This change adds dense analysis for deep zoom. It does not apply that CPU cost to initial loading or slight zoom. It does not change the FFT size automatically or eliminate the time/frequency tradeoff.

## When it runs

The whole-file overview and light time zoom retain the original 0.2.0 algorithm, including its column-dependent FFT scheduling and dB average. At 4x time zoom or deeper, the control opts into detailed analysis for the visible interval. For example, a 10-second view of a 3-minute track qualifies; a slight zoom of the whole track does not.

`AnalysisSettings.Quality` defaults to Fast. Detailed analysis is explicit in the core API. The control selects it for deep refinement only, and cache reuse includes the quality tier so a broad fast result cannot substitute for a detailed one at the threshold. Reset always returns to the cached fast overview.

This two-tier design intentionally preserves ordinary loading performance. Its tradeoff is a possible brightness/timing change when the detailed result replaces the fast preview. Consistency guarantees below apply between detailed analyses, not between the legacy overview and the detailed mode.

## Measurement grid

In detailed mode, for FFT size N and sample rate R, the analyser uses a hop in samples of:

```text
H = min(N / 16, max(1, round(R / 1000)))
centres = 0, H, 2H, 3H, ...
window at centre C = [C - N/2, C + N/2)
```

The hop is about 1 ms or less, with at least 93.75% overlap. It depends on FFT size and sample rate, never on the number of image columns or the visible time range. At 48 kHz, 512 samples use a 32-sample hop, and 2048/8192/16384 samples use a 48-sample hop.

A long FFT still mixes audio across a long time window. More overlapping measurements sample that result densely; they do not turn an 8192-sample window into a short observation. Zoom beyond the measurement spacing reuses the nearest cells rather than inventing independent FFTs per pixel.

## Centred timestamps and context

Each FFT is centred on its timestamp rather than ending there. This removes the systematic half-window delay of a trailing analysis. The window still spreads transient energy over time; the change makes that spread symmetric rather than placing it after the event.

Range analysis starts at the first required window's left edge and decodes through the final required window's right edge. That includes real audio outside the visible passage. It uses zero padding only beyond the file's boundaries. Finishing an unexpectedly short decode pads the final FFT context, not the rest of an overestimated file duration.

The grid is anchored at the start of the file. Seeking into a passage does not restart it at the passage boundary. Compressed-decoder seek behavior and metadata timing precision can still limit exact cross-decode agreement; the strict equivalence tests use lossless audio.

## Drawing measurements into pixels

Each measurement owns the time cell nearest its centre. A pixel averages the linear power of cells that overlap its time interval, weighted by the overlap duration. Conversion to dB happens after that average.

For example, equal time at power 1 and power 0 produces power 0.5, about -3 dB. Averaging 0 dB and negative infinity directly would incorrectly erase the result. Quiet and silent measurements must contribute their proper energy, not dominate through logarithmic averaging.

Pixel boundaries use exact integer ratios with Int128 intermediate coordinates, so more pixels than audio samples do not produce empty-time pixels. Aligned groups of fine pixels aggregate to the corresponding coarse pixel's power, within floating-point precision.

Detailed pixels show mean energy, not peak hold. Peak hold would preserve brief maxima but could overemphasize an occasional tone or noise burst; it is not added. The normal fast overview deliberately retains its old averaging behavior.

## Scale and appearance

The existing normalized FFT power scale, window functions, palettes and dB limits remain. No audio gain is applied. This is not a calibrated power spectral density, and levels from different FFT sizes are not a universal loudness comparison.

Detailed content can appear brighter than the fast preview because arithmetic power averages differ from dB averages. Centred timestamps can also move energy relative to the old trailing-window view. This is a measurement change, not just a sharpening filter. The FFT window still determines real time/frequency resolution. Image interpolation also remains a display choice and can soften drawn pixels.

The profile and cutoff continue to use the cached overview's selected passage. Zoom refinement does not change that selection or replace those measurements. The 350 ms refinement debounce and immediate cached overview reset are unchanged.

## Work, memory and cancellation

The pipeline streams. It keeps a ring buffer, FFT work arrays and one column's power sums, not a whole-file history of dense FFTs. The existing overview/detail pixel buffers and one-interval cache remain. Extra working memory scales with FFT size, not track duration.

The main cost is CPU in detailed mode. The UI does not run detailed whole-track analysis. Deep range refinement only analyses the visible interval plus context. Cancellation is checked between dense FFTs as well as at decoder reads, and work remains off the UI thread.

### Synthetic benchmark

One warmed run on this development machine, 48 kHz mono noise, 1000 output columns, Release build. These timings compare the legacy algorithm with explicitly requested detailed analysis. The expensive detailed 30-second overview is a diagnostic, not the normal UI path. Timings include pipeline analysis and pixel colouring, not decoding or GPU rendering. They are observations, not performance guarantees.

| FFT | 0.2.0, 30 s overview | Fixed grid, 30 s overview | 0.2.0, 1 s crop | Fixed grid, 1 s crop |
| --- | ---: | ---: | ---: | ---: |
| 512 | 59 ms | 283 ms | 33 ms | 18 ms |
| 2048 | 94 ms | 852 ms | 49 ms | 53 ms |
| 8192 | 240 ms | 3553 ms | 217 ms | 217 ms |
| 16384 | 467 ms | 8051 ms | 477 ms | 482 ms |

Allocation stayed within about 0.07 MiB of the previous pipeline for each case. The 1000-column 16384-sample benchmark uses larger output buffers than the UI's 32 MiB refinement cap permits; the benchmark keeps the output dimensions identical between implementations.

After scoping detailed analysis to deep zoom, a separate warmed 20-second FLAC overview benchmark through the analyser measured 116 ms with published 0.2.0 and 111 ms with the default Fast path at 2048 samples. At 8192 samples it measured 251 ms and 208 ms. These single-run differences are normal timing variability; the relevant result is that normal loading still uses the old algorithm rather than the expensive detailed path.

## Verification

- Coarse pixels equal the energy average of aligned fine pixels.
- Crops share the overview's global grid, including odd hop sizes and file edges.
- Lossless ffmpeg crops match corresponding overview pixels at 44.1, 48 and 96 kHz within 0.0001 dB.
- Decoder chunk sizes do not change output, and FFT count does not depend on image width.
- An impulse stays near its actual timestamp instead of shifting by half an FFT.
- Silence, partial cells, early EOF, extra samples, fractional pixel widths and cancellation have dedicated tests.
- Existing channel, multi-stream, MP3/Opus range, cutoff and FFT reference tests remain in the suite.
- The fast path matches reference values captured from the published 0.2.0 package, and the analyser defaults to Fast.
- Control checks verify fast initial loading, fast light zoom, the exact 4x threshold, and replacement of a nearly identical fast cached result when detailed quality is required.
