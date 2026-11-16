using Avalonia.Controls;
using MyScreenRecorder.Platform.Linux;

namespace MyScreenRecorder.Linux;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(SessionCheckResult session) : this()
    {
        if (session.Kind == SessionKind.Wayland)
        {
            TitleText.IsVisible = false;
            StatusText.Text = session.Message;
        }
    }
}
