using System;
using System.Windows;
using System.Windows.Threading;

namespace MyScreenRecorder;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            MessageBox.Show(args.ExceptionObject?.ToString() ?? "Unknown error", "MyScreenRecorder", MessageBoxButton.OK, MessageBoxImage.Error);
        base.OnStartup(e);
    }

    private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.ToString(), "MyScreenRecorder", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
