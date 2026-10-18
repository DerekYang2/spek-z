using System.Globalization;

namespace SpekZ;

// spek's command line: Spek-Z [FILE] [PNG] [WIDTH] [HEIGHT]. With a PNG path, the spectrogram is saved and the app
// exits. WIDTH and HEIGHT are the size of the image.
internal sealed record CommandLine(string? FilePath, string? PngPath, int Width, int Height, bool ShowHelp, bool ShowVersion)
{
    public const int DefaultWidth = 1000;
    public const int DefaultHeight = 600;

    public const string Help = """
        Usage: Spek-Z [FILE] [PNG] [WIDTH] [HEIGHT]

          FILE      Audio file to analyse.
          PNG       Save the spectrogram to this PNG file and exit.
          WIDTH     Width of the PNG in pixels. Default: 1000.
          HEIGHT    Height of the PNG in pixels. Default: 600.

          -h, --help       Show this help.
          -V, --version    Show the version.
        """;

    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        var positional = new List<string>();
        bool help = false, version = false;
        foreach (var arg in args)
        {
            switch (arg)
            {
                case "-h" or "--help" or "/?":
                    help = true;
                    break;
                case "-V" or "--version":
                    version = true;
                    break;
                default:
                    positional.Add(arg);
                    break;
            }
        }

        int width = positional.Count > 2 && int.TryParse(positional[2], NumberStyles.None, CultureInfo.InvariantCulture, out int w) && w > 0 ? w : DefaultWidth;
        int height = positional.Count > 3 && int.TryParse(positional[3], NumberStyles.None, CultureInfo.InvariantCulture, out int h) && h > 0 ? h : DefaultHeight;
        return new CommandLine(positional.ElementAtOrDefault(0), positional.ElementAtOrDefault(1), width, height, help, version);
    }
}
