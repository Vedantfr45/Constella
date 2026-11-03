using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Constella.Core;

namespace Constella;

/// <summary>The WPF application. Runs from the tray; there is no main window.</summary>
public sealed class App : Application
{
    /// <summary>Passed when Windows starts the app at sign-in: start quietly, without opening settings.</summary>
    public const string BackgroundArgument = "--background";

    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // An overlay should never vanish because of one bad frame: log and carry on.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unexpected error", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Fatal error", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Background task error", args.Exception);
            args.SetObserved();
        };

        System.Windows.Forms.Application.EnableVisualStyles();
        _controller = new AppController(Dispatcher);
        _controller.Start();

        if (!e.Args.Contains(BackgroundArgument))
        {
            _controller.OpenSettings();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        Log.Info("Constella stopped");
        base.OnExit(e);
    }

    /// <summary>Opens the settings window (used when the user launches Constella a second time).</summary>
    internal void ShowSettings() => _controller?.OpenSettings();
}

/// <summary>Entry point. Allows one copy; launching again just opens the running copy's settings.</summary>
public static class Program
{
    private const string MutexName = @"Local\Constella.SingleInstance";
    private const string ShowSettingsEventName = @"Local\Constella.ShowSettings";

    [STAThread]
    public static int Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool firstInstance);
        using var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        if (!firstInstance)
        {
            showSettings.Set();
            return 0;
        }

        var app = new App();
        var wait = ThreadPool.RegisterWaitForSingleObject(showSettings,
            (_, _) => app.Dispatcher.InvokeAsync(app.ShowSettings, DispatcherPriority.Normal),
            null, Timeout.Infinite, executeOnlyOnce: false);
        try
        {
            return app.Run();
        }
        finally
        {
            wait.Unregister(null);
        }
    }
}
