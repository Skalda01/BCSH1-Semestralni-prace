using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using SkalaView;

namespace SkalaView.Browser;

internal sealed partial class Program
{
    private static Task Main(string[] args)
    {
        BrowserEnvironment.Configure(args);

        return BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
