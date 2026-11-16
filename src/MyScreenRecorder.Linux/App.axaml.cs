using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MyScreenRecorder.Platform.Linux;

namespace MyScreenRecorder.Linux;

public partial class App : Application
{
    public SessionCheckResult Session { get; init; } = new(SessionKind.X11, null);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(Session);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
