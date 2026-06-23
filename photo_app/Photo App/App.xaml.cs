using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace SocialPrepTool;

public partial class App : Application
{
    private Window m_window;
    public static MainWindow MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try { System.IO.File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), $"Unhandled exception: {e.ExceptionObject}"); } catch {}
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            try { System.IO.File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), $"Unobserved task exception: {e.Exception}"); } catch {}
        };

        this.UnhandledException += (s, e) =>
        {
            try { System.IO.File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), $"XAML Unhandled exception: {e.Message}\n{e.Exception}"); } catch {}
        };
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        m_window = new MainWindow();
        MainWindow = (MainWindow)m_window;
        m_window.Activate();
    }
}
