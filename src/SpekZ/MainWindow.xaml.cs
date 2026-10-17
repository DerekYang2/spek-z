using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Spek.Net;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using SpekView = Spek.Net.WinUI.SpekView;

namespace SpekZ;

public sealed partial class MainWindow : Window
{
    private static readonly string[] AudioExtensions =
    [
        ".aac", ".aif", ".aiff", ".alac", ".ape", ".dsf", ".flac", ".m4a", ".mka", ".mkv", ".mp2", ".mp3", ".mp4",
        ".mpc", ".oga", ".ogg", ".opus", ".tta", ".wav", ".webm", ".wma", ".wv",
    ];

    private readonly AppSettings settings;

    public MainWindow(string? filePath)
    {
        InitializeComponent();
        settings = AppSettings.Load();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        RestorePlacement();

        Spek.BrandName = AppInfo.Name;
        Spek.BrandVersion = AppInfo.Version;
        Spek.FfmpegPath = AppSettings.FindFfmpeg(settings);
        Spek.Palette = settings.Palette;
        Spek.WindowFunction = settings.WindowFunction;
        Spek.FftBits = settings.FftBits;
        Spek.LowerDb = settings.LowerDb;
        Spek.UpperDb = settings.UpperDb;
        Spek.ShowFullPath = settings.ShowFullPath;
        Spek.ShowDetailedDescription = settings.ShowDetailedDescription;
        Spek.ReferenceLines = settings.ReferenceLines;
        Spek.ShowSpectrum = settings.ShowSpectrum;
        Spek.ShowCutoffMarker = settings.ShowCutoffMarker;
        ReferenceLinesBox.SelectedIndex = (int)settings.ReferenceLines;
        SpectrumToggle.IsChecked = settings.ShowSpectrum;
        CutoffToggle.IsChecked = settings.ShowCutoffMarker;
        Spek.InspectionChanged += (_, _) => UpdateInspection();
        Spek.RegisterPropertyChangedCallback(SpekView.RefinementStatusProperty, (_, _) => UpdateInspection());
        UpdateInspection();

        foreach (var property in new[]
        {
            SpekView.PaletteProperty, SpekView.WindowFunctionProperty, SpekView.FftBitsProperty, SpekView.ChannelProperty,
            SpekView.StreamProperty, SpekView.ChannelCountProperty, SpekView.StreamCountProperty, SpekView.FilePathProperty,
        })
        {
            Spek.RegisterPropertyChangedCallback(property, (_, _) => UpdateCommands());
        }
        UpdateCommands();

        Closed += (_, _) => SaveSettings();
        if (filePath is not null) OpenFile(Path.GetFullPath(filePath));
        Root.Loaded += (_, _) => Spek.Focus(FocusState.Programmatic);
        Root.SizeChanged += (_, _) => Profile.Height = Root.ActualHeight < 720 ? 140 : 180;
    }

    private void OpenFile(string path)
    {
        Spek.FilePath = path;
        Title = $"{Path.GetFileName(path)} - {AppInfo.Name}";
        AppTitleBar.Subtitle = Path.GetFileName(path);
        Spek.Focus(FocusState.Programmatic);
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker(AppWindow.Id) { SuggestedStartLocation = PickerLocationId.MusicLibrary };
        foreach (var extension in AudioExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }
        picker.FileTypeFilter.Add("*");

        var result = await picker.PickSingleFileAsync();
        if (result is not null) OpenFile(result.Path);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(Spek.FilePath)) return;

