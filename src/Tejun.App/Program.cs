using Avalonia;

namespace Tejun.App;

internal static class Program
{
    public static string[] Args { get; private set; } = Array.Empty<string>();

    [STAThread]
    public static int Main(string[] args)
    {
        Args = args;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return Environment.ExitCode;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
