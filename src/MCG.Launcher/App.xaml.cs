using Microsoft.UI.Xaml;

namespace MCG.Launcher;

public partial class App : Application
{
    private Window? _window;
    private Mutex? _instanceMutex;
    public App() => InitializeComponent();
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Keep one writer for the local library, even if the executable is opened twice.
        _instanceMutex = new Mutex(true, @"Local\MCGLauncher", out var firstInstance);
        if (!firstInstance)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Exit();
            return;
        }
        var commandLine = Environment.GetCommandLineArgs();
        var checkIndex = Array.IndexOf(commandLine, "--check-settings-dialog");
        var dialogReport = checkIndex >= 0 && checkIndex + 1 < commandLine.Length ? commandLine[checkIndex + 1] : null;
        _window = new MainWindow(dialogReport);
        _window.Closed += (_, _) =>
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
        };
        _window.Activate();
    }
}
