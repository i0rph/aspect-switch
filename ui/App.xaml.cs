#nullable enable
using Microsoft.UI.Xaml;
namespace AspectSettings;
public partial class App : Application
{
    private Window? window;
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => {
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "AspectSwitch-ui-error.txt"), e.Exception.ToString()); } catch { }
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try { window = new SettingsWindow(new EditorSession(Environment.GetCommandLineArgs())); window.Activate(); }
        catch (Exception e) { File.WriteAllText(Path.Combine(Path.GetTempPath(), "AspectSwitch-ui-error.txt"), e.ToString()); Environment.ExitCode = 1; Exit(); }
    }
}
