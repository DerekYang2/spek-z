using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Spek.Net;
using Spek.Net.WinUI;

namespace SpekZ;

public partial class App : Application
{
    private MainWindow? window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var commandLine = CommandLine.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray());
        if (commandLine.ShowHelp || commandLine.ShowVersion || commandLine.PngPath is not null)
        {
            // Like spek, rendering to a PNG doesn't open a window. The exit code reports the result.
            Environment.Exit(await RunCommandLineAsync(commandLine));
            return;
        }

        window = new MainWindow(commandLine.FilePath);
        window.Activate();
    }

    private static async Task<int> RunCommandLineAsync(CommandLine commandLine)
    {
        AttachConsole(AttachParentProcess);
        if (commandLine.ShowHelp)
        {
            Console.WriteLine(CommandLine.Help);
            return 0;
        }
        if (commandLine.ShowVersion)
        {
            Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
            return 0;
        }
        if (commandLine.FilePath is null || commandLine.PngPath is null)
        {
            Console.Error.WriteLine(CommandLine.Help);
            return 2;
        }

        var settings = AppSettings.Load();
        var ffmpegPath = AppSettings.FindFfmpeg(settings);
        if (ffmpegPath is null)
        {
            Console.Error.WriteLine($"{AppInfo.Name}: cannot find ffmpeg.");
            return 1;
        }

        try
        {
            var options = new ExportOptions(
                new AnalysisSettings(FftBits: settings.FftBits, WindowFunction: settings.WindowFunction),
                settings.Palette,
                settings.LowerDb,
                settings.UpperDb,
                settings.ShowFullPath,
                settings.ShowDetailedDescription,
                AppInfo.Name,
                AppInfo.Version,
                ReferenceLines: settings.ReferenceLines,
                ShowCutoffMarker: settings.ShowCutoffMarker);
            var info = await SpekExporter.ExportAsync(new Ffmpeg(ffmpegPath), Path.GetFullPath(commandLine.FilePath),
                Path.GetFullPath(commandLine.PngPath), commandLine.Width, commandLine.Height, options);
            if (info.Error != AudioError.None)
            {
                Console.Error.WriteLine($"{AppInfo.Name}: {SpekDescription.ErrorText(info.Error)}: {commandLine.FilePath}");
                return 1;
            }
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException or FfmpegException)
        {
            Console.Error.WriteLine($"{AppInfo.Name}: {e.Message}");
            return 1;
        }
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
