using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CommandCapture.App.ViewModels;
using CommandCapture.App.Views;
using CommandCapture.Audio;
using CommandCapture.Camera;

namespace CommandCapture.App;

public partial class App : Application
{
    private MainViewModel? _viewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var stills = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "CommandCapture");
            Directory.CreateDirectory(stills);
            _viewModel = new MainViewModel(
                new PreviewCameraSession(stills),
                new WasapiPcmSource(),
                new AvaloniaUiMarshal(),
                Path.Combine(AppContext.BaseDirectory, "commands.json"),
                stills);
            desktop.MainWindow = new MainWindow
            {
                DataContext = _viewModel,
            };
            desktop.ShutdownRequested += (_, _) => _viewModel.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}