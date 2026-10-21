using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace MCG.Launcher.Services;

internal static class DialogDiagnostics
{
    public static async Task CheckSettingsButtonAsync(Button button, Task completed, string reportPath)
    {
        try
        {
            var peer = new ButtonAutomationPeer(button);
            var invoke = (IInvokeProvider)peer.GetPattern(PatternInterface.Invoke);
            invoke.Invoke();
            await completed.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { Opened = false, Rendered = false, Error = error.ToString() }));
        }
    }

    // Exercise the real WinUI dialog without changing the user's settings or starting games.
    public static async Task CheckSettingsAsync(ContentDialog dialog, ToggleSwitch toggle, string reportPath)
    {
        var opened = false;
        var rendered = false;
        string? error = null;
        var timer = dialog.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(5);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => { error = "O modal não abriu em 5 segundos."; dialog.Hide(); };
        dialog.Opened += (_, _) =>
        {
            opened = true;
            dialog.DispatcherQueue.TryEnqueue(() =>
            {
                rendered = dialog.Template is not null && toggle is { ActualWidth: > 0, ActualHeight: > 0 };
                if (!rendered) error = "O modal abriu sem renderizar a opção.";
                dialog.Hide();
            });
        };
        try
        {
            timer.Start();
            await dialog.ShowAsync();
        }
        catch (Exception exception) { error = exception.ToString(); }
        finally { timer.Stop(); }
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { Opened = opened, Rendered = rendered, Error = error }));
    }
}