        var picker = new FileSavePicker(AppWindow.Id)
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(Spek.FilePath),
            DefaultFileExtension = ".png",
        };
        picker.FileTypeChoices.Add("PNG image", [".png"]);

        var result = await picker.PickSaveFileAsync();
        if (result is not null) await Spek.SaveAsync(result.Path);
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems)) e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        if (items.OfType<StorageFile>().FirstOrDefault() is { } file) OpenFile(file.Path);
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        FullPathToggle.IsOn = Spek.ShowFullPath;
        DetailedToggle.IsOn = Spek.ShowDetailedDescription;
        UpperBox.Value = Spek.UpperDb;
        LowerBox.Value = Spek.LowerDb;
        FfmpegBox.Text = settings.FfmpegPath ?? "";
        FfmpegStatus.Text = Spek.FfmpegPath is { } found ? $"Using {found}" : "ffmpeg wasn't found.";

        SettingsDialog.XamlRoot = Content.XamlRoot;
        await SettingsDialog.ShowAsync();

        Spek.ShowFullPath = FullPathToggle.IsOn;
        Spek.ShowDetailedDescription = DetailedToggle.IsOn;
        int upper = double.IsNaN(UpperBox.Value) ? Spek.UpperDb : (int)UpperBox.Value;
        int lower = double.IsNaN(LowerBox.Value) ? Spek.LowerDb : (int)LowerBox.Value;
        if (lower < upper)
        {
            Spek.LowerDb = Math.Max(lower, SpekDefaults.MinDb);
            Spek.UpperDb = Math.Min(upper, SpekDefaults.MaxDb);
        }

        var ffmpeg = FfmpegBox.Text.Trim().Trim('"');
        settings.FfmpegPath = ffmpeg.Length == 0 ? null : ffmpeg;
        var resolved = AppSettings.FindFfmpeg(settings);
        if (resolved != Spek.FfmpegPath) Spek.FfmpegPath = resolved;

        SaveSettings();
        Spek.Focus(FocusState.Programmatic);
    }

    private void Menu_Opening(object? sender, object e)
    {
        if (sender is not MenuFlyout menu) return;
        menu.Items.Clear();

        if (menu == PaletteMenu)
        {
            foreach (var palette in Enum.GetValues<Palette>())
                Add(menu, Palettes.DisplayName(palette), Spek.Palette == palette, () => Spek.Palette = palette);
        }
        else if (menu == WindowMenu)
        {
            foreach (var function in Enum.GetValues<WindowFunction>())
                Add(menu, WindowFunctions.DisplayName(function), Spek.WindowFunction == function, () => Spek.WindowFunction = function);
        }
        else if (menu == FftMenu)
        {
            for (int bits = SpekDefaults.MinFftBits; bits <= SpekDefaults.MaxFftBits; bits++)
            {
                int value = bits;
                Add(menu, $"{1 << bits} samples", Spek.FftBits == bits, () => Spek.FftBits = value);
            }
        }
        else if (menu == ChannelMenu)
        {
            for (int channel = 0; channel < Spek.ChannelCount; channel++)
            {
                int value = channel;
                Add(menu, $"Channel {channel + 1}", Spek.Channel == channel, () => Spek.Channel = value);
            }
        }
        else if (menu == StreamMenu)
        {
            for (int stream = 0; stream < Spek.StreamCount; stream++)
            {
                int value = stream;
                Add(menu, $"Stream {stream + 1}", Spek.Stream == stream, () => Spek.Stream = value);
            }
        }

        static void Add(MenuFlyout menu, string text, bool isChecked, Action select)
        {
            var item = new RadioMenuFlyoutItem { Text = text, IsChecked = isChecked, GroupName = menu.GetHashCode().ToString() };
            item.Click += (_, _) => select();
            menu.Items.Add(item);
        }
    }

    private void UpdateCommands()
    {
        PaletteButton.Label = Palettes.DisplayName(Spek.Palette);
        WindowButton.Label = WindowFunctions.DisplayName(Spek.WindowFunction);
        FftButton.Label = $"{1 << Spek.FftBits}";
        ChannelButton.Label = Spek.ChannelCount > 0 ? $"Channel {Spek.Channel + 1} / {Spek.ChannelCount}" : "Channel";
        ChannelButton.IsEnabled = Spek.ChannelCount > 1;
        StreamButton.Label = $"Stream {Spek.Stream + 1} / {Spek.StreamCount}";
        StreamButton.Visibility = Spek.StreamCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = !string.IsNullOrEmpty(Spek.FilePath);
    }

    private void ReferenceLines_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Spek is null || ReferenceLinesBox.SelectedIndex < 0) return;
        Spek.ReferenceLines = (ReferenceLines)ReferenceLinesBox.SelectedIndex;
        UpdateInspection();
    }

    private void InspectionOption_Changed(object sender, RoutedEventArgs e)
    {
        if (Spek is null) return;
        bool hidingSpectrum = Spek.ShowSpectrum && SpectrumToggle.IsChecked != true;
        Spek.ShowSpectrum = SpectrumToggle.IsChecked == true;
        Spek.ShowCutoffMarker = CutoffToggle.IsChecked == true;
        if (hidingSpectrum) Spek.ClearSelection();
        UpdateInspection();
    }

    private void ResetZoom_Click(object sender, RoutedEventArgs e)
    {
        Spek.ResetZoom();
        Spek.Focus(FocusState.Programmatic);
    }

    private void UpdateInspection()
    {
        Profile.Visibility = Spek.ShowSpectrum ? Visibility.Visible : Visibility.Collapsed;
        Profile.Profile = Spek.SpectrumProfile;
        Profile.FrequencyRange = Spek.VisibleFrequencyRange;
        Profile.MeasuredDrop = Spek.ShowCutoffMarker ? Spek.MeasuredDrop : null;
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(Spek.RefinementStatus)) parts.Add(Spek.RefinementStatus);
        var time = Spek.VisibleTimeRange;
        if (Spek.MinimumTime > 0 || Spek.MaximumTime > 0)
            parts.Add($"Time view {time.Start:0.00} to {time.End:0.00} s");
        var frequency = Spek.VisibleFrequencyRange;
        if (Spek.MinimumFrequency > 0 || Spek.MaximumFrequency > 0)
            parts.Add($"Frequency view {frequency.Minimum / 1000:0.00} to {frequency.Maximum / 1000:0.00} kHz");
        if (Spek.HasSelection)
            parts.Add($"Passage {Spek.SelectionStartSeconds:0.00} to {Spek.SelectionEndSeconds:0.00} s");
        else if (Spek.ShowSpectrum || Spek.ShowCutoffMarker)
            parts.Add("Using the whole track");
        if (Spek.ReferenceLines == ReferenceLines.Mp3Lame)
            parts.Add("Guides are nominal LAME stereo CBR low-pass targets, not universal MP3 boundaries.");
        if (Spek.ShowCutoffMarker)
        {
            if (Spek.SpectrumProfile is null) parts.Add("Cutoff measurement waits for analysis.");
            else if (Spek.MeasuredDrop is { } drop) parts.Add($"Measured spectral drop near {drop.FrequencyHz / 1000:0.00} kHz, about {drop.DropDb:0} dB.");
            else parts.Add("No qualifying sustained spectral drop found.");
            parts.Add("A cutoff does not prove lossy encoding; no marker does not prove lossless origin.");
        }
        InspectionStatus.Text = string.Join(" / ", parts);
    }

    private void RestorePlacement()
    {
        if (settings.WindowWidth is int width && settings.WindowHeight is int height && settings.WindowX is int x && settings.WindowY is int y)
        {
            var rect = new RectInt32(x, y, width, height);
            if (DisplayArea.GetFromRect(rect, DisplayAreaFallback.None) is not null)
            {
                AppWindow.MoveAndResize(rect);
                if (settings.WindowMaximized && AppWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
                return;
            }
        }

        double scale = GetDpiForWindow(Win32Interop.GetWindowFromWindowId(AppWindow.Id)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1000 * scale), (int)(680 * scale)));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void SaveSettings()
    {
        settings.Palette = Spek.Palette;
        settings.WindowFunction = Spek.WindowFunction;
        settings.FftBits = Spek.FftBits;
        settings.LowerDb = Spek.LowerDb;
        settings.UpperDb = Spek.UpperDb;
        settings.ShowFullPath = Spek.ShowFullPath;
        settings.ShowDetailedDescription = Spek.ShowDetailedDescription;
        settings.ReferenceLines = Spek.ReferenceLines;
        settings.ShowSpectrum = Spek.ShowSpectrum;
        settings.ShowCutoffMarker = Spek.ShowCutoffMarker;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            settings.WindowMaximized = presenter.State == OverlappedPresenterState.Maximized;
            if (presenter.State == OverlappedPresenterState.Restored)
            {
                settings.WindowX = AppWindow.Position.X;
                settings.WindowY = AppWindow.Position.Y;
                settings.WindowWidth = AppWindow.Size.Width;
                settings.WindowHeight = AppWindow.Size.Height;
            }
        }
        settings.Save();
    }
}
